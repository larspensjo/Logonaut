using CommunityToolkit.Mvvm.ComponentModel;
using Logonaut.Common;
using Logonaut.Filters;
using System; // For Action

namespace Logonaut.UI.ViewModels
{
    /// <summary>
    /// ViewModel wrapper for a FilterProfile, used for display in lists (like ComboBox)
    /// and potentially holding the root FilterViewModel for the tree.
    /// </summary>
    public partial class FilterProfileViewModel : ObservableObject
    {
        public FilterProfile Model { get; }

        // The root ViewModel for the filter tree displayed when this profile is active.
        // Lazily created or managed by MainViewModel.
        [ObservableProperty]
        private FilterViewModel? _rootFilterViewModel;

        // Expose Name for binding (e.g., ComboBox DisplayMemberPath)
        [ObservableProperty]
        private string _name;

        private readonly Action? _filterConfigurationChangedCallback;

        public FilterProfileViewModel(FilterProfile model, Action? filterConfigurationChangedCallback)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            _name = Model.Name ?? "Unnamed Profile"; // Ensure name is not null for display
            _filterConfigurationChangedCallback = filterConfigurationChangedCallback;

            // Create the root FilterViewModel for this profile's tree
            if (Model.RootFilter != null)
            {
                _rootFilterViewModel = new FilterViewModel(Model.RootFilter, _filterConfigurationChangedCallback);
            }
            // Important: Don't create children here recursively if FilterViewModel constructor does it.
            // Ensure FilterViewModel handles building its own children based on the passed IFilter.
        }

        // Update the underlying model's name when the VM's Name changes
        partial void OnNameChanged(string value)
        {
            if (Model.Name != value)
            {
                Model.Name = value;
                // Potentially trigger a save or notify MainViewModel if needed immediately
            }
        }

        // Method to rebuild the RootFilterViewModel if the underlying model changes externally
        // (e.g., after deserialization or if model is modified directly - though modifying via VM is preferred)
        public void RefreshRootViewModel()
        {
            if (Model.RootFilter != null)
            {
                 // Pass the *same* callback down
                RootFilterViewModel = new FilterViewModel(Model.RootFilter, _filterConfigurationChangedCallback);
            }
            else
            {
                RootFilterViewModel = null;
            }
        }

        // Helper to update the model's root filter (e.g., when adding the first filter node)
        public void SetModelRootFilter(IFilter? filter)
        {
            Model.RootFilter = filter;
            RefreshRootViewModel(); // Rebuild the VM tree
            _filterConfigurationChangedCallback?.Invoke(); // Notify that structure changed
        }
    }
}