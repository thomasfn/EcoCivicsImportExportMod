using System.IO;

using Newtonsoft.Json;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;

    public static class Exporter
    {
        public static void Export<T>(T civicObject, string filename) where T : IHasID
        {
            File.WriteAllText(filename, JsonConvert.SerializeObject(civicObject, new CivicsJsonConverter()));
        }
    }
}
