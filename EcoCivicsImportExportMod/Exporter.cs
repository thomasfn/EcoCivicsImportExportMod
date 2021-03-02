using System;
using System.IO;
using System.Net;

using Newtonsoft.Json;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;

    public static class Exporter
    {
        private static readonly WebClient webClient = new WebClient();

        public static void Export(IHasID civicObject, string filename)
        {
            string text = JsonConvert.SerializeObject(civicObject, Formatting.Indented, new CivicsJsonConverter());
            if (Uri.TryCreate(filename, UriKind.Absolute, out Uri uri))
            {
                webClient.UploadString(uri, text);
            }
            else
            {
                File.WriteAllText(filename, text);
            }
            
        }
    }
}
