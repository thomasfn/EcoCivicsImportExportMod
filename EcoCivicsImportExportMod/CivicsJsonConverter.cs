using System;
using System.Collections;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;

    using Shared.Networking;
    using Shared.Localization;

    using Gameplay.Civics.Misc;
    using Gameplay.Civics.GameValues;
    using Gameplay.GameActions;
    using Gameplay.Utils;

    public class CivicsJsonConverter : JsonConverter
    {
        /// <summary>
        /// Serialised civics authored with a different major version are completely incompatible.
        /// </summary>
        public const int MajorVersion = 1;

        /// <summary>
        /// Serialised civics authored with a different minor version are compatible.
        /// </summary>
        public const int MinorVersion = 0;

        #region Serialisation

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var rootObj = SerialiseGenericObject(value, value as IHasSubRegistrarEntries);
            rootObj.AddFirst(new JProperty("version", new int[] { MajorVersion, MinorVersion }));
            rootObj.WriteTo(writer);
        }

        private JObject SerialiseGenericObject(object value, IHasSubRegistrarEntries inlineObjectContext = null)
        {
            var obj = new JObject();
            obj.Add(new JProperty("type", value.GetType().FullName));
            if (value is INamed named)
            {
                obj.Add(new JProperty("name", SerialiseValue(named.Name)));
            }
            obj.Add(new JProperty("reference", false));
            if (value is SimpleEntry simpleEntry)
            {
                obj.Add(new JProperty("description", SerialiseValue(simpleEntry.UserDescription)));
            }

            obj.Add(new JProperty("properties", SerialiseObjectProperties(value, value as IHasSubRegistrarEntries)));
            return obj;
        }

        private JObject SerialiseObjectProperties(object value, IHasSubRegistrarEntries inlineObjectContext = null)
        {
            var obj = new JObject();

            var properties = value.GetType()
                .GetProperties()
                .Where((propInfo) => propInfo.GetCustomAttribute<EcoAttribute>() != null);

            foreach (var propInfo in properties)
            {
                var token = SerialiseValue(propInfo.GetValue(value), inlineObjectContext);
                obj.Add(new JProperty(propInfo.Name, token));
            }

            return obj;
        }

        private object SerialiseValue(object value, IHasSubRegistrarEntries inlineObjectContext = null)
        {
            if (value == null)
            {
                return null;
            }
            else if (value is int intValue)
            {
                return intValue;
            }
            else if (value is bool boolValue)
            {
                return boolValue;
            }
            else if (value is float floatValue)
            {
                return floatValue;
            }
            else if (value is double doubleValue)
            {
                return doubleValue;
            }
            else if (value is string stringValue)
            {
                return stringValue;
            }
            else if (value is LocString locStringValue)
            {
                return locStringValue.ToString();
            }
            else if (value is Type typeValue)
            {
                var jsonObj = new JObject();
                jsonObj.Add(new JProperty("type", "Type"));
                jsonObj.Add(new JProperty("value", typeValue.FullName));
                return jsonObj;
            }
            else if (value is GamePickerList gamePickerListValue)
            {
                return SerialiseGamePickerList(gamePickerListValue, inlineObjectContext);
            }
            else if (value is INamed namedValue && (inlineObjectContext == null || !inlineObjectContext.SubRegistrarEntries.Contains(namedValue)))
            {
                return SerialiseObjectReference(namedValue);
            }
            else if (value is IEnumerable enumerableValue)
            {
                return SerialiseList(enumerableValue, inlineObjectContext);
            }
            else if (value is IGameValueContext gameValueContext)
            {
                return SerialiseGameValueContext(gameValueContext);
            }
            else if (value is TriggerConfig triggerConfig)
            {
                return SerialiseTriggerConfig(triggerConfig);
            }
            else if (value is GameValue gameValue)
            {
                return SerialiseGameValue(gameValue);
            }
            else if (value.GetType().IsEnum)
            {
                return value.GetType().GetEnumName(value);
            }
            else if (value.GetType().IsClass)
            {
                return SerialiseGenericObject(value, inlineObjectContext);
            }
            else
            {
                var jsonObj = new JObject();
                jsonObj.Add(new JProperty("type", "unknown"));
                jsonObj.Add(new JProperty("actualType", value.GetType().Name));
                jsonObj.Add(new JProperty("info", value.ToString()));
                return jsonObj;
            }
        }

        private JObject SerialiseObjectReference(INamed value)
        {
            var jsonObj = new JObject();
            jsonObj.Add(new JProperty("type", value.GetType().FullName));
            jsonObj.Add(new JProperty("name", value.Name));
            jsonObj.Add(new JProperty("reference", true));
            return jsonObj;
        }

        private JArray SerialiseList(IEnumerable enumerableValue, IHasSubRegistrarEntries inlineObjectContext)
        {
            var jsonArr = new JArray();
            foreach (object value in enumerableValue)
            {
                jsonArr.Add(SerialiseValue(value, inlineObjectContext));
            }
            return jsonArr;
        }

        private JObject SerialiseTriggerConfig(TriggerConfig triggerConfig)
        {
            var jsonObj = new JObject();
            jsonObj.Add(new JProperty("type", triggerConfig.GetType().FullName));
            var typeToConfig = triggerConfig.GetType().GetProperty("TypeToConfig", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(triggerConfig) as Type;
            jsonObj.Add(new JProperty("typeToConfig", SerialiseValue(typeToConfig?.FullName)));
            jsonObj.Add(new JProperty("propNameBacker", SerialiseValue(triggerConfig.PropNameBacker)));
            var propDisplayName = typeof(TriggerConfig).GetProperty("PropDisplayName", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(triggerConfig) as string;
            jsonObj.Add(new JProperty("propDisplayName", SerialiseValue(propDisplayName)));
            jsonObj.Add(new JProperty("properties", SerialiseObjectProperties(triggerConfig)));
            return jsonObj;
        }

        private JObject SerialiseGamePickerList(GamePickerList gamePickerListValue, IHasSubRegistrarEntries inlineObjectContext)
        {
            var jsonObj = new JObject();
            jsonObj.Add(new JProperty("type", "GamePickerList"));
            jsonObj.Add(new JProperty("mustDeriveType", SerialiseValue(gamePickerListValue.MustDeriveType, inlineObjectContext)));
            jsonObj.Add(new JProperty("requiredTag", SerialiseValue(gamePickerListValue.RequiredTag, inlineObjectContext)));
            jsonObj.Add(new JProperty("entries", SerialiseList(gamePickerListValue.Entries, inlineObjectContext)));
            return jsonObj;
        }

        private JObject SerialiseGameValue(GameValue gameValue)
        {
            var jsonObj = new JObject();
            if (gameValue.GetType().IsConstructedGenericType && gameValue.GetType().GetGenericTypeDefinition() == typeof(GameValueWrapper<>))
            {
                jsonObj.Add(new JProperty("type", "GameValueWrapper"));
                object wrappedValue = gameValue.GetType().GetProperty("Object", BindingFlags.Public | BindingFlags.Instance).GetValue(gameValue);
                jsonObj.Add(new JProperty("value", SerialiseValue(wrappedValue)));
            }
            else
            {
                jsonObj.Add(new JProperty("type", gameValue.GetType().FullName));
                jsonObj.Add(new JProperty("properties", SerialiseObjectProperties(gameValue)));
            }
            return jsonObj;
        }

        private JObject SerialiseGameValueContext(IGameValueContext gameValueContext)
        {
            var jsonObj = new JObject();
            jsonObj.Add(new JProperty("type", "GameValueContext"));
            jsonObj.Add(new JProperty("contextName", SerialiseValue(gameValueContext.ContextName)));
            string titleBacking = gameValueContext.GetType().GetProperty("TitleBacking", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(gameValueContext, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null) as string;
            jsonObj.Add(new JProperty("titleBacking", SerialiseValue(titleBacking)));
            string tooltip = gameValueContext.GetType().GetProperty("Tooltip", BindingFlags.Public | BindingFlags.Instance).GetValue(gameValueContext, BindingFlags.Public | BindingFlags.Instance, null, null, null) as string;
            jsonObj.Add(new JProperty("tooltip", SerialiseValue(tooltip)));
            return jsonObj;
        }

        #endregion

        #region Deserialisation

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        #endregion

        public override bool CanRead => false;

        public override bool CanConvert(Type objectType) => true;
    }
}
