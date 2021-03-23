using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;

namespace Eco.Mods.CivicsImpExp
{
    using Core.Systems;

    using Shared.Utils;

    using Gameplay.Civics.Elections;

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
            => Registrars.Get(Type).GetByName(Name);

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
    }

    public readonly struct BundledCivic
    {
        public readonly JObject Data;

        public string Name { get => Data.Value<string>("name"); }

        public string TypeName { get => Data.Value<string>("type"); }

        public Type Type { get => ReflectionUtils.GetTypeFromFullName(TypeName); }

        public IEnumerable<CivicReference> References { get => SearchForReferences(Data).Distinct(); }

        public BundledCivic(JObject data)
        {
            Data = data;
        }

        private static IEnumerable<CivicReference> SearchForReferences(JToken target)
        {
            if (target is JObject obj)
            {
                string name = obj.Value<string>("name");
                string typeName = obj.Value<string>("type");
                bool isRef = obj.Value<bool>("reference");
                if (isRef && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(typeName))
                {
                    yield return new CivicReference(ReflectionUtils.GetTypeFromFullName(typeName), name);
                    yield break;
                }
                foreach (var pair in obj)
                {
                    foreach (var reference in SearchForReferences(pair.Value))
                    {
                        yield return reference;
                    }
                }
            }
            else if (target is JArray arr)
            {
                foreach (var element in arr)
                {
                    foreach (var reference in SearchForReferences(element))
                    {
                        yield return reference;
                    }
                }
            }
        }

        public IHasID CreateStub()
        {
            var obj = Activator.CreateInstance(Type) as IHasID;
            obj.Name = Name;
            Registrars.Get(Type).Insert(obj);
            return obj;
        }

        public void Import(IHasID target)
        {
            if (target.GetType() != Type)
            {
                throw new ArgumentException($"Type mismatch (expecting '{Type.FullName}', got '{target.GetType().FullName}')");
            }
            Importer.DeserialiseGenericObject(Data, target);
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

        public IEnumerable<CivicReference> ExternalReferences { get => AllReferences.Where(r => !ReferenceIsLocal(r)); }

        public bool ReferenceIsLocal(CivicReference reference)
            => Civics.Any(c => c.Type == reference.Type && c.Name == reference.Name);

        public IEnumerable<IHasID> ImportAll()
        {
            var importContext = new ImportContext();
            try
            {
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
