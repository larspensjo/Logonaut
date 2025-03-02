using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Filters;

namespace Logonaut.UI.ViewModels
{
    public partial class FilterViewModel : ObservableObject
    {
        // The underlying filter model from Logonaut.Filters.
        public IFilter FilterModel { get; }

        // A collection of child view models, used if this filter is composite.
        public ObservableCollection<FilterViewModel> Children { get; } = new();

        [ObservableProperty]
        private bool isSelected;

        public FilterViewModel(IFilter filter)
        {
            FilterModel = filter;
            // If the filter is composite, wrap its children.
            if (filter is CompositeFilterBase composite)
            {
                foreach (var child in composite.SubFilters)
                {
                    Children.Add(new FilterViewModel(child));
                }
            }
        }

        // Command to add a child filter (only applicable if this filter is composite).
        [RelayCommand]
        public void AddChildFilter(IFilter childFilter)
        {
            if (FilterModel is CompositeFilterBase composite)
            {
                composite.Add(childFilter);
                Children.Add(new FilterViewModel(childFilter));
            }
        }

        // A read-only property for display purposes.
        public string DisplayText => FilterModel switch
        {
            SubstringFilter s => $"Substring: {s.Substring}",
            AndFilter _ => "AND",
            OrFilter _ => "OR",
            NegationFilter _ => "NOT",
            _ => "Filter"
        };

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
    }
}
