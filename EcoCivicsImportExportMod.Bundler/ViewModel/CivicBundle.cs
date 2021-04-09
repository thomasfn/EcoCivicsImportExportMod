using System;
using System.ComponentModel;
using System.Windows;

namespace EcoCivicsImportExportMod.Bundler.ViewModel
{
    public class CivicBundle : INotifyPropertyChanged
    {
        private Model.CivicBundle underlyingCivicBundle;
        public Model.CivicBundle UnderlyingCivicBundle
        {
            get => underlyingCivicBundle;
            set
            {
                if (value == underlyingCivicBundle) { return; }
                underlyingCivicBundle = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnderlyingCivicBundle)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public CivicBundle(Model.CivicBundle underlyingCivicBundle)
        {
            this.underlyingCivicBundle = underlyingCivicBundle;
        }
    }
}
