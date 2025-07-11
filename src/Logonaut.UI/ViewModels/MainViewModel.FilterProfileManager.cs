using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Core.Commands;
using System.Reactive.Linq;
using System.ComponentModel;
using System.Diagnostics;

namespace Logonaut.UI.ViewModels;

/*
 * Partial class for MainViewModel responsible for managing filter profiles.
 * This includes loading, saving, creating, deleting, and selecting filter profiles.
 * It also handles interactions related to the active filter profile's rule tree.
 */
public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{
    private FilterProfileViewModel? _observedActiveProfile;
    private IDisposable? _activeProfileNameSubscription;

    public ObservableCollection<FilterProfileViewModel> AvailableProfiles { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFilterCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveFilterNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleEditNodeCommand))]
    private FilterProfileViewModel? _activeFilterProfile;

    private bool CanManageActiveProfile() => ActiveFilterProfile != null;

    private void LoadFilterProfiles(LogonautSettings settings)
    {
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
        // This remains largely the same as it saves the global list of profiles
        if (ActiveFilterProfile == null && AvailableProfiles.Any()) // Ensure there's a fallback if Active is somehow null
        {
            settings.LastActiveProfileName = AvailableProfiles.First().Name;
        }
        else if (ActiveFilterProfile != null)
        {
            settings.LastActiveProfileName = ActiveFilterProfile.Name;
        }
        else // No profiles at all
        {
            settings.LastActiveProfileName = "Default"; // Or handle as error
        }


        settings.FilterProfiles = AvailableProfiles.Select(vm =>
        {
            if (vm == null) { Debug.WriteLine($"---> SaveFilterProfiles: Encountered NULL FilterProfileViewModel!"); return null; }
            var model = vm.Model;
            if (model == null) { Debug.WriteLine($"---> SaveFilterProfiles: Profile VM '{vm.Name}' has NULL Model!"); return null; }
            return model;
        }).Where(m => m != null).ToList()!;
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
        var newProfileModel = new FilterProfile(newName, null);
        var newProfileVM = new FilterProfileViewModel(newProfileModel, this);
        AvailableProfiles.Add(newProfileVM);
        ActiveFilterProfile = newProfileVM;

        _uiContext.Post(_ =>
        {
            if (ActiveFilterProfile == newProfileVM)
                newProfileVM.BeginRenameCommand.Execute(null);
        }, null);
        MarkSettingsAsDirty();
    }

    [RelayCommand(CanExecute = nameof(CanManageActiveProfile))]
    private void DeleteProfile()
    {
        if (ActiveFilterProfile == null) return;
        var profileToRemove = ActiveFilterProfile;
        int removedIndex = AvailableProfiles.IndexOf(profileToRemove);
        AvailableProfiles.Remove(profileToRemove);
        if (AvailableProfiles.Count == 0)
        {
            var defaultModel = new FilterProfile("Default", null);
            var defaultVM = new FilterProfileViewModel(defaultModel, this);
            AvailableProfiles.Add(defaultVM);
            ActiveFilterProfile = defaultVM;
        }
        else
        {
            ActiveFilterProfile = AvailableProfiles.ElementAtOrDefault(Math.Max(0, removedIndex - 1)) ?? AvailableProfiles.First();
        }
        MarkSettingsAsDirty();
    }

    partial void OnActiveFilterProfileChanged(FilterProfileViewModel? oldValue, FilterProfileViewModel? newValue)
    {
        _activeProfileNameSubscription?.Dispose();
        _observedActiveProfile = null;

        // Clear active matching status for the old profile's filter tree
        if (oldValue?.RootFilterViewModel != null)
        {
            ClearActiveFilterMatchingStatusRecursive(oldValue.RootFilterViewModel);
        }

        if (newValue != null)
        {
            _observedActiveProfile = newValue;
            // --- Subscribe to the new profile's name changes ---
            // Explicitly specify the Delegate type AND EventArgs type
            _activeProfileNameSubscription = Observable.FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                    handler => newValue.PropertyChanged += handler,
                    handler => newValue.PropertyChanged -= handler)
                .Where(pattern => pattern.EventArgs.PropertyName == nameof(FilterProfileViewModel.Name))
                .ObserveOn(_uiContext)
                .Subscribe(pattern => HandleActiveProfileNameChange(pattern.Sender as FilterProfileViewModel));

            UpdateActiveTreeRootNodes(newValue); // Updates the TreeView for editing this profile
            SelectedFilterNode = null;
            
            // Apply the change to the currently active tab
            if (ActiveTabViewModel != null)
            {
                ActiveTabViewModel.AssociatedFilterProfileName = newValue.Name;
                ActiveTabViewModel.ApplyFiltersFromProfile(this.AvailableProfiles, this.ContextLines);
                // Update active filter matching status based on the active tab's current filtered lines
                UpdateActiveFilterMatchingStatus();
            }
        }
        else
        {
            UpdateActiveTreeRootNodes(null);
            SelectedFilterNode = null;
            if (ActiveTabViewModel != null)
            {
                ActiveTabViewModel.AssociatedFilterProfileName = "Default"; // Or some sensible default
                ActiveTabViewModel.ApplyFiltersFromProfile(this.AvailableProfiles, this.ContextLines);
                UpdateActiveFilterMatchingStatus();
            }
        }
        MarkSettingsAsDirty();
    }

    private void HandleActiveProfileNameChange(FilterProfileViewModel? profileVM)
    {
        if (profileVM == null || profileVM != ActiveFilterProfile)
            return;
        string newName = profileVM.Name;
        string modelName = profileVM.Model.Name;
        if (string.IsNullOrWhiteSpace(newName))
        {
            profileVM.Name = modelName;
            return;
        }
        if (AvailableProfiles.Any(p => p != profileVM && p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
        {
            profileVM.Name = modelName;
            return;
        }
        if (profileVM.Model.Name != newName) profileVM.Model.Name = newName;

        // If the active profile's name changed, update the active tab's association
        if (ActiveTabViewModel != null && ActiveTabViewModel.AssociatedFilterProfileName != newName)
        {
            ActiveTabViewModel.AssociatedFilterProfileName = newName;
            // The tab doesn't need to re-filter immediately unless this was the *only* change.
            // TriggerFilterUpdate (which calls ApplyFiltersFromProfile on the tab) is usually
            // called after filter *content* changes. For just a name change, saving is enough.
        }
        MarkSettingsAsDirty();
    }
}
