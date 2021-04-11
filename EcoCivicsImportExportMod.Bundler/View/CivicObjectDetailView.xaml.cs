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
    /// Interaction logic for CivicObjectDetailView.xaml
    /// </summary>
    public partial class CivicObjectDetailView : UserControl
    {
        public static readonly RoutedUICommand UpdateTextBoxBindingOnEnterCommand = new RoutedUICommand
            (
                "Enter",
                "Enter",
                typeof(CivicObjectDetailView)
            );

        public CivicObjectDetailView()
        {
            InitializeComponent();
        }

        private void CanExecuteUpdateTextBoxBindingOnEnterCommand(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;

        private void ExecuteUpdateTextBoxBindingOnEnterCommand(object sender, ExecutedRoutedEventArgs e)
        {
            TextBox tBox = e.Parameter as TextBox;
            if (tBox != null)
            {
                DependencyProperty prop = TextBox.TextProperty;
                BindingExpression binding = BindingOperations.GetBindingExpression(tBox, prop);
                if (binding != null)
                    binding.UpdateSource();
            }
        }
    }
}
