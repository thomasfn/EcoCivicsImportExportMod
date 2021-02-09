using System.IO;

using Newtonsoft.Json;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;

    public static class Exporter
    {
        public static void Export<T>(T civicObject, string filename) where T : IHasID
        {
            string json = JsonConvert.SerializeObject(civicObject, Formatting.Indented, new CivicsJsonConverter());
            File.WriteAllText(filename, json);
        }
    }
}
