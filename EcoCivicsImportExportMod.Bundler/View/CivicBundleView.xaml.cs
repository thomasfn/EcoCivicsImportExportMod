using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace EcoCivicsImportExportMod.Bundler.View
{
    /// <summary>
    /// Interaction logic for CivicBundleView.xaml
    /// </summary>
    public partial class CivicBundleView : UserControl
    {
        public CivicBundleView()
        {
            InitializeComponent();
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var civicBundle = DataContext as ViewModel.CivicBundle;
            if (civicBundle != null && e.NewValue is ViewModel.CivicObject civicObject)
            {
                civicBundle.SelectedCivicObject = civicObject;
            }
            else
            {
                civicBundle.SelectedCivicObject = null;
            }
        }

        private void TreeView_Drop(object sender, DragEventArgs e)
        {
            var bundle = DataContext as ViewModel.CivicBundle;
            if (bundle == null) { return; }
            bundle.IncomingDrop = false;
            var data = e.Data as DataObject;
            if (data == null) { return; }
            if (!data.ContainsFileDropList()) { return; }
            var fileDropList = data.GetFileDropList();
            string[] arr = new string[fileDropList.Count];
            fileDropList.CopyTo(arr, 0);
            if (BundlerCommands.AddToBundle.CanExecute(arr, this))
            {
                BundlerCommands.AddToBundle.Execute(arr, this);
            }
        }

        private void TreeView_DragEnter(object sender, DragEventArgs e)
        {
            var bundle = DataContext as ViewModel.CivicBundle;
            var data = e.Data as DataObject;
            if (bundle == null || data == null) { return; }
            if (!data.ContainsFileDropList()) { return; }
            var fileDropList = data.GetFileDropList();
            string[] arr = new string[fileDropList.Count];
            fileDropList.CopyTo(arr, 0);
            if (!BundlerCommands.AddToBundle.CanExecute(arr, this)) { return; }
            bundle.IncomingDrop = true;
        }

        private void TreeView_DragLeave(object sender, DragEventArgs e)
        {
            var bundle = DataContext as ViewModel.CivicBundle;
            if (bundle == null) { return; }
            bundle.IncomingDrop = false;
        }
    }
}
