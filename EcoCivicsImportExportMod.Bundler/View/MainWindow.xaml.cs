using System;
using System.Windows;
using System.Windows.Input;

using Microsoft.Win32;

namespace EcoCivicsImportExportMod.Bundler.View
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public Context Context { get; }

        public MainWindow()
        {
            InitializeComponent();
            Context = new Context();
            DataContext = new ViewModel.MainWindow(Context);
        }

        private bool CheckUnsavedChanges()
        {
            if (Context.LastSavePoint == 0) { return true; }
            var result = MessageBox.Show($"The current bundle has unsaved changes. Do you wish to save before proceeding?", "Eco Civic Bundler", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.No) { return true; }
            if (result == MessageBoxResult.Cancel) { return false; }
            if (string.IsNullOrEmpty(Context.FilePath))
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog();
                saveFileDialog.Filter = "Json files (*.json)|*.json";
                if (saveFileDialog.ShowDialog() != true) { return false; }
                Context.SaveAs(saveFileDialog.FileName);
                return true;
            }
            Context.Save();
            return true;
        }

        #region Commands

        private void NewCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;

        private void NewCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!CheckUnsavedChanges()) { return; }
            Context.New();
        }

        private void OpenCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;

        private void OpenCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!CheckUnsavedChanges()) { return; }
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Json files (*.json)|*.json";
            openFileDialog.CheckFileExists = true;
            if (openFileDialog.ShowDialog() != true) { return; }
            Context.Load(openFileDialog.FileName);
        }

        private void SaveCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Context.LastSavePoint != 0;

        private void SaveCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Context.FilePath))
            {
                SaveAsCommand_Executed(sender, e);
                return;
            }
            Context.Save();
        }

        private void SaveAsCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;

        private void SaveAsCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Json files (*.json)|*.json";
            if (saveFileDialog.ShowDialog() != true) { return; }
            Context.SaveAs(saveFileDialog.FileName);
        }

        private void CloseCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;

        private void CloseCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (!CheckUnsavedChanges()) { return; }
            Close();
        }

        private void UndoCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Context.CanUndo;

        private void UndoCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            e.Handled = Context.Undo();
        }

        private void RedoCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Context.CanRedo;

        private void RedoCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            e.Handled = Context.Redo();
        }

        #endregion
    }
}
