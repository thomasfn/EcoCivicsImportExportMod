using System;
using System.Collections.Generic;
using System.Reflection;

using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp.Migrations
{
    public class ExternalMigratorV1 : ICivicsImpExpMigratorV1
    {
        private readonly object migratorObj;
        private readonly MethodInfo shouldMigrateMethod;
        private readonly MethodInfo applyMigrationMethod;

        public ExternalMigratorV1(object migratorObj)
        {
            this.migratorObj = migratorObj;
            shouldMigrateMethod = migratorObj.GetType().GetMethod("ShouldMigrate", BindingFlags.Public | BindingFlags.Instance);
            applyMigrationMethod = migratorObj.GetType().GetMethod("ApplyMigration", BindingFlags.Public | BindingFlags.Instance);
        }

        public bool ShouldMigrate(JObject obj)
            => (bool)shouldMigrateMethod.Invoke(migratorObj, new object[] { obj });

        public bool ApplyMigration(JObject obj, IList<string> outMigrationReport)
            => (bool)applyMigrationMethod.Invoke(migratorObj, new object[] { obj, outMigrationReport });

        public override string ToString()
            => $"ExternalMigratorV1({migratorObj})";
    }
}
