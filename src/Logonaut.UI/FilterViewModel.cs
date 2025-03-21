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
        // TODO: Ask the class itself
        public string FilterType => FilterModel switch
        {
            SubstringFilter _ => "Substring",
            RegexFilter _ => "Regex",
            AndFilter _ => "AND",
            OrFilter _ => "OR",
            NegationFilter _ => "NOT",
            _ => "Unknown"
        };

        // A property that gets/sets the substring when the FilterModel is a SubstringFilter.
        // See also DisplayText, used when displaying the filter.
        // TODO: this is unused, can it be removed?
        public string FilterText
        {
            get => FilterModel switch
            {
                SubstringFilter s => s.Substring,
                RegexFilter r => r.Pattern,
                _ => string.Empty // TODO: This is shown when clicking on a composite filter. Should be fixed.
            };
            set
            {
                if (FilterModel is SubstringFilter s && s.Substring != value)
                {
                    s.Substring = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayText));
                    NotifyFilterTextChanged();
                }
                else if (FilterModel is RegexFilter r && r.Pattern != value)
                {
                    r.Pattern = value;
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
