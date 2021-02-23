using System.IO;

using Newtonsoft.Json;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;

    public static class Exporter
    {
        public static void Export(IHasID civicObject, string filename)
        {
            string json = JsonConvert.SerializeObject(civicObject, Formatting.Indented, new CivicsJsonConverter());
            File.WriteAllText(filename, json);
        }
    }
}
