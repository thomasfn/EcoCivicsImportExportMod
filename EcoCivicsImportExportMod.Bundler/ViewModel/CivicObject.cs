using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace EcoCivicsImportExportMod.Bundler.ViewModel
{
    public class CivicObject : INotifyPropertyChanged
    {
        private CivicBundle bundle;
        private Model.BundledCivic bundledCivic;

        public CivicBundle Bundle
        {
            get => bundle;
            set
            {
                if (value == bundle) { return; }
                bundle = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Bundle)));
            }
        }

        public Model.BundledCivic BundledCivic
        {
            get => bundledCivic;
            set
            {
                bundledCivic = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BundledCivic)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RawJson)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSource)));
                UpdateSubobjects();
                UpdateReferences();
                
            }
        }

        public string Name
        {
            get => BundledCivic.Name;
            set
            {
                bundle.Context.RenameCivic(bundledCivic.AsReference, value);
            }
        }

        public string Description
        {
            get => BundledCivic.Data["description"]?.ToString() ?? string.Empty;
            set
            {
                bundle.Context.MutateBundledCivic(bundledCivic.AsReference, (in Model.BundledCivic bundledCivic) =>
                {
                    bundledCivic.Data["description"] = value;
                });
            }
        }

        public string RawJson
        {
            get => bundledCivic.Data.ToString();
        }

        public string IconSource
        {
            get => Icons.TypeToIconSource(bundledCivic.Type);
        }

        public ObservableCollection<CivicObject> SubObjects { get; } = new ObservableCollection<CivicObject>();

        public ObservableCollection<CivicReference> InternalReferences { get; } = new ObservableCollection<CivicReference>();

        public ObservableCollection<CivicReference> ExternalReferences { get; } = new ObservableCollection<CivicReference>();

        public ObservableCollection<CivicReference> InternalDependants { get; } = new ObservableCollection<CivicReference>();

        public event PropertyChangedEventHandler PropertyChanged;

        public CivicObject(CivicBundle bundle, Model.BundledCivic bundledCivic)
        {
            this.bundle = bundle;
            this.bundledCivic = bundledCivic;
            UpdateSubobjects();
            UpdateReferences();
        }

        private void UpdateSubobjects()
        {
            int i = 0;
            foreach (var inlineObject in BundledCivic.InlineObjects)
            {
                CivicObject civicObject;
                if (i >= SubObjects.Count)
                {
                    civicObject = new CivicObject(bundle, inlineObject);
                    SubObjects.Add(civicObject);
                }
                else
                {
                    civicObject = SubObjects[i];
                    civicObject.BundledCivic = inlineObject;
                }
                ++i;
            }
            while (i < SubObjects.Count)
            {
                SubObjects.RemoveAt(i);
            }
        }

        private IEnumerable<Model.CivicReference> SortCivicReferences(IEnumerable<Model.CivicReference> civicReferences)
        {
            return civicReferences
                .GroupBy(c => c.Type)
                .OrderBy(g => CivicBundle.PreferredSortOrderForType.TryGetValue(g.Key, out int sortOrder) ? sortOrder : int.MaxValue)
                .Select(g => g.OrderBy(c => c.Name))
                .SelectMany(g => g);
        }

        private void UpdateReferences()
        {
            InternalReferences.SetFromEnumerable(
                SortCivicReferences(bundledCivic.References.Where(r => bundle.UnderlyingCivicBundle.ReferenceIsLocal(r))),
                (in Model.CivicReference civicReference) => new CivicReference(civicReference),
                (CivicReference viewModel, in Model.CivicReference civicReference) => viewModel.UnderlyingCivicReference = civicReference
            );
            ExternalReferences.SetFromEnumerable(
                SortCivicReferences(bundledCivic.References.Where(r => !bundle.UnderlyingCivicBundle.ReferenceIsLocal(r))),
                (in Model.CivicReference civicReference) => new CivicReference(civicReference),
                (CivicReference viewModel, in Model.CivicReference civicReference) => viewModel.UnderlyingCivicReference = civicReference
            );
            InternalDependants.SetFromEnumerable(
                SortCivicReferences(bundle.UnderlyingCivicBundle.Civics.Where(c => c.References.Contains(bundledCivic.AsReference)).Select(c => c.AsReference)),
                (in Model.CivicReference civicReference) => new CivicReference(civicReference),
                (CivicReference viewModel, in Model.CivicReference civicReference) => viewModel.UnderlyingCivicReference = civicReference
            );
        }
    }
}
