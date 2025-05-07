
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.UI.Commands;
using System.Reactive.Linq;
using System.ComponentModel;
using System.Diagnostics;

// TODO: Should we move all filter management here?

namespace Logonaut.UI.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{
    private FilterProfileViewModel? _observedActiveProfile;
    private IDisposable? _activeProfileNameSubscription;

    // Collection of all available filter profiles (VMs) for the ComboBox.
    public ObservableCollection<FilterProfileViewModel> AvailableProfiles { get; } = new();

    // The currently selected filter profile VM in the ComboBox.
    // Setting this property triggers updates to the TreeView and filtering.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFilterCommand))] // Enable adding nodes if profile selected
    [NotifyCanExecuteChangedFor(nameof(RemoveFilterNodeCommand))] // Depends on node selection *within* active tree
    [NotifyCanExecuteChangedFor(nameof(ToggleEditNodeCommand))] // Depends on node selection *within* active tree
    private FilterProfileViewModel? _activeFilterProfile;

    private bool CanManageActiveProfile() => ActiveFilterProfile != null;

    // // Rebuild the AvailableProfiles collection from the loaded filter profile models
    private void LoadFilterProfiles(LogonautSettings settings) {
        AvailableProfiles.Clear();
        FilterProfileViewModel? profileToSelect = null;
        foreach (var profileModel in settings.FilterProfiles)
        {
            // Pass 'this' as the ICommandExecutor
            var profileVM = new FilterProfileViewModel(profileModel, this);
            AvailableProfiles.Add(profileVM);
            if (profileModel.Name == settings.LastActiveProfileName)
            {
                profileToSelect = profileVM;
            }
        }

        // Set the active profile. Should always find one due to validation in SettingsManager.
        // This assignment will trigger OnActiveFilterProfileChanged.
        ActiveFilterProfile = profileToSelect ?? AvailableProfiles.FirstOrDefault(); // Fallback just in case
    }

    private void SaveFilterProfiles(LogonautSettings settings)
    {
        if (ActiveFilterProfile is null)
            throw new InvalidOperationException("No active filter profile to save.");

        // Extract the models from the ViewModels
        settings.LastActiveProfileName = ActiveFilterProfile?.Name ?? AvailableProfiles.FirstOrDefault()?.Name ?? "Default";

        // Add detailed logging within the Select statement
        settings.FilterProfiles = AvailableProfiles.Select(vm => {
            if (vm == null) {
                Debug.WriteLine($"---> SaveFilterProfiles: Encountered NULL FilterProfileViewModel!");
                return null; // Should not happen, but guard
            }
            var model = vm.Model; // Get the model reference
            if (model == null) {
                Debug.WriteLine($"---> SaveFilterProfiles: Profile VM '{vm.Name}' has NULL Model!");
                return null; // Should not happen
            }
            var rootFilter = model.RootFilter; // Get the root filter reference
            string rootTypeName = rootFilter?.GetType().Name ?? "null";
            int subFilterCount = -1;
            if (rootFilter is Logonaut.Filters.CompositeFilter cf) {
                subFilterCount = cf.SubFilters.Count; // Get count directly from the model's property
            }
            Debug.WriteLine($"---> SaveFilterProfiles: Processing Profile '{vm.Name}'. RootFilter Type: {rootTypeName}, SubFilter Count: {subFilterCount}");

            return model; // Return the model
        }).Where(m => m != null).ToList()!; // Filter out potential nulls and use null-forgiving operator

        Debug.WriteLine($"---> SaveFilterProfiles: Finished processing profiles for saving.");
    }

