using System;
using System.ComponentModel;
using System.Windows;

namespace EcoCivicsImportExportMod.Bundler.ViewModel
{
    public class MainWindow : INotifyPropertyChanged
    {
        public Context Context { get; }

        public Visibility ShowUnloadedHint { get => Context.CivicBundle != null ? Visibility.Collapsed : Visibility.Visible; }

        public Visibility ShowLoadedView { get => Context.CivicBundle != null ? Visibility.Visible : Visibility.Collapsed; }

        public string Filename
        {
            get
            {
                if (Context.CivicBundle == null) { return ""; }
                if (string.IsNullOrEmpty(Context.FilePath)) { return "untitled*"; }
                return $"{System.IO.Path.GetFileName(Context.FilePath)}{(Context.LastSavePoint != 0 ? "*" : "")}";
            }
        }

        public string WindowTitle
        {
            get
            {
                string filename = Filename;
                if (string.IsNullOrEmpty(filename))
                {
                    return "Eco Civics Bundler";
                }
                return $"Eco Civics Bundler - {filename}";
            }
        }

        private CivicBundle civicBundle;

        public CivicBundle CivicBundle
        {
            get => civicBundle;
            private set
            {
                if (value == civicBundle) { return; }
                civicBundle = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CivicBundle)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindow(Context context)
        {
            Context = context;
            Context.OnCivicBundleChange += Context_OnCivicBundleChange;
            Context.OnFilePathChange += Context_OnFilePathChange;
            Context.OnLastSavePointChange += Context_OnLastSavePointChange;
        }

        private void Context_OnCivicBundleChange(object sender, EventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowUnloadedHint)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowLoadedView)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Filename)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WindowTitle)));
            if (Context.CivicBundle == null)
            {
                CivicBundle = null;
            }
            else if (civicBundle != null)
            {
                civicBundle.UnderlyingCivicBundle = Context.CivicBundle;
            }
            else
            {
                CivicBundle = new CivicBundle(Context.CivicBundle);
            }
        }

        private void Context_OnFilePathChange(object sender, EventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Filename)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WindowTitle)));
        }

        private void Context_OnLastSavePointChange(object sender, EventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Filename)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WindowTitle)));
        }
    }
}
