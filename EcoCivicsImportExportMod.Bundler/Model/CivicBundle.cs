using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json.Linq;

namespace EcoCivicsImportExportMod.Bundler.Model
{
    public delegate void NamedObjectVisitor(CivicReference civicReference, JObject obj);

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
            get => FindNamedObjects(Data, true, true)
                .Select(t => t.Item1)
                .Distinct();
        }

        public IEnumerable<BundledCivic> InlineObjects
        {
            get => FindNamedObjects(Data, false, true)
                .Select(t => new BundledCivic(t.Item2));
        }

        public BundledCivic(JObject data)
        {
            Data = data;
        }

        public void VisitInlineObjects(NamedObjectVisitor visitor)
            => VisitNamedObjects(visitor, Data, false, true);

        public void VisitReferences(NamedObjectVisitor visitor)
            => VisitNamedObjects(visitor, Data, true, true);

        private static IEnumerable<(CivicReference CivicReference, JObject obj)> FindNamedObjects(JToken target, bool? references = null, bool ignoreRoot = false)
        {
            var result = new List<(CivicReference CivicReference, JObject Obj)>();
            VisitNamedObjects((civicReference, obj) => result.Add((civicReference, obj)), target, references, ignoreRoot);
            return result;
        }

        private static void VisitNamedObjects(NamedObjectVisitor visitor, JToken target, bool? references = null, bool ignoreRoot = false)
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
                        visitor(new CivicReference(typeName, name), obj);
                    }
                    return;
                }
                foreach (var pair in obj)
                {
                    VisitNamedObjects(visitor, pair.Value, references);
                }
            }
            else if (target is JArray arr)
            {
                foreach (var element in arr)
                {
                    VisitNamedObjects(visitor, element, references);
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
            if (typeName == "Eco.Mods.CivicsImpExp.CivicBundle")
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

        public string SaveToText()
        {
            JObject jsonObj = new JObject();
            jsonObj.Add("type", "Eco.Mods.CivicsImpExp.CivicBundle");
            jsonObj.Add("version", new JArray(MajorVersion, MinorVersion));
            JArray civicsArr = new JArray();
            jsonObj.Add("civics", civicsArr);
            foreach (var civic in Civics)
            {
                civicsArr.Add(civic.Data);
            }
            return jsonObj.ToString();
        }
    }
}
