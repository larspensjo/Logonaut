using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Filters;

namespace Logonaut.UI.ViewModels
{
    // There will be one instance of this for every filter.
    // TODO: Remove all hard coded dependency of filter type. Instead, ask the filter itself.
    public partial class FilterViewModel : ObservableObject
    {
        // The underlying filter model from Logonaut.Filters.
        public IFilter FilterModel { get; }
        
        // Optional parent reference for non-root filters.
        public FilterViewModel? Parent { get; }
        
        // Child view models for composite filters.
        public ObservableCollection<FilterViewModel> Children { get; } = new();

        [ObservableProperty]
        private bool isSelected;

        [ObservableProperty]
        private bool isEditing = false;

        [ObservableProperty]
        private bool isNotEditing = true;

        // Indicates whether this filter is editable.
        public bool IsEditable => FilterModel.IsEditable;

        public FilterViewModel(IFilter filter, FilterViewModel? parent = null)
        {
            FilterModel = filter;
            Parent = parent;
            if (filter is CompositeFilter composite)
            {
                foreach (var child in composite.SubFilters)
                {
                    Children.Add(new FilterViewModel(child, this));
                }
            }

#if false
            // TODO: We want to start edit mode immediately, but some improved visual and focus management is needed.
            if (filter.IsEditable)
            {
                BeginEdit();
            }
#endif
        }

        // Command to add a child filter.
        [RelayCommand]
        public void AddChildFilter(IFilter childFilter)
        {
            if (FilterModel is CompositeFilter composite)
            {
                composite.Add(childFilter);
                Children.Add(new FilterViewModel(childFilter, this));
            }
        }

        // Removes a child filter from the composite.
        public void RemoveChild(FilterViewModel child)
        {
            if (FilterModel is CompositeFilter composite)
            {
                composite.Remove(child.FilterModel);
                Children.Remove(child);
            }
        }

        // Property to control whether the filter is enabled.
        public bool Enabled
        {
            get => FilterModel.Enabled;
            set
            {
                if (FilterModel.Enabled != value)
                {
                    FilterModel.Enabled = value;
                    OnPropertyChanged();
                }
            }
        }

        // Read-only display text. See also FilterText, used when editing.
        public string DisplayText => FilterModel.DisplayText;

        // This is used by FilterTemplates.xaml.
        public string FilterType => FilterModel.TypeText;

        // A property that sets the substring when the FilterModel shows a TextBox.
        // See also DisplayText, used when displaying the filter.
        // VS Code shows reference as 0, but it is used by FilterTemplates.xaml.
        public string FilterText
        {
            get => FilterModel.Value;
            set
            {
                if (FilterModel.Value != value)
                {
                    FilterModel.Value = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayText));
                    NotifyFilterTextChanged();
                }
            }
        }

        // Method to propagate filter text changes up to MainViewModel
        private void NotifyFilterTextChanged()
        {
            // Find the root MainViewModel
            if (App.Current.MainWindow?.DataContext is MainViewModel mainViewModel)
            {
                // Update filter substrings for highlighting
                // Use the command property instead of the method
                mainViewModel.UpdateFilterSubstringsCommand.Execute(null);
            }
        }

        // Command to begin inline editing.
        [RelayCommand]
        public void BeginEdit()
        {
            if (IsEditable)
            {
                IsEditing = true;
                IsNotEditing = false;
            }
        }

        // Command to end inline editing.
        [RelayCommand]
        public void EndEdit()
        {
            IsEditing = false;
            IsNotEditing = true;
    
            // Notify that filter text has changed to update highlighting
            NotifyFilterTextChanged();
        }
    }
}
