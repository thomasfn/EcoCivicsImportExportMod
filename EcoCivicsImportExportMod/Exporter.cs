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

        public static void Export(IHasID civicObject, string destination)
        {
            string text = JsonConvert.SerializeObject(civicObject, Formatting.Indented, new CivicsJsonConverter());
            if (Uri.TryCreate(destination, UriKind.Absolute, out Uri uri))
            {
                webClient.UploadString(uri, text);
            }
            else
            {
                File.WriteAllText(destination, text);
            }
            
        }
    }
}
