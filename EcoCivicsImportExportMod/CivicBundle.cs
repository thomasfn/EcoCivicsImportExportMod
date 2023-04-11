using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;

    using Gameplay.Civics;
    using Gameplay.Settlements;

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

        public Type Type
        {
            get
            {
                var type = ReflectionUtils.GetTypeFromFullName(TypeName);
                if (type == null) { throw new Exception($"Failed to resolve type '{TypeName}'"); }
                return type;
            }
        }

        public CivicReference AsReference { get => new(Type, Name); }

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

        public bool Is<T>() where T : IHasID
        {
            return typeof(T).IsAssignableFrom(Type);
        }
    }

    public readonly struct BundledSettlementCivic
    {
        public readonly BundledCivic BundledCivic;

        public string Name { get => BundledCivic.Name; }

        public string TypeName { get => BundledCivic.TypeName; }

        public Type Type { get => BundledCivic.Type; }

        public CivicReference AsReference { get => BundledCivic.AsReference; }

        public IEnumerable<CivicReference> References { get => BundledCivic.References; }

        public IEnumerable<BundledCivic> InlineObjects { get => BundledCivic.InlineObjects; }

        public CivicReference? LeaderReference { get => GetReferenceProperty(nameof(Settlement.Leader)); }

        public CivicReference? ImmigrationPolicyReference { get => GetReferenceProperty(nameof(Settlement.ImmigrationPolicy)); }

        public CivicReference? ElectionProcessReference { get => GetReferenceProperty(nameof(Settlement.ElectionProcess)); }

        public CivicReference? ConstitutionReference { get => GetReferenceProperty(nameof(Settlement.Constitution)); }

        public CivicReference? CitizenDemographicReference { get => GetReferenceProperty(nameof(Settlement.CitizenDemographic)); }

        public BundledSettlementCivic(BundledCivic bundledCivic)
        {
            BundledCivic = bundledCivic;
        }

        private CivicReference? GetReferenceProperty(string key)
        {
            var propertiesToken = BundledCivic.Data["properties"];
            if (propertiesToken is not JObject propertiesObj) { return null; }
            var referenceToken = propertiesObj[key];
            if (referenceToken is not JObject referenceObj) { return null; }
            string name = referenceObj.Value<string>("name");
            string typeName = referenceObj.Value<string>("type");
            bool isRef = referenceObj.Value<bool>("reference");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(typeName) || !isRef) { return null; }
            var type = ReflectionUtils.GetTypeFromFullName(typeName);
            if (type == null) { return null; }
            return new CivicReference(type, name);
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
                throw new InvalidOperationException($"Civic format not supported (found major '{verMaj}', expecting '{CivicsJsonConverter.MajorVersion}')");
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

        public bool ContainsSettlement { get => Civics.Any(c => c.Is<Settlement>()); }

        public BundledCivic? Settlement { get => Civics.Select(c => new BundledCivic?(c)).SingleOrDefault(c => c.Value.Is<Settlement>()); }

        public BundledCivic? Constitution { get => Civics.Select(c => new BundledCivic?(c)).SingleOrDefault(c => c.Value.Is<Constitution>()); }

        public IReadOnlyDictionary<CivicReference, IHasID> GetSettlementOverwriteCivics(Settlement targetSettlement)
        {
            var dict = new Dictionary<CivicReference, IHasID>();
            var importSettlementCivicRaw = Settlement;
            if (importSettlementCivicRaw == null) { return dict; }
            var importSettlementCivic = new BundledSettlementCivic(importSettlementCivicRaw.Value);
            dict.Add(importSettlementCivic.AsReference, targetSettlement);
            var leaderRef = importSettlementCivic.LeaderReference;
            if (leaderRef != null && targetSettlement.Leader != null && Civics.Any(c => c.AsReference == leaderRef)) { dict.Add(leaderRef.Value, targetSettlement.Leader); }
            var immigrationPolicyRef = importSettlementCivic.ImmigrationPolicyReference;
            if (immigrationPolicyRef != null && targetSettlement.ImmigrationPolicy != null && Civics.Any(c => c.AsReference == immigrationPolicyRef)) { dict.Add(immigrationPolicyRef.Value, targetSettlement.ImmigrationPolicy); }
            var electionProcessRef = importSettlementCivic.ElectionProcessReference;
            if (electionProcessRef != null && targetSettlement.ElectionProcess != null && Civics.Any(c => c.AsReference == electionProcessRef)) { dict.Add(electionProcessRef.Value, targetSettlement.ElectionProcess); }
            var constitutionRef = importSettlementCivic.ConstitutionReference;
            if (constitutionRef != null && targetSettlement.Constitution != null && Civics.Any(c => c.AsReference == constitutionRef)) { dict.Add(constitutionRef.Value, targetSettlement.Constitution); }
            var citizenDemographicRef = importSettlementCivic.CitizenDemographicReference;
            if (citizenDemographicRef != null && targetSettlement.CitizenDemographic != null && Civics.Any(c => c.AsReference == citizenDemographicRef)) { dict.Add(citizenDemographicRef.Value, targetSettlement.CitizenDemographic); }
            return dict;
        }

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

        public IEnumerable<IHasID> ImportAll(Settlement targetSettlement)
        {
            var importContext = new ImportContext();
            var importSettlementCivic = Settlement;
            if (importSettlementCivic != null)
            {
                var settlementOverwriteCivics = GetSettlementOverwriteCivics(targetSettlement);
                foreach (var pair in settlementOverwriteCivics)
                {
                    importContext.ReferenceMap.Add(pair);
                }
                importContext.ImportedObjects.AddUniqueRange(settlementOverwriteCivics.Values);
            }
            try
            {
                foreach (var civic in Civics)
                {
                    if (importContext.ReferenceMap.ContainsKey(civic.AsReference)) { continue; }
                    try
                    {
                        importContext.ImportStub(civic);
                        foreach (var inlineCivic in civic.InlineObjects)
                        {
                            importContext.ImportStub(inlineCivic);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to import stub for civic {civic.AsReference}: {ex}");
                        throw;
                    }
                }
                foreach (var civic in Civics)
                {
                    try
                    {
                        importContext.Import(civic, targetSettlement);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to import civic {civic.AsReference}: {ex}");
                        throw;
                    }
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
