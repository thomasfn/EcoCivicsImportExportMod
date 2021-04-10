using System;
using System.ComponentModel;
using System.Collections.ObjectModel;

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
                UpdateSubobjects();
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

        public ObservableCollection<CivicObject> SubObjects { get; } = new ObservableCollection<CivicObject>();

        public event PropertyChangedEventHandler PropertyChanged;

        public CivicObject(CivicBundle bundle, Model.BundledCivic bundledCivic)
        {
            this.bundle = bundle;
            this.bundledCivic = bundledCivic;
            UpdateSubobjects();
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
    }
}
