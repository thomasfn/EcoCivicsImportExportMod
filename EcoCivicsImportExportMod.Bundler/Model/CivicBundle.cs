using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;

namespace EcoCivicsImportExportMod.Bundler.Model
{
    public readonly struct CivicReference : IEquatable<CivicReference>
    {
        public readonly string Type;
        public readonly string Name;

        public CivicReference(string type, string name)
        {
            Type = type;
            Name = name;
        }

        public override bool Equals(object obj)
            => obj is CivicReference reference && Equals(reference);

        public bool Equals(CivicReference other)
            => Type == other.Type
            && Name == other.Name;

        public override int GetHashCode()
            => HashCode.Combine(Type, Name);

        public static bool operator ==(CivicReference left, CivicReference right)
            => left.Equals(right);

        public static bool operator !=(CivicReference left, CivicReference right)
            => !(left == right);

        public override string ToString()
            => $"{Type}:\"{Name}\"";
    }

    public readonly struct BundledCivic : ICloneable
    {
        public readonly JObject Data;

        public string Name { get => Data.Value<string>("name"); }

        public string Type { get => Data.Value<string>("type"); }

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
                        yield return (new CivicReference(typeName, name), obj);
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

        public object Clone()
            => new BundledCivic(Data.DeepClone() as JObject);
    }

    public class CivicBundle : ICloneable
    {
        public const int MajorVersion = 1;
        public const int MinorVersion = 0;

        #region Importing

        public static CivicBundle LoadFromText(string text)
        {
            JObject jsonObj = JObject.Parse(text);
            string typeName = jsonObj.Value<string>("type");
            var versionArr = jsonObj.Value<JArray>("version");
            int verMaj = versionArr.Value<int>(0);
            //int verMin = versionArr.Value<int>(1);
            if (verMaj != MajorVersion)
            {
                throw new InvalidOperationException($"Civic format not supported (found major '{verMaj}', expecting '{MajorVersion}'");
            }
            if (typeName == typeof(CivicBundle).FullName)
            {
                // Importing formal bundle with multiple civics
                var civics = jsonObj.Value<JArray>("civics");
                var civicsList = new List<BundledCivic>();
                for (int i = 0; i < civics.Count; ++i)
                {
                    var element = civics.Value<JObject>(i);
                    civicsList.Add(new BundledCivic(element));
                }
                return new CivicBundle(civicsList);
            }
            else
            {
                // Importing single civic
                return new CivicBundle(new BundledCivic[] { new BundledCivic(jsonObj) });
            }
        }

        #endregion

        private readonly BundledCivic[] civics;

        public IEnumerable<BundledCivic> Civics { get => civics; }

        public IEnumerable<CivicReference> AllReferences { get => Civics.SelectMany(c => c.References).Distinct(); }

        public IEnumerable<BundledCivic> AllInlineObjects { get => civics.SelectMany(c => c.InlineObjects); }

        public IEnumerable<CivicReference> ExternalReferences { get => AllReferences.Where(r => !ReferenceIsLocal(r)); }

        public CivicBundle(IEnumerable<BundledCivic> civics = null)
        {
            this.civics = civics != null ? civics.ToArray() : new BundledCivic[0];
        }

        public bool ReferenceIsLocal(CivicReference reference)
            => Civics.Select(c => c.AsReference).Contains(reference)
            || AllInlineObjects.Select(c => c.AsReference).Contains(reference);

        public object Clone()
            => new CivicBundle(Civics.Select(c => (BundledCivic)c.Clone()));
    }
}
