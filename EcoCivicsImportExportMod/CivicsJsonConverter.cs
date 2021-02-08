using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp
{
    using Shared.Networking;

    using Gameplay.Civics.Misc;
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

            rootObj.Add(new JProperty("majVer", 1));
            rootObj.Add(new JProperty("minVer", 0));
            rootObj.Add(new JProperty("civicType", value.GetType().Name));

            if (value is SimpleProposable simpleProposable)
            {
                rootObj.Add(new JProperty("civicName", SerialiseCivicValue(simpleProposable.Name)));
                rootObj.Add(new JProperty("description", SerialiseCivicValue(simpleProposable.Description())));
            }

            rootObj.Add(new JProperty("civicData", SerialiseCivicObject(value)));

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
            if (value is int intValue)
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
            else if (value is IAlias aliasValue)
            {
                return SerialiseAlias(aliasValue);
            }
            return null;
        }

        private object SerialiseAlias(IAlias alias)
        {
            var aliasObj = new JObject();
            aliasObj.Add(new JProperty("type", alias.GetType().Name));
            return aliasObj;
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
