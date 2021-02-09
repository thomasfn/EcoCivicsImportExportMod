using System;
using System.Collections;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;
    using Core.Utils;

    using Shared.Networking;
    using Shared.Localization;

    using Gameplay.Civics.Misc;
    using Gameplay.Civics.GameValues;
    using Gameplay.Aliases;
    

    public class CivicsJsonConverter : JsonConverter
    {
        /// <summary>
        /// Serialised civics authored with a different major version are completely incompatible.
        /// </summary>
        public const int MajorVersion = 1;

        /// <summary>
        /// Serialised civics authored with a higher minor version are compatible with readers that support a lower minor version.
        /// </summary>
        public const int MinorVersion = 0;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var rootObj = new JObject();

            rootObj.Add(new JProperty("version", new int[] { MajorVersion, MinorVersion }));
            rootObj.Add(new JProperty("type", value.GetType().Name));

            if (value is SimpleProposable simpleProposable)
            {
                rootObj.Add(new JProperty("name", SerialiseCivicValue(simpleProposable.Name)));
                rootObj.Add(new JProperty("description", SerialiseCivicValue(simpleProposable.Description())));
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
            else if (value is INamed namedValue)
            {
                return SerialiseObjectReference(namedValue);
            }
            else if (value is IEnumerable enumerableValue)
            {
                return SerialiseList(enumerableValue);
            }
            else if (value is GameValue gameValue)
            {
                return SerialiseGameValue(gameValue);
            }
            var jsonObj = new JObject();
            jsonObj.Add(new JProperty("type", "unknown"));
            jsonObj.Add(new JProperty("actualType", value.GetType().Name));
            jsonObj.Add(new JProperty("info", value.ToString()));
            return jsonObj;
        }

        private object SerialiseObjectReference(INamed value)
        {
            var jsonObj = new JObject();
            jsonObj.Add(new JProperty("type", value.GetType().Name));
            jsonObj.Add(new JProperty("name", value.Name));
            return jsonObj;
        }

        private object SerialiseList(IEnumerable enumerableValue)
        {
            var jsonArr = new JArray();
            foreach (object value in enumerableValue)
            {
                jsonArr.Add(SerialiseCivicValue(value));
            }
            return jsonArr;
        }

        private object SerialiseGameValue(GameValue gameValue)
        {
            var jsonObj = SerialiseCivicObject(gameValue);
            jsonObj.AddFirst(new JProperty("type", gameValue.GetType().Name));
            return jsonObj;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            throw new NotImplementedException("Unnecessary because CanRead is false. The type will skip the converter.");
        }

        public override bool CanRead
        {
            get { return false; }
        }

        public override bool CanConvert(Type objectType) => true;
    }
}
