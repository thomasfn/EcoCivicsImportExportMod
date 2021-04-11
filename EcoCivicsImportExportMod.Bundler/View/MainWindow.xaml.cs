using System;
using System.Windows;
using System.Windows.Input;
using System.Linq;

using Microsoft.Win32;
using System.Collections.Generic;

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
            if (e.Parameter is IEnumerable<string> newWith)
            {
                Context.NewWith(newWith);
            }
            else
            {
                Context.New();
            }
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
            Context.Undo();
        }

        private void RedoCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Context.CanRedo;

        private void RedoCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Context.Redo();
        }

        private void AddToBundleCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = Context.CivicBundle != null;

        private void AddToBundleCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            IEnumerable<string> filePaths;
            if (e.Parameter is string str)
            {
                filePaths = new string[] { str };
            }
            else if (e.Parameter is IEnumerable<string> paramFilePaths)
            {
                filePaths = paramFilePaths;
            }
            else
            {
                OpenFileDialog openFileDialog = new OpenFileDialog();
                openFileDialog.Title = "Add Civic to Bundle";
                openFileDialog.Filter = "Json files (*.json)|*.json";
                openFileDialog.CheckFileExists = true;
                openFileDialog.Multiselect = true;
                if (openFileDialog.ShowDialog() != true) { return; }
                filePaths = openFileDialog.FileNames;
            }
            Context.AddCivics(filePaths);
        }

        private void RemoveFromBundleCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            var civicObject = (e.Parameter as ViewModel.CivicObject) ?? (DataContext as ViewModel.MainWindow).CivicBundle?.SelectedCivicObject ?? null;
            if (civicObject == null)
            {
                e.CanExecute = false;
                return;
            }
            if (!Context.CivicBundle.Civics.Any(c => c.AsReference == civicObject.BundledCivic.AsReference))
            {
                e.CanExecute = false;
                return;
            }
            e.CanExecute = true;
        }

        private void RemoveFromBundleCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var civicObject = (e.Parameter as ViewModel.CivicObject) ?? (DataContext as ViewModel.MainWindow).CivicBundle?.SelectedCivicObject ?? null;
            if (civicObject == null) { return; }
            Context.RemoveCivic(civicObject.BundledCivic.AsReference);
        }

        #endregion

        private void Label_Drop(object sender, DragEventArgs e)
        {
            var mainWindow = DataContext as ViewModel.MainWindow;
            if (mainWindow == null) { return; }
            mainWindow.IncomingDrop = false;
            var data = e.Data as DataObject;
            if (data == null) { return; }
            if (!data.ContainsFileDropList()) { return; }
            var fileDropList = data.GetFileDropList();
            string[] arr = new string[fileDropList.Count];
            fileDropList.CopyTo(arr, 0);
            if (ApplicationCommands.New.CanExecute(arr, this))
            {
                ApplicationCommands.New.Execute(arr, this);
            }
        }

        private void Label_DragEnter(object sender, DragEventArgs e)
        {
            var mainWindow = DataContext as ViewModel.MainWindow;
            var data = e.Data as DataObject;
            if (mainWindow == null || data == null) { return; }
            if (!data.ContainsFileDropList()) { return; }
            var fileDropList = data.GetFileDropList();
            string[] arr = new string[fileDropList.Count];
            fileDropList.CopyTo(arr, 0);
            if (!ApplicationCommands.New.CanExecute(arr, this)) { return; }
            mainWindow.IncomingDrop = true;
        }

        private void Label_DragLeave(object sender, DragEventArgs e)
        {
            var mainWindow = DataContext as ViewModel.MainWindow;
            if (mainWindow == null) { return; }
            mainWindow.IncomingDrop = false;
        }

    }
}
