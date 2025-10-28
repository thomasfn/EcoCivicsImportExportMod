using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp.Migrations._1110
{
    using Shared.Utils;

    public class CustomStatUser : ICivicsImpExpMigratorV1
    {
        private const string CustomStatTypeName = "Eco.Gameplay.Civics.GameValues.Values.Stats.CustomStatQuery";
        private const string OldUserKey = "User";
        private const string NewUserKey = "RestrictCountToCitizen";

        public bool ShouldMigrate(JObject obj)
            => GetMigrateTargets(obj)
                .Any();

        private static IEnumerable<JObject> GetMigrateTargets(JObject obj)
            => obj.GetNestedObjects().Where(x => x.Value<string>("type") == CustomStatTypeName);

        public bool ApplyMigration(JObject obj, IList<string> outMigrationReport)
        {
            bool didMigrate = false;
            foreach (var customStatQueryObj in GetMigrateTargets(obj))
            {
                var propertiesObj = customStatQueryObj.Value<JObject>("properties");
                if (propertiesObj == null) { continue; }

                var userValue = propertiesObj.Value<JToken>(OldUserKey);
                if (userValue == null) { continue; }

                propertiesObj[NewUserKey] = userValue;
                propertiesObj.Remove(OldUserKey);

                outMigrationReport.Add($"Changed '{OldUserKey}' to '{NewUserKey}'");
                didMigrate = true;
            }
            return didMigrate;
        }
    }
}