    [RelayCommand] private void CreateNewProfile()
    {
        int counter = 1;
        string baseName = "New Profile ";
        string newName = baseName + counter;
        // Ensure generated name is unique
        while (AvailableProfiles.Any(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            counter++;
            newName = baseName + counter;
        }

        // Create the underlying model and its ViewModel wrapper
        var newProfileModel = new FilterProfile(newName, null); // Start with a simple root
        var newProfileVM = new FilterProfileViewModel(newProfileModel, this);

        AvailableProfiles.Add(newProfileVM);
        ActiveFilterProfile = newProfileVM; // Select the new profile (this triggers update via OnChanged)

        // Immediately trigger the rename mode for the new profile VM
        // Use Dispatcher.InvokeAsync to ensure UI updates (like setting ActiveFilterProfile)
        // have likely processed before trying to execute the command that relies on it being active.
        // Using BeginInvoke with Background priority can also work well here.
        _uiContext.Post(_ => { // Use the SynchronizationContext instead
            if (ActiveFilterProfile == newProfileVM) // Double-check it's still the active one
            {
                newProfileVM.BeginRenameCommand.Execute(null);
                // Focus should be handled automatically by TextBoxHelper.FocusOnVisible
            }
        }, null); // Pass null for the state object

        SaveCurrentSettings(); // Save changes immediately
    }

    [RelayCommand(CanExecute = nameof(CanManageActiveProfile))]
    private void DeleteProfile()
    {
        if (ActiveFilterProfile == null) return;

        // Keep the profile to remove and its index for selection logic later
        var profileToRemove = ActiveFilterProfile;
        int removedIndex = AvailableProfiles.IndexOf(profileToRemove);

        // --- Logic for Deletion ---
        AvailableProfiles.Remove(profileToRemove); // Remove the selected profile

        // --- Handle Last Profile Scenario ---
        if (AvailableProfiles.Count == 0)
        {
            // If the list is now empty, create and add a new default profile
            var defaultModel = new FilterProfile("Default", null); // Use default name and no filter
            var defaultVM = new FilterProfileViewModel(defaultModel, this);
            AvailableProfiles.Add(defaultVM);

            // Set the new default profile as the active one
            ActiveFilterProfile = defaultVM;
            // Setting ActiveFilterProfile triggers OnActiveFilterProfileChanged -> UpdateActiveTreeRootNodes & TriggerFilterUpdate
        }
        else
        {
            // Select another profile (e.g., the previous one or the first one)
            ActiveFilterProfile = AvailableProfiles.ElementAtOrDefault(Math.Max(0, removedIndex - 1)) ?? AvailableProfiles.First();
            // Setting ActiveFilterProfile triggers OnActiveFilterProfileChanged -> UpdateActiveTreeRootNodes & TriggerFilterUpdate
        }

        SaveCurrentSettings(); // Save the updated profile list and active profile
    }

    // This method is automatically called by the CommunityToolkit.Mvvm generator
    // when the ActiveFilterProfile property's value changes.
    partial void OnActiveFilterProfileChanged(FilterProfileViewModel? oldValue, FilterProfileViewModel? newValue)
    {
        // --- Unsubscribe from the old profile's name changes ---
        _activeProfileNameSubscription?.Dispose();
        _observedActiveProfile = null;

        if (newValue != null)
        {
            _observedActiveProfile = newValue;
            // --- Subscribe to the new profile's name changes ---
            // Explicitly specify the Delegate type AND EventArgs type
            _activeProfileNameSubscription = Observable.FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                    handler => newValue.PropertyChanged += handler, // handler is now correctly typed
                    handler => newValue.PropertyChanged -= handler)
                .Where(pattern => pattern.EventArgs.PropertyName == nameof(FilterProfileViewModel.Name)) // Check EventArgs property name
                // Optional: Add debounce/throttle if changes trigger too rapidly
                // .Throttle(TimeSpan.FromMilliseconds(200), _uiScheduler)
                .ObserveOn(_uiContext) // Ensure handler runs on UI thread
                .Subscribe(pattern => HandleActiveProfileNameChange(pattern.Sender as FilterProfileViewModel)); // pattern.Sender is the source

            // Keep existing logic
            UpdateActiveTreeRootNodes(newValue);
            SelectedFilterNode = null;
            TriggerFilterUpdate();
            // SaveCurrentSettings(); // Save triggered by name change or initial selection
        }
        else // No active profile
        {
            UpdateActiveTreeRootNodes(null);
            SelectedFilterNode = null;
            TriggerFilterUpdate(); // Trigger with default filter
        }
        // Save immediately on selection change as well
        SaveCurrentSettings();
    }
    private void HandleActiveProfileNameChange(FilterProfileViewModel? profileVM)
    {
        if (profileVM == null || profileVM != ActiveFilterProfile)
        {
            // Stale event or profile changed again quickly, ignore.
            return;
        }

        string newName = profileVM.Name;
        string modelName = profileVM.Model.Name; // Get the last known committed name

        // --- Validation ---
        // Check for empty/whitespace
        if (string.IsNullOrWhiteSpace(newName))
        {
            // MessageBox.Show("Profile name cannot be empty.", "Rename Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            profileVM.Name = modelName; // Revert VM property immediately
            // No save needed here, as the invalid name wasn't saved.
            return;
        }

        // Check for duplicates (case-insensitive, excluding self)
        if (AvailableProfiles.Any(p => p != profileVM && p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            // MessageBox.Show($"A profile named '{newName}' already exists.", "Rename Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            profileVM.Name = modelName; // Revert VM property immediately
            // No save needed here.
            return;
        }

        // --- Validation Passed ---
        // Ensure the model is updated (might be redundant if binding worked, but safe)
        if (profileVM.Model.Name != newName)
        {
            profileVM.Model.Name = newName;
        }
        SaveCurrentSettings(); // Save the valid new name
    }
}
