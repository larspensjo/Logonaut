using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Filters;

namespace Logonaut.UI.ViewModels
{
    public partial class FilterViewModel : ObservableObject
    {
        public IFilter FilterModel { get; }
        public FilterViewModel? Parent { get; }
        public ObservableCollection<FilterViewModel> Children { get; } = new();

        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private bool _isEditing = false;
        [ObservableProperty] private bool _isNotEditing = true;

        public bool IsEditable => FilterModel.IsEditable;

        // Callback to notify owner (MainViewModel) that filter config requires re-evaluation
        private readonly Action? _filterConfigurationChangedCallback; // <<< RENAME? More like "TriggerRefilterCallback"

        public FilterViewModel(IFilter filter, Action? filterConfigurationChangedCallback = null, FilterViewModel? parent = null)
        {
            FilterModel = filter;
            Parent = parent;
            _filterConfigurationChangedCallback = filterConfigurationChangedCallback;

            if (filter is CompositeFilter composite)
            {
                foreach (var child in composite.SubFilters)
                {
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
                // Ensure the callback is passed down
                var childVM = new FilterViewModel(childFilter, _filterConfigurationChangedCallback, this);
                Children.Add(childVM);
                NotifyFilterConfigurationChanged(); // Notify after structural change
            }
        }

        public void RemoveChild(FilterViewModel child)
        {
            if (FilterModel is CompositeFilter composite)
            {
                if (composite.Remove(child.FilterModel))
                {
                    Children.Remove(child);
                    NotifyFilterConfigurationChanged(); // Notify after structural change
                }
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
                    NotifyFilterConfigurationChanged(); // <<< Notify on Enabled change
                }
            }
        }

        public string DisplayText => FilterModel.DisplayText;
        public string FilterType => FilterModel.TypeText;

        public string FilterText
        {
            get => FilterModel.Value;
            set
            {
                try
                {
                    if (FilterModel.Value != value)
                    {
                        FilterModel.Value = value;
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(DisplayText)); // DisplayText depends on Value for some types
                        // DO NOT notify on every keystroke. Notification happens on EndEdit or Enabled change.
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
            if (IsEditing) // Only trigger if we were actually editing
            {
                IsEditing = false;
                IsNotEditing = true;
                // Notify AFTER editing is finished and state is updated
                NotifyFilterConfigurationChanged(); // <<< Notify on finishing edit
            }
        }
    }
}