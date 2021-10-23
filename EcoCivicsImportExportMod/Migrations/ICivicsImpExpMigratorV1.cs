using System;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp
{
    public interface ICivicsImpExpMigratorV1
    {
        bool ShouldMigrate(JObject obj);

        bool ApplyMigration(JObject obj, IList<string> outMigrationReport);
    }
}
