using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace EcoCivicsImportExportMod.Bundler
{
    public delegate TViewModel CreateViewModelFromModel<TModel, TViewModel>(in TModel model);

    public delegate void UpdateViewModelFromModel<TModel, TViewModel>(TViewModel viewModel, in TModel model);

    public static class ObservableCollectionExt
    {
        public static void SetFromEnumerable<TModel, TViewModel>(
            this ObservableCollection<TViewModel> observableCollection, IEnumerable<TModel> items,
            CreateViewModelFromModel<TModel, TViewModel> createViewModelFromModel,
            UpdateViewModelFromModel<TModel, TViewModel> updateViewModelFromModel
        )
            where TViewModel : INotifyPropertyChanged
        {
            int ptr = 0;
            foreach (var item in items)
            {
                if (ptr >= observableCollection.Count)
                {
                    observableCollection.Add(createViewModelFromModel(item));
                }
                else
                {
                    updateViewModelFromModel(observableCollection[ptr], item);
                }
                ++ptr;
            }
            while (observableCollection.Count > ptr)
            {
                observableCollection.RemoveAt(ptr);
            }
        }
    }
}
