using System;
using System.Collections.Generic;
using System.Linq;

namespace EcoCivicsImportExportMod.Bundler
{
    using Model;

    public delegate void CivicBundleMutator(CivicBundle civicBundle);

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

        public void Load(string filePath)
        {
            CivicBundle = CivicBundle.LoadFromText(System.IO.File.ReadAllText(filePath));
            FilePath = filePath;
            undoStack.Clear();
            redoStack.Clear();
            LastSavePoint = 0;
        }

        public void Mutate(CivicBundleMutator mutator)
        {
            if (civicBundle == null) { return; }
            var oldCivicBundle = civicBundle;
            var newCivicBundle = oldCivicBundle.Clone() as CivicBundle;
            mutator(newCivicBundle);
            CivicBundle = civicBundle;
            undoStack.Push(oldCivicBundle);
            redoStack.Clear();
            ++LastSavePoint;
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
            // TODO: This
            LastSavePoint = 0;
        }

        public void SaveAs(string filePath)
        {
            FilePath = filePath;
            Save();
        }
    }
}
