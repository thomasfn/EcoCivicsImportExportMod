using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;

    using Shared.Utils;

    public readonly struct CivicReference : IEquatable<CivicReference>
    {
        public readonly Type Type;
        public readonly string Name;

        public CivicReference(Type type, string name)
        {
            Type = type;
            Name = name;
        }

        public IHasID Resolve()
            => Registrars.GetByDerivedType(Type).GetByName(Name);

        public override bool Equals(object obj)
            => obj is CivicReference reference && Equals(reference);

        public bool Equals(CivicReference other)
            => EqualityComparer<Type>.Default.Equals(Type, other.Type)
            && Name == other.Name;

        public override int GetHashCode()
            => HashCode.Combine(Type, Name);

        public static bool operator ==(CivicReference left, CivicReference right)
            => left.Equals(right);

        public static bool operator !=(CivicReference left, CivicReference right)
            => !(left == right);

        public override string ToString()
            => $"{Type.Name}:\"{Name}\"";
    }

    public readonly struct BundledCivic
    {
        public readonly JObject Data;

        public string Name { get => Data.Value<string>("name"); }

        public string TypeName { get => Data.Value<string>("type"); }

        public Type Type { get => ReflectionUtils.GetTypeFromFullName(TypeName); }

        public CivicReference AsReference { get => new CivicReference(Type, Name); }

        public IEnumerable<CivicReference> References
        {
            get => SearchForInlineNamedObjects(Data, true, true)
                .Select(t => t.Item1)
                .Distinct();
        }

        public IEnumerable<BundledCivic> InlineObjects
        {
            get => SearchForInlineNamedObjects(Data, false, true)
                .Select(t => new BundledCivic(t.Item2));
        }

        public BundledCivic(JObject data)
        {
            Data = data;
        }

        private static IEnumerable<(CivicReference, JObject)> SearchForInlineNamedObjects(JToken target, bool? references = null, bool ignoreRoot = false)
        {
            if (target is JObject obj)
            {
                string name = obj.Value<string>("name");
                string typeName = obj.Value<string>("type");
                bool isRef = obj.Value<bool>("reference");
                if (!ignoreRoot && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(typeName))
                {
                    if (references == null || references.Value == isRef)
                    {
                        yield return (new CivicReference(ReflectionUtils.GetTypeFromFullName(typeName), name), obj);
                    }
                    yield break;
                }
                foreach (var pair in obj)
                {
                    foreach (var referenceTuple in SearchForInlineNamedObjects(pair.Value, references))
                    {
                        yield return referenceTuple;
                    }
                }
            }
            else if (target is JArray arr)
            {
                foreach (var element in arr)
                {
                    foreach (var referenceTuple in SearchForInlineNamedObjects(element, references))
                    {
                        yield return referenceTuple;
                    }
                }
            }
        }

        public IHasID CreateStub()
        {
            var registrar = Registrars.GetByDerivedType(Type);
            if (registrar == null)
            {
                throw new InvalidOperationException($"No registrar found for type '{Type.FullName}'");
            }
            var obj = Activator.CreateInstance(Type) as IHasID;
            registrar.Insert(obj);
            return obj;
        }
    }

    public class CivicBundle
    {
        #region Importing

        public static CivicBundle LoadFromText(string text)
        {
            JObject jsonObj = JObject.Parse(text);
            string typeName = jsonObj.Value<string>("type");
            var versionArr = jsonObj.Value<JArray>("version");
            int verMaj = versionArr.Value<int>(0);
            //int verMin = versionArr.Value<int>(1);
            if (verMaj != CivicsJsonConverter.MajorVersion)
            {
                throw new InvalidOperationException($"Civic format not supported (found major '{verMaj}', expecting '{CivicsJsonConverter.MajorVersion}'");
            }
            if (typeName == typeof(CivicBundle).FullName)
            {
                // Importing formal bundle with multiple civics
                var civics = jsonObj.Value<JArray>("civics");
                var bundle = new CivicBundle();
                for (int i = 0; i < civics.Count; ++i)
                {
                    var element = civics.Value<JObject>(i);
                    bundle.civics.Add(new BundledCivic(element));
                }
                return bundle;
            }
            else
            {
                // Importing single civic
                var bundle = new CivicBundle();
                bundle.civics.Add(new BundledCivic(jsonObj));
                return bundle;
            }
        }

        #endregion

        private readonly IList<BundledCivic> civics = new List<BundledCivic>();

        public IEnumerable<BundledCivic> Civics { get => civics; }

        public IEnumerable<CivicReference> AllReferences { get => Civics.SelectMany(c => c.References).Distinct(); }

        public IEnumerable<BundledCivic> AllInlineObjects { get => civics.SelectMany(c => c.InlineObjects); }

        public IEnumerable<CivicReference> ExternalReferences { get => AllReferences.Where(r => !ReferenceIsLocal(r)); }

        public bool ReferenceIsLocal(CivicReference reference)
            => Civics.Select(c => c.AsReference).Contains(reference)
            || AllInlineObjects.Select(c => c.AsReference).Contains(reference);

        public IEnumerable<string> ApplyMigrations()
        {
            var migrationReport = new List<string>();
            var migrator = new Migrations.MigratorV1();
            foreach (var obj in Civics)
            {
                if (migrator.ShouldMigrate(obj.Data))
                {
                    migrationReport.Add($"Applying migrations for {obj.AsReference}...");
                    migrator.ApplyMigration(obj.Data, migrationReport);
                }
            }
            return migrationReport;
        }

        public IEnumerable<IHasID> ImportAll()
        {
            var importContext = new ImportContext();
            try
            {
                foreach (var civic in Civics)
                {
                    importContext.ImportStub(civic);
                    foreach (var inlineCivic in civic.InlineObjects)
                    {
                        importContext.ImportStub(inlineCivic);
                    }
                }
                foreach (var civic in Civics)
                {
                    importContext.Import(civic);
                }
            }
            catch
            {
                importContext.Clear();
                throw;
            }
            return importContext.ImportedObjects;
        }
    }
}
