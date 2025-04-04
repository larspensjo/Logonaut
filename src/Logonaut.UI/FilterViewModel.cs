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

        public bool IsEditable => FilterModel.IsEditable;

        // Callback to notify the owner (MainViewModel) that the filter configuration changed
        private readonly Action? _filterConfigurationChangedCallback;

        // Modified constructor to accept the callback
        public FilterViewModel(IFilter filter, Action? filterConfigurationChangedCallback = null, FilterViewModel? parent = null)
        {
            FilterModel = filter;
            Parent = parent;
            _filterConfigurationChangedCallback = filterConfigurationChangedCallback;

            if (filter is CompositeFilter composite)
            {
                foreach (var child in composite.SubFilters)
                {
                    // Propagate the callback down to children
                    Children.Add(new FilterViewModel(child, filterConfigurationChangedCallback, this));
                }
            }
        }

        [RelayCommand]
        public void AddChildFilter(IFilter childFilter)
        {
            if (FilterModel is CompositeFilter composite)
            {
                composite.Add(childFilter);
                // Pass the callback when creating child ViewModel
                Children.Add(new FilterViewModel(childFilter, _filterConfigurationChangedCallback, this));
                NotifyFilterConfigurationChanged(); // Notify that structure changed
            }
        }

        public void RemoveChild(FilterViewModel child)
        {
            if (FilterModel is CompositeFilter composite)
            {
                composite.Remove(child.FilterModel);
                Children.Remove(child);
                NotifyFilterConfigurationChanged(); // Notify that structure changed
            }
        }

        public bool Enabled
        {
            get => FilterModel.Enabled;
            set
            {
                if (FilterModel.Enabled != value)
                {
                    FilterModel.Enabled = value;
                    OnPropertyChanged();
                    NotifyFilterConfigurationChanged(); // <<<<< ADDED
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
                // Only trigger change if the value actually changed.
                // Also handles cases where Value getter/setter might throw if not supported.
                try
                {
                    if (FilterModel.Value != value)
                    {
                        FilterModel.Value = value;
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(DisplayText)); // DisplayText depends on Value
                        // No need to notify config changed on every keystroke here,
                        // only when editing finishes (EndEdit) or Enabled changes.
                    }
                }
                catch (NotSupportedException)
                {
                    // Ignore attempts to set Value on filters that don't support it.
                }
            }
        }

        // Method to invoke the callback
        private void NotifyFilterConfigurationChanged()
        {
            _filterConfigurationChangedCallback?.Invoke();
        }

        [RelayCommand]
        public void BeginEdit()
        {
            if (IsEditable)
            {
                IsEditing = true;
                IsNotEditing = false;
            }
        }

        [RelayCommand]
        public void EndEdit()
        {
            IsEditing = false;
            IsNotEditing = true;
            // Notify AFTER editing is finished
            NotifyFilterConfigurationChanged();
        }
    }
}