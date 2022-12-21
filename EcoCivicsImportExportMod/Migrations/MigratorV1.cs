using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp.Migrations
{
    using Shared.Utils;

    public class MigratorV1 : ICivicsImpExpMigratorV1
    {
        private static IEnumerable<ICivicsImpExpMigratorV1> InternalMigrators
            => typeof(ICivicsImpExpMigratorV1).ConcreteTypesWithInteface()
                .Except(new Type[] { typeof(MigratorV1), typeof(ExternalMigratorV1) })
                .Select(t => Activator.CreateInstance(t) as ICivicsImpExpMigratorV1);

        private static IEnumerable<ICivicsImpExpMigratorV1> ExternalMigrators
            => ReflectionCache
                .GetGameAssemblies()
                .SelectMany(a => a.GetTypes())
                .Where(t => t.GetInterface("ICivicsImpExpMigratorV1") != null && !t.IsAssignableTo(typeof(ICivicsImpExpMigratorV1)))
                .Select(t => new ExternalMigratorV1(Activator.CreateInstance(t)));

        private readonly IEnumerable<ICivicsImpExpMigratorV1> allMigrators = ExternalMigrators.Concat(InternalMigrators);

        public bool ShouldMigrate(JObject obj)
        {
            foreach (var migrator in allMigrators)
            {
                if (migrator.ShouldMigrate(obj))
                {
                    return true;
                }
            }
            return false;
        }

        public bool ApplyMigration(JObject obj, IList<string> outMigrationReport)
        {
            bool didMigrate = false;
            foreach (var migrator in allMigrators)
            {
                if (migrator.ShouldMigrate(obj))
                {
                    didMigrate |= migrator.ApplyMigration(obj, outMigrationReport);
                }
            }
            return didMigrate;
        }

        
    }
}
