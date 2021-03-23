using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Net;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Utils;
    using Core.Systems;
    using Core.Controller;

    using Shared.Items;
    using Shared.Localization;
    using Shared.Math;
    using Shared.Utils;

    using Gameplay.LegislationSystem;
    using Gameplay.Civics.GameValues;
    using Gameplay.Civics.Misc;
    using Gameplay.Civics.Districts;
    using Gameplay.GameActions;
    using Gameplay.Utils;

    public class ImportContext
    {
        public IList<IHasID> ImportedObjects { get; } = new List<IHasID>();

        public IDictionary<CivicReference, IHasID> ReferenceMap { get; } = new Dictionary<CivicReference, IHasID>();

        public void Import(BundledCivic bundledCivic)
        {
            var registrar = Registrars.Get(bundledCivic.Type);
            if (registrar == null)
            {
                throw new InvalidOperationException($"No registrar found for type '{bundledCivic.Type.FullName}'");
            }
            var obj = Activator.CreateInstance(bundledCivic.Type) as IHasID;
            registrar.Insert(obj);

            try
            {
                if (obj is IProposable proposable)
                {
                    proposable.InitializeDraftProposable();
                    Importer.DeserialiseGenericObject(bundledCivic.Data, obj);
                    proposable.SetProposedState(ProposableState.Draft, true, true);
                }
                else
                {
                    Importer.DeserialiseGenericObject(bundledCivic.Data, obj);
                }
            }
            finally
            {
                ImportedObjects.Add(obj);
                ReferenceMap.Add(new CivicReference(bundledCivic.Type, bundledCivic.Name), obj);
            }
        }

        public void Clear()
        {
            Importer.Cleanup(ImportedObjects);
            ImportedObjects.Clear();
            ReferenceMap.Clear();
        }
    }

    public static class Importer
    {
        private static readonly Regex matchNumberAtEnd = new Regex(@"[0-9]+$", RegexOptions.Compiled);

        private static readonly WebClient webClient = new WebClient();

        public static IEnumerable<IHasID> Import(string source)
        {
            string text;
            if (Uri.TryCreate(source, UriKind.Absolute, out Uri uri))
            {
                text = webClient.DownloadString(uri);
            }
            else
            {
                text = File.ReadAllText(Path.Combine(CivicsImpExpPlugin.ImportExportDirectory, source));
            }
            var bundle = CivicBundle.LoadFromText(text);
            return bundle.ImportAll();
        }

        public static void Cleanup(IHasID obj)
        {
            Registrars.Remove(obj);
            if (obj is IHasSubRegistrarEntries hasSubRegistrarEntries)
            {
                foreach (var subObj in hasSubRegistrarEntries.SubRegistrarEntries)
                {
                    Cleanup(subObj);
                }
            }
        }

        public static void Cleanup(IEnumerable<IHasID> objs)
        {
            foreach (var obj in objs)
            {
                Cleanup(obj);
            }
        }

        #region Deserialisation

        private static void Deserialise(object target, JObject rootObj)
        {
            var versionArr = rootObj.Value<JArray>("version");
            int verMaj = versionArr.Value<int>(0);
            //int verMin = versionArr.Value<int>(1);
            if (verMaj != CivicsJsonConverter.MajorVersion)
            {
                throw new InvalidOperationException($"Civic format not supported (found major '{verMaj}', expecting '{CivicsJsonConverter.MajorVersion}'");
            }
            string importType = rootObj.Value<string>("type");
            if (importType != target.GetType().FullName)
            {
                throw new InvalidOperationException($"Civic type mismatch (found '{importType}', expecting '{target.GetType().FullName}'");
            }
            DeserialiseGenericObject(rootObj, target);
        }

        private static string GetUniqueName(Registrar registrar, string original)
        {
            string name = original;
            while (registrar.GetByName(name) != null)
            {
                var numberAtEndMatch = matchNumberAtEnd.Match(name);
                if (numberAtEndMatch.Success)
                {
                    int num = int.Parse(numberAtEndMatch.Value);
                    name = $"{name.Substring(0, name.Length - numberAtEndMatch.Length)}{num + 1}";
                }
                else
                {
                    name = $"{name} 2";
                }
            }
            return name;
        }

        private static void DeserialiseObjectProperties(object target, JObject obj)
        {
            foreach (var pair in obj)
            {
                var targetProperty = target.GetType().GetProperty(pair.Key, BindingFlags.Public | BindingFlags.Instance);
                if (targetProperty == null)
                {
                    Logger.Debug($"Json object has property '{pair.Key}' but no such property exists on '{target.GetType().FullName}', skipping");
                    continue;
                }
                DeserialiseValue(target, targetProperty, pair.Value);
            }
        }

        private static void DeserialiseValue(object target, PropertyInfo propertyInfo, JToken value)
        {
            if (propertyInfo.PropertyType.IsConstructedGenericType && typeof(ControllerList<>).IsAssignableFrom(propertyInfo.PropertyType.GetGenericTypeDefinition()))
            {
                if (value.Type != JTokenType.Array)
                {
                    Logger.Error($"Can't deserialise {value.Type} into '{target.GetType().FullName}.{propertyInfo.Name}' (expecting Array)");
                    return;
                }
                DeserialiseControllerList(propertyInfo.GetValue(target), value.ToObject<JArray>());
            }
            else if (propertyInfo.SetMethod != null)
            {
                propertyInfo.SetValue(target, DeserialiseValueAsType(value, propertyInfo.PropertyType));
            }
            else
            {
                if (value == null)
                {
                    Logger.Error($"Can't deserialise value into '{target.GetType().FullName}.{propertyInfo.Name}' as we don't know how (it's a {propertyInfo.PropertyType.FullName} and we got a null)");
                }
                else
                {
                    Logger.Error($"Can't deserialise value into '{target.GetType().FullName}.{propertyInfo.Name}' as we don't know how (it's a {propertyInfo.PropertyType.FullName} and we got a {value.Type})");
                }
            }
        }

        private static object DeserialiseValueAsType(JToken token, Type expectedType)
        {
            if (token.Type == JTokenType.String)
            {
                var str = token.ToObject<JValue>().Value as string;
                if (expectedType.IsAssignableFrom(typeof(string)))
                {
                    return str;
                }
                else if (expectedType.IsAssignableFrom(typeof(LocString)))
                {
                    return new LocString(str);
                }
                else if (expectedType.IsEnum)
                {
                    return Enum.Parse(expectedType, str);
                }
            }
            else if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float || token.Type == JTokenType.Boolean)
            {
                return Convert.ChangeType(token.ToObject<JValue>().Value, expectedType);
            }
            else if (token.Type == JTokenType.Object)
            {
                JObject obj = token.ToObject<JObject>();
                string typeName = obj.Value<string>("type");
                switch (typeName)
                {
                    case "Type": return ResolveType(obj.Value<string>("value"));
                    case "GameValueContext": return DeserialiseGameValueContext(obj, expectedType);
                    case "GameValueWrapper": return DeserialiseGameValueWrapper(obj, expectedType);
                    case "GamePickerList": return DeserialiseGamePickerList(obj, expectedType);
                    default: return DeserialiseGenericObject(obj, expectedType);
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                JArray arr = token.ToObject<JArray>();
                if (expectedType == typeof(Color))
                {
                    return new Color(arr.Value<float>(0), arr.Value<float>(1), arr.Value<float>(2), arr.Value<float>(3));
                }
                throw new InvalidOperationException($"Can't deserialise an array into a '{expectedType.FullName}'");
            }
            else if (token.Type == JTokenType.Null && (expectedType.IsClass || expectedType.IsInterface))
            {
                return null;
            }
            throw new InvalidOperationException($"Can't deserialise a {token.Type} into a '{expectedType.FullName}'");
        }

        private static Type ResolveType(string typeName)
            => ReflectionUtils.GetTypeFromFullName(typeName);

        private static object ResolveReference(Type type, string name)
        {
            var registrar = Registrars.TypeToRegistrar[type];
            if (registrar == null)
            {
                throw new InvalidOperationException($"Can't resolve reference to a '{type.FullName}' ('{name}') as no registrar was found for that type");
            }
            var obj = registrar.GetByName(name);
            if (obj == null)
            {
                throw new InvalidOperationException($"Failed to resolve reference '{name}' (of type '{type.FullName}')");
            }
            return obj;
        }

        private static object DeserialiseGenericObject(JObject obj, Type expectedType)
        {
            string typeName = obj.Value<string>("type");
            Type type = ResolveType(typeName);
            if (typeof(TriggerConfig).IsAssignableFrom(type))
            {
                return DeserialiseTriggerConfig(obj, type, expectedType);
            }
            string name = obj.Value<string>("name");
            bool isRef = obj.Value<bool>("reference");
            if (isRef)
            {
                if (string.IsNullOrEmpty(name))
                {
                    throw new InvalidOperationException($"Can't deserialise a reference to '{typeName}' (missing name)");
                }
                return ResolveReference(type, name);
            }
            if (!expectedType.IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"Can't deserialise a '{typeName}' into a '{expectedType.FullName}'");
            }
            object target = Activator.CreateInstance(type);
            DeserialiseGenericObject(obj, target);
            if (target is IHasID hasID)
            {
                var registrar = Registrars.Get(type);
                registrar.Insert(hasID);
            }
            return target;
        }

        public static void DeserialiseGenericObject(JObject obj, object target)
        {
            string name = obj.Value<string>("name");
            bool isRef = obj.Value<bool>("reference");
            if (isRef)
            {
                throw new InvalidOperationException($"Can't deserialise a reference into an existing object");
            }
            if (target is INamed named && !string.IsNullOrEmpty(name))
            {
                try
                {
                    var registrar = Registrars.Get(target.GetType());
                    registrar.Rename(target as IHasID, GetUniqueName(registrar, name), true);
                }
                catch
                {
                    named.Name = name;
                }
            }
            if (target is SimpleEntry simpleEntry)
            {
                string description = obj.Value<string>("description");
                if (!string.IsNullOrEmpty(description))
                {
                    simpleEntry.UserDescription = description;
                }
            }
            if (target is IProposable proposable)
            {
                proposable.InitializeDraftProposable();
            }
            if (target is DistrictMap districtMap)
            {
                var sizeJson = obj.Value<JArray>("size");
                var size = new Vector2i(sizeJson.Value<int>(0), sizeJson.Value<int>(1));
                if (size != districtMap.Map.Size)
                {
                    throw new InvalidOperationException($"Tried to import district map with a different world size (expecting {districtMap.Map.Size}, got {size})");
                }
                var districts = obj.Value<JArray>("districts");
                DeserialiseControllerList(districtMap.Districts, districts);
                var rows = obj.Value<JArray>("data");
                for (int z = 0; z < size.Y; ++z)
                {
                    var row = rows.Value<JArray>(z);
                    for (int x = 0; x < size.X; ++x)
                    {
                        var localId = row.Value<int>(x);
                        if (localId >= 0)
                        {
                            var district = districtMap.Districts[localId];
                            districtMap.Map[new Vector2i(x, z)] = district.Id;
                        }
                    }
                }
                districtMap.Changed(nameof(districtMap.Districts));
                districtMap.Changed(nameof(districtMap.Map));
                districtMap.UpdateDistricts();
            }
            if (target is District district2)
            {
                district2.SetColor((Color)DeserialiseValueAsType(obj.Value<JToken>("color"), typeof(Color)));
            }
            DeserialiseObjectProperties(target, obj.Value<JObject>("properties"));
            if (target is IProposable proposable2)
            {
                proposable2.SetProposedState(ProposableState.Draft, true, true);
            }
        }

        private static void DeserialiseControllerList(object target, JArray token)
        {
            Type innerElementType = target.GetType().GetGenericArguments()[0];
            target.GetType().GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance).Invoke(target, null);
            var addMethod = target.GetType().GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
            foreach (var element in token)
            {
                addMethod.Invoke(target, new object[] { DeserialiseValueAsType(element, innerElementType) });
            }
        }

        private static object DeserialiseGameValueContext(JObject obj, Type expectedType)
        {
            if (!expectedType.IsConstructedGenericType)
            {
                throw new InvalidOperationException($"Can't deserialise a GameValueContext into a '{expectedType.FullName}'");
            }
            Type innerType = expectedType.GetGenericArguments()[0];
            Type gameValueContextType = typeof(GameValueContext<>).MakeGenericType(innerType);
            if (!expectedType.IsAssignableFrom(gameValueContextType))
            {
                throw new InvalidOperationException($"Can't deserialise a '{gameValueContextType.FullName}' into a '{expectedType.FullName}'");
            }
            var gameValueContext = Activator.CreateInstance(gameValueContextType) as IGameValueContext;
            string contextName = obj.Value<string>("contextName");
            gameValueContextType.GetProperty("ContextName", BindingFlags.Public | BindingFlags.Instance).SetValue(gameValueContext, contextName, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
            string titleBacking = obj.Value<string>("titleBacking");
            gameValueContextType.GetProperty("TitleBacking", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(gameValueContext, titleBacking, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
            string tooltip = obj.Value<string>("tooltip");
            gameValueContextType.GetProperty("Tooltip", BindingFlags.Public | BindingFlags.Instance).SetValue(gameValueContext, tooltip, BindingFlags.Public | BindingFlags.Instance, null, null, null);
            (gameValueContext as IController).Changed("Title");
            return gameValueContext;
        }

        private static object DeserialiseGameValueWrapper(JObject obj, Type expectedType)
        {
            if (!expectedType.IsConstructedGenericType)
            {
                throw new InvalidOperationException($"Can't deserialise a GameValueWrapper into a '{expectedType.FullName}'");
            }
            Type innerType = expectedType.GetGenericArguments()[0];
            Type gameValueWrapperType = typeof(GameValueWrapper<>).MakeGenericType(innerType);
            if (!expectedType.IsAssignableFrom(gameValueWrapperType))
            {
                throw new InvalidOperationException($"Can't deserialise a '{gameValueWrapperType.FullName}' into a '{expectedType.FullName}'");
            }
            var value = obj.Value<JToken>("value");
            var gameValueWrapper = Activator.CreateInstance(gameValueWrapperType);
            gameValueWrapperType.GetProperty("Object", BindingFlags.Public | BindingFlags.Instance).SetValue(gameValueWrapper, DeserialiseValueAsType(value, innerType));
            return gameValueWrapper;
        }

        private static GamePickerList DeserialiseGamePickerList(JObject obj, Type expectedType)
        {
            if (!expectedType.IsAssignableFrom(typeof(GamePickerList)))
            {
                throw new InvalidOperationException($"Can't deserialise a GamePickerList into a '{expectedType.FullName}'");
            }
            var gamePickerList = new GamePickerList();
            JObject mustDeriveType = obj.Value<JObject>("mustDeriveType");
            if (mustDeriveType != null)
            {
                gamePickerList.MustDeriveType = DeserialiseValueAsType(mustDeriveType, typeof(Type)) as Type;
            }
            string requiredTag = obj.Value<string>("requiredTag");
            if (!string.IsNullOrEmpty(requiredTag))
            {
                gamePickerList.RequiredTag = requiredTag;
            }
            var arr = obj.Value<JArray>("entries");
            foreach (JToken entry in arr)
            {
                gamePickerList.Entries.Add(DeserialiseValueAsType(entry, typeof(Type)));
            }
            string internalDescription = obj.Value<string>("internalDescription");
            if (!string.IsNullOrEmpty(internalDescription))
            {
                gamePickerList.InternalDescription = internalDescription;
                gamePickerList.Changed(nameof(gamePickerList.MarkedUpName));
            }
            return gamePickerList;
        }

        private static TriggerConfig DeserialiseTriggerConfig(JObject obj, Type triggerConfigType, Type expectedType)
        {
            if (!expectedType.IsAssignableFrom(typeof(TriggerConfig)))
            {
                throw new InvalidOperationException($"Can't deserialise a TriggerConfig into a '{expectedType.FullName}'");
            }
            if (!typeof(TriggerConfig).IsAssignableFrom(triggerConfigType))
            {
                throw new InvalidOperationException($"Can't deserialise a '{triggerConfigType.FullName}' into a TriggerConfig");
            }
            var triggerConfig = Activator.CreateInstance(triggerConfigType) as TriggerConfig;
            typeof(TriggerConfig)
                .GetProperty("PropNameBacker", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(triggerConfig, obj.Value<string>("propNameBacker"), BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
            string typeToConfig = obj.Value<string>("typeToConfig");
            typeof(TriggerConfig)
                .GetProperty("TypeToConfig", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(triggerConfig, string.IsNullOrEmpty(typeToConfig) ? null : ResolveType(typeToConfig), BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
            string propDisplayName = obj.Value<string>("propDisplayName");
            typeof(TriggerConfig)
                .GetProperty("PropDisplayName", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(triggerConfig, propDisplayName, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null);
            DeserialiseObjectProperties(triggerConfig, obj.Value<JObject>("properties"));
            return triggerConfig;
        }


        #endregion
    }
}
