using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Filters;

namespace Logonaut.UI.ViewModels
{
    // There will be one instance of this for every filter.
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
        private bool isEditing = true;

        public FilterViewModel(IFilter filter, FilterViewModel? parent = null)
        {
            FilterModel = filter;
            Parent = parent;
            if (filter is CompositeFilterBase composite)
            {
                foreach (var child in composite.SubFilters)
                {
                    Children.Add(new FilterViewModel(child, this));
                }
            }
        }

        // Command to add a child filter.
        [RelayCommand]
        public void AddChildFilter(IFilter childFilter)
        {
            if (FilterModel is CompositeFilterBase composite)
            {
                composite.Add(childFilter);
                Children.Add(new FilterViewModel(childFilter, this));
            }
        }

        // Removes a child filter from the composite.
        public void RemoveChild(FilterViewModel child)
        {
            if (FilterModel is CompositeFilterBase composite)
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
        public string DisplayText => FilterModel switch
        {
            SubstringFilter s => $"Substring: {s.Substring}",
            AndFilter _ => "AND",
            OrFilter _ => "OR",
            NegationFilter _ => "NOT",
            _ => "Filter"
        };

        // A property that gets/sets the substring when the FilterModel is a SubstringFilter.
        // See also DisplayText, used when displaying the filter.
        public string FilterText
        {
            get => FilterModel is SubstringFilter s ? s.Substring : string.Empty;
            set
            {
                if (FilterModel is SubstringFilter s && s.Substring != value)
                {
                    s.Substring = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        // Command to begin inline editing.
        [RelayCommand]
        private void BeginEdit()
        {
            IsEditing = true;
        }

        // Command to end inline editing.
        [RelayCommand]
        private void EndEdit()
        {
            IsEditing = false;
        }
    }
}
