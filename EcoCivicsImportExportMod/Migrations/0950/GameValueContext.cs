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

    public class GameValueContext : ICivicsImpExpMigratorV1
    {
        public bool ShouldMigrate(JObject obj)
            => GetGameValueContexts(obj)
                .Any(x => !string.IsNullOrEmpty(x.Value<string>("contextName")) || !string.IsNullOrEmpty(x.Value<string>("titleBacking")) || !string.IsNullOrEmpty(x.Value<string>("tooltip")));

        private IEnumerable<JObject> GetGameValueContexts(JObject obj)
            => obj.GetNestedObjects().Where(x => x.Value<string>("type") == "GameValueContext");

        public bool ApplyMigration(JObject obj, IList<string> outMigrationReport)
        {
            bool didMigrate = false;
            foreach (var gameValueContextObj in GetGameValueContexts(obj))
            {
                if (RenameProperty(gameValueContextObj, "contextName", "_name"))
                {
                    outMigrationReport.Add($"Renamed 'contextName' to '_name' in '{gameValueContextObj.Value<string>("type")}'");
                    didMigrate = true;
                }
                if (RenameProperty(gameValueContextObj, "titleBacking", "markedUpName"))
                {
                    outMigrationReport.Add($"Renamed 'titleBacking' to 'markedUpName' in '{gameValueContextObj.Value<string>("type")}'");
                    didMigrate = true;
                }
                if (RenameProperty(gameValueContextObj, "tooltip", "contextDescription"))
                {
                    outMigrationReport.Add($"Renamed 'tooltip' to 'contextDescription' in '{gameValueContextObj.Value<string>("type")}'");
                    didMigrate = true;
                }
            }
            return didMigrate;
        }

        private bool RenameProperty(JObject target, string oldKey, string newKey)
        {
            var oldValue = target.Value<JToken>(oldKey);
            var newValue = target.Value<JToken>(newKey);
            if (newValue == null && oldValue != null)
            {
                target[newKey] = oldValue;
                target.Remove(oldKey);
                return true;
            }
            return false;
        }
    }
}
