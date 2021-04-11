using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace EcoCivicsImportExportMod.Bundler
{
    using Model;
    using System.Windows;

    public delegate CivicBundle CivicBundleMutator(CivicBundle oldCivicBundle);
    public delegate void BundledCivicMutator(in BundledCivic bundledCivic);

    public class Context
    {
        private readonly Stack<CivicBundle> undoStack = new Stack<CivicBundle>();
        private readonly Stack<CivicBundle> redoStack = new Stack<CivicBundle>();

        private CivicBundle civicBundle;
        private string filePath;
        private int lastSavePoint;

        public CivicBundle CivicBundle
        {
            get => civicBundle;
            private set
            {
                if (value == civicBundle) { return; }
                civicBundle = value;
                OnCivicBundleChange?.Invoke(this, EventArgs.Empty);
            }
        }

        public string FilePath
        {
            get => filePath;
            private set
            {
                if (value == filePath) { return; }
                filePath = value;
                OnFilePathChange?.Invoke(this, EventArgs.Empty);
            }
        }

        public int LastSavePoint
        {
            get => lastSavePoint;
            private set
            {
                if (value == lastSavePoint) { return; }
                lastSavePoint = value;
                OnLastSavePointChange?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool CanUndo => undoStack.Count > 0;

        public bool CanRedo => redoStack.Count > 0;

        public event EventHandler OnCivicBundleChange;
        public event EventHandler OnFilePathChange;
        public event EventHandler OnLastSavePointChange;

        public void New()
        {
            CivicBundle = new CivicBundle();
            FilePath = null;
            undoStack.Clear();
            redoStack.Clear();
            LastSavePoint = 1;
        }

        public void NewWith(IEnumerable<string> filePaths)
        {
            if (filePaths.Count() == 1)
            {
                Load(filePaths.Single());
                return;
            }
            CivicBundle = new CivicBundle();
            FilePath = null;
            AddCivics(filePaths);
            undoStack.Clear();
            redoStack.Clear();
            LastSavePoint = 1;
        }

        private CivicBundle OpenBundle(string filename)
        {
            string text;
            try
            {
                text = File.ReadAllText(filename);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load '{Path.GetFileName(filePath)}' ({ex.Message})!", "Eco Civic Bundler", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
            try
            {
                return CivicBundle.LoadFromText(text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to parse bundle from '{Path.GetFileName(filePath)}' ({ex.Message})!", "Eco Civic Bundler", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        public void Load(string filePath)
        {
            var bundle = OpenBundle(filePath);
            if (bundle == null) { return; }
            CivicBundle = bundle;
            FilePath = filePath;
            undoStack.Clear();
            redoStack.Clear();
            LastSavePoint = 0;
        }

        public void Mutate(CivicBundleMutator mutator)
        {
            if (civicBundle == null) { return; }
            var oldCivicBundle = civicBundle;
            var newCivicBundle = mutator(oldCivicBundle);
            CivicBundle = newCivicBundle;
            undoStack.Push(oldCivicBundle);
            redoStack.Clear();
            ++LastSavePoint;
        }

        public void MutateBundledCivic(CivicReference civicReference, BundledCivicMutator mutator)
        {
            Mutate((oldCivicBundle) =>
            {
                var civics = oldCivicBundle.Civics.Select(c => (BundledCivic)c.Clone()).ToArray();
                for (int i = 0, l = civics.Length; i < l; ++i)
                {
                    ref BundledCivic civic = ref civics[i];
                    if (civic.AsReference == civicReference)
                    {
                        mutator(civic);
                        break;
                    }
                    foreach (var inlineObject in civic.InlineObjects)
                    {
                        if (inlineObject.AsReference == civicReference)
                        {
                            mutator(inlineObject);
                            break;
                        }
                    }
                }
                return new CivicBundle(civics);
            });
        }

        public void RenameCivic(CivicReference civicReference, string newName)
        {
            if (civicBundle == null) { return; }
            BundledCivic? civicToRename = FindCivic(civicReference);
            if (civicToRename == null)
            {
                MessageBox.Show($"Failed to rename civic - not found in this bundle!", "Eco Civic Bundler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            CivicReference renamedCivicReference = new CivicReference(civicReference.Type, newName);
            BundledCivic? existingCivic = FindCivic(renamedCivicReference);
            if (existingCivic != null)
            {
                MessageBox.Show($"Failed to rename civic - another civic by that name already exists in this bundle!", "Eco Civic Bundler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            int renameCnt = 0, fixupCnt = 0;
            Mutate((oldCivicBundle) =>
            {
                var civics = oldCivicBundle.Civics.Select(c => (BundledCivic)c.Clone()).ToArray();
                for (int i = 0, l = civics.Length; i < l; ++i)
                {
                    ref BundledCivic civic = ref civics[i];
                    if (civic.AsReference == civicReference)
                    {
                        civic.Data["name"] = newName;
                        ++renameCnt;
                    }
                    else
                    {
                        civic.VisitInlineObjects((inlineCivicReference, obj) =>
                        {
                            if (inlineCivicReference == civicReference)
                            {
                                obj["name"] = newName;
                                ++renameCnt;
                            }
                        });
                    }
                    civic.VisitReferences((inlineCivicReference, obj) =>
                    {
                        if (inlineCivicReference == civicReference)
                        {
                            obj["name"] = newName;
                            ++fixupCnt;
                        }
                    });
                }
                return new CivicBundle(civics);
            });
            if (renameCnt != 1)
            {
                MessageBox.Show($"Failed to rename civic - renameCnt was {renameCnt}, expecting 1!", "Eco Civic Bundler", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (fixupCnt > 0)
            {
                MessageBox.Show($"Renamed '{civicReference.Name}' to '{newName}' and fixed up {fixupCnt} internal references.", "Eco Civic Bundler", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Renamed '{civicReference.Name}' to '{newName}'.", "Eco Civic Bundler", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public void AddCivics(IEnumerable<string> filePaths)
        {
            Mutate((oldCivicBundle) =>
            {
                var civics = new List<BundledCivic>(oldCivicBundle.Civics.Select(c => (BundledCivic)c.Clone()));
                foreach (string filePath in filePaths)
                {
                    var bundle = OpenBundle(filePath);
                    if (bundle == null) { continue; }
                    foreach (var innerCivic in bundle.Civics)
                    {
                        civics.Add(innerCivic);
                    }
                }
                return new CivicBundle(civics);
            });
        }

        public void RemoveCivic(CivicReference civicReference)
        {
            // TODO: Add support for removing inline objects (e.g. a district from a district map)
            Mutate(civicBundle =>
                new CivicBundle(
                    civicBundle.Civics
                        .Except(civicBundle.Civics.Where(c => c.AsReference == civicReference))
                        .Select(c => (BundledCivic)c.Clone())
                )
            );
        }

        private BundledCivic? FindCivic(CivicReference civicReference)
        {
            foreach (var civic in civicBundle.Civics)
            {
                if (civic.AsReference == civicReference)
                {
                    return civic;
                }
            }
            foreach (var civic in civicBundle.AllInlineObjects)
            {
                if (civic.AsReference == civicReference)
                {
                    return civic;
                }
            }
            return null;
        }

        public bool Undo()
        {
            if (undoStack.Count == 0) { return false; }
            if (civicBundle != null)
            {
                redoStack.Push(civicBundle);
            }
            civicBundle = undoStack.Pop();
            OnCivicBundleChange?.Invoke(this, EventArgs.Empty);
            --LastSavePoint;
            return true;
        }

        public bool Redo()
        {
            if (redoStack.Count == 0) { return false; }
            if (civicBundle != null)
            {
                undoStack.Push(civicBundle);
            }
            civicBundle = redoStack.Pop();
            OnCivicBundleChange?.Invoke(this, EventArgs.Empty);
            ++LastSavePoint;
            return true;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(filePath) || civicBundle == null) { return; }
            System.IO.File.WriteAllText(filePath, civicBundle.SaveToText());
            LastSavePoint = 0;
        }

        public void SaveAs(string filePath)
        {
            FilePath = filePath;
            Save();
        }
    }
}
