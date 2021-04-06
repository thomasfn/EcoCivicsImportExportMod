using System;
using System.IO;
using System.Net;
using System.Collections.Generic;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;

    using Gameplay.Civics.Misc;

    public static class Importer
    {
        private static readonly WebClient webClient = new WebClient();

        public static CivicBundle ImportBundle(string source)
        {
            string text;
            if (Uri.TryCreate(source, UriKind.Absolute, out Uri uri))
            {
                text = webClient.DownloadString(uri);
            }
            else
            {
                text = File.ReadAllText(Path.Combine(CivicsImpExpPlugin.ImportExportDirectory, source));
            }
            return CivicBundle.LoadFromText(text);
        }

        public static void Cleanup(IHasID obj)
        {
            Registrars.Remove(obj);
            if (obj is IHasSubRegistrarEntries hasSubRegistrarEntries)
            {
                foreach (var subObj in hasSubRegistrarEntries.SubRegistrarEntries)
                {
                    Cleanup(subObj);
                }
            }
        }

        public static void Cleanup(IEnumerable<IHasID> objs)
        {
            foreach (var obj in objs)
            {
                Cleanup(obj);
            }
        }
    }
}
