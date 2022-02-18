using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp.Migrations._0950
{
    using Shared.Utils;

    public class CivicArticle : ICivicsImpExpMigratorV1
    {
        public bool ShouldMigrate(JObject obj)
        {
            if (obj.Value<string>("type") != "Eco.Gameplay.Civics.Constitutional.ConstitutionalAmendment") { return false; }
            var newArticles = obj.Value<JObject>("properties").Value<JArray>("NewArticles");
            foreach (var article in newArticles)
            {
                var properties = article.Value<JObject>("properties");
                var executors = properties.Value<JObject>("Executors");
                if (executors == null || executors.Value<string>("type") == "GameValueWrapper") { return true; }
                var proposers = properties.Value<JObject>("Proposers");
                if (proposers == null || proposers.Value<string>("type") == "GameValueWrapper") { return true; }
            }
            return false;
        }

        private JObject CreateGamePickerList(string mustDeriveType, IEnumerable<JObject> entries)
        {
            var gamePickerListObj = new JObject();
            gamePickerListObj.Add("type", "GamePickerList");
            var mustDeriveTypeObj = new JObject();
            mustDeriveTypeObj.Add("type", "Type");
            mustDeriveTypeObj.Add("value", mustDeriveType);
            gamePickerListObj.Add("mustDeriveType", mustDeriveTypeObj);
            gamePickerListObj.Add("requiredTag", null);
            gamePickerListObj.Add("internalDescription", "Any");
            var entriesArr = new JArray();
            foreach (var entry in entries)
            {
                entriesArr.Add(entry);
            }
            gamePickerListObj.Add("entries", entriesArr);
            return gamePickerListObj;
        }

        public bool ApplyMigration(JObject obj, IList<string> outMigrationReport)
        {
            var newArticles = obj.Value<JObject>("properties").Value<JArray>("NewArticles");
            foreach (var article in newArticles)
            {
                var properties = article.Value<JObject>("properties");
                var executors = properties.Value<JObject>("Executors");
                if (executors == null || executors.Value<string>("type") == "GameValueWrapper")
                {
                    var newExecutors = CreateGamePickerList("Eco.Gameplay.Alias.IAlias", executors != null ? new JObject[]
                    {
                        executors.Value<JObject>("value")
                    } : Enumerable.Empty<JObject>());
                    properties.Remove("Executors");
                    properties.Add("Executors", newExecutors);
                    outMigrationReport.Add($"Replaced a Executors {(executors == null ? "null" : "GameValueWrapper<IAlias>")} with a GameValuePicker in a civic article of '{obj.Value<string>("name")}'");
                }
                var proposers = properties.Value<JObject>("Proposers");
                if (proposers == null || proposers.Value<string>("type") == "GameValueWrapper")
                {
                    var newProposers = CreateGamePickerList("Eco.Gameplay.Alias.IAlias", proposers != null ? new JObject[]
                    {
                        proposers.Value<JObject>("value")
                    } : Enumerable.Empty<JObject>());
                    properties.Remove("Proposers");
                    properties.Add("Proposers", newProposers);
                    outMigrationReport.Add($"Replaced a Proposers {(proposers == null ? "null" : "GameValueWrapper<IAlias>")} with a GameValuePicker in a civic article of '{obj.Value<string>("name")}'");
                }
            }
            return false;
        }


    }
}
