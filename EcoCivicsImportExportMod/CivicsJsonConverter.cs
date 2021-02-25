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

    using Gameplay.Civics;
    using Gameplay.Civics.Misc;
    using Gameplay.Civics.GameValues;
    using Gameplay.Civics.Laws;
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
            var rootObj = new JObject();

            rootObj.Add(new JProperty("version", new int[] { MajorVersion, MinorVersion }));
            rootObj.Add(new JProperty("type", value.GetType().FullName));

            if (value is SimpleEntry simpleEntry)
            {
                rootObj.Add(new JProperty("name", SerialiseCivicValue(simpleEntry.Name)));
                rootObj.Add(new JProperty("description", SerialiseCivicValue(simpleEntry.UserDescription)));
            }

            rootObj.Add(new JProperty("properties", SerialiseCivicObject(value)));

            rootObj.WriteTo(writer);
        }

        private JObject SerialiseCivicObject(object civicObject)
        {
            var civicObj = new JObject();

            var properties = civicObject.GetType()
                .GetProperties()
                .Where((propInfo) => propInfo.GetCustomAttribute<EcoAttribute>() != null);

            foreach (var propInfo in properties)
            {
                var token = SerialiseCivicValue(propInfo.GetValue(civicObject));
                civicObj.Add(new JProperty(propInfo.Name, token));
            }

            return civicObj;
        }

        private object SerialiseCivicValue(object value)
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
                return SerialiseGamePickerList(gamePickerListValue);
            }
            else if (value is INamed namedValue)
            {
                return SerialiseObjectReference(namedValue);
            }
            else if (value is IEnumerable enumerableValue)
            {
                return SerialiseList(enumerableValue);
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
                var jsonObj = new JObject();
                jsonObj.Add(new JProperty("type", value.GetType().FullName));
                jsonObj.Add(new JProperty("properties", SerialiseCivicObject(value)));
                return jsonObj;
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
            return jsonObj;
        }

        private JArray SerialiseList(IEnumerable enumerableValue)
        {
            var jsonArr = new JArray();
            foreach (object value in enumerableValue)
            {
                jsonArr.Add(SerialiseCivicValue(value));
            }
            return jsonArr;
        }

        private JObject SerialiseTriggerConfig(TriggerConfig triggerConfig)
        {
            var jsonObj = new JObject();
            jsonObj.Add(new JProperty("type", triggerConfig.GetType().FullName));
            var typeToConfig = triggerConfig.GetType().GetProperty("TypeToConfig", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(triggerConfig) as Type;
            jsonObj.Add(new JProperty("typeToConfig", SerialiseCivicValue(typeToConfig?.FullName)));
            jsonObj.Add(new JProperty("propNameBacker", SerialiseCivicValue(triggerConfig.PropNameBacker)));
            var propDisplayName = typeof(TriggerConfig).GetProperty("PropDisplayName", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(triggerConfig) as string;
            jsonObj.Add(new JProperty("propDisplayName", SerialiseCivicValue(propDisplayName)));
            jsonObj.Add(new JProperty("properties", SerialiseCivicObject(triggerConfig)));
            return jsonObj;
        }

        private JObject SerialiseGamePickerList(GamePickerList gamePickerListValue)
        {
            var jsonObj = new JObject();
            jsonObj.Add(new JProperty("type", "GamePickerList"));
            jsonObj.Add(new JProperty("mustDeriveType", SerialiseCivicValue(gamePickerListValue.MustDeriveType)));
            jsonObj.Add(new JProperty("requiredTag", SerialiseCivicValue(gamePickerListValue.RequiredTag)));
            jsonObj.Add(new JProperty("entries", SerialiseList(gamePickerListValue.Entries)));
            return jsonObj;
        }

        private JObject SerialiseGameValue(GameValue gameValue)
        {
            var jsonObj = new JObject();
            if (gameValue.GetType().IsConstructedGenericType && gameValue.GetType().GetGenericTypeDefinition() == typeof(GameValueWrapper<>))
            {
                jsonObj.Add(new JProperty("type", "GameValueWrapper"));
                object wrappedValue = gameValue.GetType().GetProperty("Object", BindingFlags.Public | BindingFlags.Instance).GetValue(gameValue);
                jsonObj.Add(new JProperty("value", SerialiseCivicValue(wrappedValue)));
            }
            else
            {
                jsonObj.Add(new JProperty("type", gameValue.GetType().FullName));
                jsonObj.Add(new JProperty("properties", SerialiseCivicObject(gameValue)));
            }
            return jsonObj;
        }

        private JObject SerialiseGameValueContext(IGameValueContext gameValueContext)
        {
            var jsonObj = new JObject();
            jsonObj.Add(new JProperty("type", "GameValueContext"));
            jsonObj.Add(new JProperty("contextName", SerialiseCivicValue(gameValueContext.ContextName)));
            string titleBacking = gameValueContext.GetType().GetProperty("TitleBacking", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(gameValueContext, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null) as string;
            jsonObj.Add(new JProperty("titleBacking", SerialiseCivicValue(titleBacking)));
            string tooltip = gameValueContext.GetType().GetProperty("Tooltip", BindingFlags.Public | BindingFlags.Instance).GetValue(gameValueContext, BindingFlags.Public | BindingFlags.Instance, null, null, null) as string;
            jsonObj.Add(new JProperty("tooltip", SerialiseCivicValue(tooltip)));
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
