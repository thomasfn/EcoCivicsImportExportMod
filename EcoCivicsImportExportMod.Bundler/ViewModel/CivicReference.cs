using System;
using System.ComponentModel;

namespace EcoCivicsImportExportMod.Bundler.ViewModel
{
    public class CivicReference : INotifyPropertyChanged
    {
        private Model.CivicReference underlyingCivicReference;

        public Model.CivicReference UnderlyingCivicReference
        {
            get => underlyingCivicReference;
            set
            {
                if (value == underlyingCivicReference) { return; }
                underlyingCivicReference = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnderlyingCivicReference)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullType)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSource)));
            }
        }

        public string Name { get => underlyingCivicReference.Name; }

        public string FullType { get => underlyingCivicReference.Type; }

        public string IconSource { get => Icons.TypeToIconSource(FullType); }

        public event PropertyChangedEventHandler PropertyChanged;

        public CivicReference(Model.CivicReference underlyingCivicReference)
        {
            this.underlyingCivicReference = underlyingCivicReference;
        }
    }
}
