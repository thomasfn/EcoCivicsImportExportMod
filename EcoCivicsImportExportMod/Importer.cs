using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Utils;
    using Core.Systems;

    using Shared.Items;
    using Shared.Localization;
    
    using Gameplay.Civics.GameValues;
    using Gameplay.GameActions;
    using Gameplay.Civics.Misc;

    public static class Importer
    {
        private static readonly Regex matchNumberAtEnd = new Regex(@"[0-9]+$", RegexOptions.Compiled);
        private static readonly Regex matchAssemblyFromType = new Regex(@"^[a-zA-Z]+\.[a-zA-Z]+", RegexOptions.Compiled);

        public static T Import<T>(string filename) where T : IHasID
        {
            string json = File.ReadAllText(filename);
            JObject jsonObj = JObject.Parse(json);
            var obj = Registrars.Add<T>(null, null);
            if (obj is SimpleProposable simpleProposable)
            {
                simpleProposable.InitializeDraftProposable();
                simpleProposable.SetProposedState(ProposableState.Draft, true, true);
            }
            try
            {
                Deserialise(obj, jsonObj);
            }
            catch (Exception ex)
            {
                Registrars.Remove(obj);
                Logger.Error(ex.ToString());
                throw ex;
            }
            return obj;
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
            if (target is SimpleProposable simpleProposable)
            {
                var registrar = Registrars.TypeToRegistrar[target.GetType()];
                string name = rootObj.Value<string>("name");
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
                simpleProposable.Name = name;
                simpleProposable.UserDescription = rootObj.Value<string>("description");
            }
            DeserialiseObject(target, rootObj.Value<JObject>("properties"));
        }

        private static void DeserialiseObject(object target, JObject obj)
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

        private static object DeserialiseObject(Type type, JObject obj)
        {
            object target = Activator.CreateInstance(type);
            DeserialiseObject(target, obj);
            return target;
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
                    default:
                        {
                            Type type = ResolveType(typeName);
                            string name = obj.Value<string>("name");
                            if (name != null)
                            {
                                return ResolveReference(type, name);
                            }
                            if (!expectedType.IsAssignableFrom(type))
                            {
                                throw new InvalidOperationException($"Can't deserialise a '{typeName}' into a '{expectedType.FullName}'");
                            }
                            return DeserialiseObject(type, obj.Value<JObject>("properties"));
                        }
                }
            }
            else if (token.Type == JTokenType.Null && expectedType.IsClass)
            {
                return null;
            }
            throw new InvalidOperationException($"Can't deserialise a {token.Type} into a '{expectedType.FullName}'");
        }

        private static Type ResolveType(string typeName)
        {
            var assemblyNameMatch = matchAssemblyFromType.Match(typeName);
            if (!assemblyNameMatch.Success)
            {
                throw new InvalidOperationException($"Unable to determine assembly name for '{typeName}'");
            }
            return Type.GetType($"{typeName}, {assemblyNameMatch.Value}", true);
        }

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
            string contextName = obj.Value<string>("contextName");
            var gameValueContext = Activator.CreateInstance(gameValueContextType);
            Logger.Debug($"Created a GameValueContext for context name '{contextName}' but this part isn't implemented yet!");
            //gameValueContextType.GetMethod("SetContextChoice")
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
            var arr = obj.Value<JArray>("entries");
            foreach (JToken entry in arr)
            {
                // TODO: Can a GamePickerList ever hold something that isn't Type? If not, why is the ControllerHashSet of object, and not Type? 
                gamePickerList.Entries.Add(DeserialiseValueAsType(entry, typeof(Type)));
            }
            return gamePickerList;
        }

        #endregion
    }
}
