using System;
using System.IO;
using System.Collections.Generic;

using Newtonsoft.Json;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;

    using Shared.Items;

    using Gameplay.Civics.Misc;

    public static class Importer
    {
        public static T Import<T>(string filename) where T : IHasID
        {
            string json = File.ReadAllText(filename);
            var obj = Registrars.Add<T>(null, null);
            if (obj is SimpleProposable simpleProposable)
            {
                simpleProposable.InitializeDraftProposable();
                simpleProposable.SetProposedState(ProposableState.Draft, true, true);
            }
            try
            {
                JsonConvert.PopulateObject(json, obj, new JsonSerializerSettings
                {
                    Converters = new List<JsonConverter> { new CivicsJsonConverter() },
                });
            }
            catch (Exception ex)
            {
                Registrars.Remove(obj);
                throw ex;
            }
            return obj;
        }
    }
}
