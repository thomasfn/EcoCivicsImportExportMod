using System;
using System.Reflection;
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
    using Gameplay.Economy.Money;
    using Gameplay.Economy;
    using Gameplay.Settlements;
    using Gameplay.Aliases;
    using Gameplay.Players;

    public class ImportContext
    {
        public IList<IHasID> ImportedObjects { get; } = new List<IHasID>();

        public IDictionary<CivicReference, IHasID> ReferenceMap { get; } = new Dictionary<CivicReference, IHasID>();

        public IHasID ImportStub(BundledCivic bundledCivic)
        {
            var obj = bundledCivic.CreateStub();
            ImportedObjects.Add(obj);
            ReferenceMap.Add(bundledCivic.AsReference, obj);
            return obj;
        }

        public void Import(BundledCivic bundledCivic, Settlement settlement, User importer)
        {
            if (!ReferenceMap.TryGetValue(bundledCivic.AsReference, out IHasID obj))
            {
                obj = ImportStub(bundledCivic);
            }
            if (obj is IProposable proposable)
            {
                if (obj is not Settlement) { proposable.Settlement = settlement; }
                proposable.Creator = importer;
                if (proposable.State == ProposableState.Uninitialized) { proposable.InitializeDraftProposable(); }
                DeserialiseGenericObject(bundledCivic.Data, obj);
                proposable.SetProposedState(proposable.State == ProposableState.Uninitialized ? ProposableState.Draft : proposable.State, true, true);
            }
            else if (obj is BankAccount bankAccount)
            {
                bankAccount.Creator = importer;
                bankAccount.Settlement = settlement;
                DeserialiseGenericObject(bundledCivic.Data, obj);
            }
            else
            {
                DeserialiseGenericObject(bundledCivic.Data, obj);
            }
        }

        public void Clear()
        {
            Importer.Cleanup(ImportedObjects);
            ImportedObjects.Clear();
            ReferenceMap.Clear();
        }

        #region Deserialisation

        private void DeserialiseObjectProperties(object target, JObject obj)
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

        private void DeserialiseValue(object target, PropertyInfo propertyInfo, JToken value)
        {
            if (propertyInfo.PropertyType.IsConstructedGenericType && typeof(ControllerList<>).IsAssignableFrom(propertyInfo.PropertyType.GetGenericTypeDefinition()))
            {
                if (value.Type != JTokenType.Array)
                {
                    Logger.Error($"Can't deserialise {value.Type} into '{target.GetType().FullName}.{propertyInfo.Name}' (expecting Array)");
                    return;
                }
                DeserialiseControllerListOrHashSet(propertyInfo.GetValue(target), value.ToObject<JArray>());
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

        private object DeserialiseValueAsType(JToken token, Type expectedType)
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
                return typeName switch
                {
                    "Type" => ResolveType(obj.Value<string>("value")),
                    "GameValueContext" => DeserialiseGameValueContext(obj, expectedType),
                    "GameValueWrapper" => DeserialiseGameValueWrapper(obj, expectedType),
                    "GamePickerList" => DeserialiseGamePickerList(obj, expectedType),
                    _ => DeserialiseGenericObject(obj, expectedType),
                };
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

        private Type ResolveType(string typeName)
            => ReflectionUtils.GetTypeFromFullName(typeName);

        public object ResolveReference(CivicReference civicReference)
        {
            if (ReferenceMap.TryGetValue(civicReference, out IHasID internalObj)) { return internalObj; }
            var registrar = Registrars.GetByDerivedType(civicReference.Type);
            if (registrar == null)
            {
                throw new InvalidOperationException($"Can't resolve reference to a '{civicReference.Type.FullName}' ('{civicReference.Name}') as no registrar was found for that type");
            }
            var obj = registrar.GetByName(civicReference.Name);
            if (obj == null)
            {
                // Eco bug: treasury bank account is undiscoverable until the server is restarted
                if (civicReference.Type == typeof(TreasuryBankAccount) && civicReference.Name == "Treasury Bank Account")
                {
                    return BankAccountManager.Obj.Treasury();
                }
                throw new InvalidOperationException($"Failed to resolve reference '{civicReference.Name}' (of type '{civicReference.Type.FullName}')");
            }
            return obj;
        }

        public bool TryResolveReference(CivicReference civicReference, out object resolvedObject)
        {
            try
            {
                resolvedObject = ResolveReference(civicReference);
                return true;
            }
            catch
            {
                resolvedObject = null;
                return false;
            }
        }

        private object DeserialiseGenericObject(JObject obj, Type expectedType)
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
                return ResolveReference(new CivicReference(type, name));
            }
            if (!expectedType.IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"Can't deserialise a '{typeName}' into a '{expectedType.FullName}'");
            }
            object target;
            if (!string.IsNullOrEmpty(name) && ReferenceMap.TryGetValue(new CivicReference(type, name), out IHasID existingObj))
            {
                target = existingObj;
            }
            else
            {
                target = Activator.CreateInstance(type);
                if (target is IHasID hasID)
                {
                    var registrar = Registrars.GetByDerivedType(type);
                    registrar.Insert(hasID);
                }
            }
            DeserialiseGenericObject(obj, target);
            return target;
        }

        private void DeserialiseGenericObject(JObject obj, object target)
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
                    var registrar = Registrars.GetByDerivedType(target.GetType());
                    registrar.Rename(target as IHasID, name, true);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Got unusual error when trying to rename IHasID via registrar: {ex.Message}");
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
            if (target is IHasDualPermissions hasDualPermissions)
            {
                var managers = obj.Value<JArray>("managers");
                DeserialiseControllerListOrHashSet(hasDualPermissions.DualPermissions.ManagerSet, managers);
                var users = obj.Value<JArray>("users");
                DeserialiseControllerListOrHashSet(hasDualPermissions.DualPermissions.UserSet, users);
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
                var districtList = new List<District>();
                foreach (var districtObj in districts)
                {
                    var district = DeserialiseValueAsType(districtObj, typeof(District)) as District;
                    districtList.Add(district);
                    if (district == null) { continue; }
                    districtMap.Districts.Add(district.Id, district);
                }
                var rows = obj.Value<JArray>("data");
                for (int z = 0; z < size.Y; ++z)
                {
                    var row = rows.Value<JArray>(z);
                    for (int x = 0; x < size.X; ++x)
                    {
                        var localId = row.Value<int>(x);
                        if (localId >= 0)
                        {
                            var district = districtList[localId];
                            if (district != null)
                            {
                                districtMap.Map[new Vector2i(x, z)] = district.Id;
                            }
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
            if (target is BankAccount bankAccount)
            {
                var holdings = obj.Value<JArray>("holdings");
                foreach (var value in holdings)
                {
                    if (value is JObject holding)
                    {
                        var currency = DeserialiseGenericObject(holding.Value<JObject>("currency"), typeof(Currency)) as Currency;
                        if (currency == null) { continue; }
                        bankAccount.AddCurrency(currency, holding.Value<float>("amount"));
                    }
                }
            }
            DeserialiseObjectProperties(target, obj.Value<JObject>("properties"));
            if (target is IProposable proposable2)
            {
                proposable2.SetProposedState(ProposableState.Draft, true, true);
            }
        }

        private void DeserialiseControllerListOrHashSet(object target, JArray token)
        {
            Type innerElementType = target.GetType().GetGenericArguments()[0];
            target.GetType().GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance).Invoke(target, null);
            var addMethod = target.GetType().GetMethod("Add", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { innerElementType }, null);
            foreach (var element in token)
            {
                addMethod.Invoke(target, new object[] { DeserialiseValueAsType(element, innerElementType) });
            }
        }

        private object DeserialiseGameValueContext(JObject obj, Type expectedType)
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
            gameValueContextType.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(gameValueContext, obj.Value<string>("_name"), BindingFlags.Public | BindingFlags.Instance, null, null, null);
            gameValueContextType.GetProperty("MarkedUpNameString", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(gameValueContext, obj.Value<string>("markedUpName"), BindingFlags.Public | BindingFlags.Instance, null, null, null);
            gameValueContextType.GetProperty("ContextDescription", BindingFlags.Public | BindingFlags.Instance)
                .SetValue(gameValueContext, obj.Value<string>("contextDescription"), BindingFlags.Public | BindingFlags.Instance, null, null, null);
            (gameValueContext as IController).Changed("Title");
            return gameValueContext;
        }

        private object DeserialiseGameValueWrapper(JObject obj, Type expectedType)
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

        private GamePickerList DeserialiseGamePickerList(JObject obj, Type expectedType)
        {
            GamePickerList gamePickerList;
            if (expectedType.IsConstructedGenericType)
            {
                Type innerType = expectedType.GetGenericArguments()[0];
                Type gamePickerListType = typeof(GamePickerList<>).MakeGenericType(innerType);
                if (!expectedType.IsAssignableFrom(gamePickerListType))
                {
                    throw new InvalidOperationException($"Can't deserialise a '{gamePickerListType.FullName}' into a '{expectedType.FullName}'");
                }
                gamePickerList = Activator.CreateInstance(gamePickerListType, new object[] { null }) as GamePickerList;
            }
            else if (expectedType.IsAssignableFrom(typeof(GamePickerList)))
            {
                gamePickerList = new GamePickerList();
                JObject mustDeriveTypeToken = obj.Value<JObject>("mustDeriveType");
                if (mustDeriveTypeToken != null)
                {
                    gamePickerList.MustDeriveType = DeserialiseValueAsType(mustDeriveTypeToken, typeof(Type)) as Type;
                }
            }
            else if (expectedType.IsAssignableFrom(typeof(GamePickerListAlias)))
            {
                gamePickerList = new GamePickerListAlias();
                JObject mustDeriveTypeToken = obj.Value<JObject>("mustDeriveType");
                if (mustDeriveTypeToken != null)
                {
                    var mustDeriveType = DeserialiseValueAsType(mustDeriveTypeToken, typeof(Type)) as Type;
                    if (mustDeriveType != typeof(IAlias))
                    {
                        throw new InvalidOperationException($"Can't deserialise a GamePickerList with mustDeriveType of '{mustDeriveType.FullName}' into a '{expectedType.FullName}'");
                    }
                }
            }
            else
            {
                throw new InvalidOperationException($"Can't deserialise a GamePickerList into a '{expectedType.FullName}'");
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

        private TriggerConfig DeserialiseTriggerConfig(JObject obj, Type triggerConfigType, Type expectedType)
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
