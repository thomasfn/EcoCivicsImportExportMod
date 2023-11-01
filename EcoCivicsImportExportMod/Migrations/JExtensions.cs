using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp.Migrations
{
    public static class JExtensions
    {
        public static IEnumerable<JObject> GetNestedObjects(this JObject obj)
        {
            foreach (var pair in obj)
            {
                if (pair.Value is JObject innerObj)
                {
                    yield return innerObj;
                    foreach (var x in GetNestedObjects(innerObj)) { yield return x; }
                }
                else if (pair.Value is JArray innerArr)
                {
                    foreach (var element in innerArr)
                    {
                        if (element is JObject elementObj)
                        {
                            foreach (var x in GetNestedObjects(elementObj)) { yield return x; }
                        }
                    }
                }
            }
        }
    }
}
