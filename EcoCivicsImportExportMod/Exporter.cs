using System;
using System.IO;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;

using Newtonsoft.Json;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;

    public static class Exporter
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public static async Task Export(IHasUniversalID civicObject, string destination)
        {
            string text = JsonConvert.SerializeObject(civicObject, Formatting.Indented, new CivicsJsonConverter());
            if (Uri.TryCreate(destination, UriKind.Absolute, out Uri uri))
            {
                await httpClient.PostAsync(uri, new StringContent(text, Encoding.UTF8, "application/json"));
            }
            else
            {
                await File.WriteAllTextAsync(destination, text);
            }
            
        }
    }
}
