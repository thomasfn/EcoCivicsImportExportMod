using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp.Migrations._1000
{
    using Shared.Utils;

    public class MoneyLegalActionNamespace : ICivicsImpExpMigratorV1
    {
        private static readonly Dictionary<string, string> typeRenames = new()
        {
            { "Eco.Gameplay.Civics.LegalActions.TransferToAccount_LegalAction", "Eco.Gameplay.Civics.Laws.LegalActions.Money.TransferToAccount_LegalAction" },
            { "Eco.Gameplay.Civics.LegalActions.Pay_LegalAction", "Eco.Gameplay.Civics.Laws.LegalActions.Money.Pay_LegalAction" },
            { "Eco.Gameplay.Civics.LegalActions.Tax_LegalAction", "Eco.Gameplay.Civics.Laws.LegalActions.Money.Tax_LegalAction" }
        };

        public bool ShouldMigrate(JObject obj)
            => GetRenameTargets(obj)
                .Any();

        private bool IsRenameTarget(string typeName)
            => !string.IsNullOrEmpty(typeName) && typeRenames.ContainsKey(typeName);

        private IEnumerable<JObject> GetRenameTargets(JObject obj)
            => obj.GetNestedObjects().Where(x => IsRenameTarget(x.Value<string>("type")));

        public bool ApplyMigration(JObject obj, IList<string> outMigrationReport)
        {
            bool didMigrate = false;
            foreach (var legalActionObj in GetRenameTargets(obj))
            {
                var oldType = legalActionObj.Value<string>("type");
                if (!typeRenames.TryGetValue(oldType, out var newType)) { continue; }
                legalActionObj["type"] = newType;
                outMigrationReport.Add($"Changed '{oldType}' to '{newType}'");
                didMigrate = true;
            }
            return didMigrate;
        }
    }
}
