# Logonaut: Filter Profile Management Flow

This document outlines the user interaction and data flow for managing named filter profiles within the Logonaut application, using the ComboBox-based UI approach.

## Key Components

*   **`FilterProfile` (`Logonaut.Common`):** Model class holding the `Name` and root `IFilter` for a single profile. ([`FilterProfile.cs`](../src/Logonaut.Common/FilterProfile.cs))
*   **`LogonautSettings` (`Logonaut.Common`):** Holds the `List<FilterProfile>` and the `LastActiveProfileName`. ([`LogonautSettings.cs`](../src/Logonaut.Common/LogonautSettings.cs))
*   **`SettingsManager` (`Logonaut.Core`):** Handles loading and saving `LogonautSettings` to/from JSON. ([`SettingsManager.cs`](../src/Logonaut.Core/SettingsManager.cs))
*   **`FilterProfileViewModel` (`Logonaut.UI.ViewModels`):** ViewModel wrapper for `FilterProfile`, used in collections for UI binding. Holds the root `FilterViewModel` for the tree. ([`FilterProfileViewModel.cs`](../src/Logonaut.UI/ViewModels/FilterProfileViewModel.cs))
*   **`FilterViewModel` (`Logonaut.UI.ViewModels`):** Represents a single node within a filter tree. ([`FilterViewModel.cs`](../src/Logonaut.UI/ViewModels/FilterViewModel.cs))
*   **`MainViewModel` (`Logonaut.UI.ViewModels`):** The main orchestrator. Holds collections of available profiles, the active profile, the selected node within the active tree, and commands for management. ([`MainViewModel.cs`](../src/Logonaut.UI/ViewModels/MainViewModel.cs))
*   **`MainWindow.xaml` (`Logonaut.UI`):** Contains the UI elements:
    *   `ComboBox` for profile selection.
    *   `Button`s for profile management (New, Rename, Delete).
    *   `TreeView` to display the active profile's filter tree.
    *   `Button`s for node management within the active tree. ([`MainWindow.xaml`](../src/Logonaut.UI/MainWindow.xaml))
*   **`IInputPromptService` (`Logonaut.UI.ViewModels`):** Interface (and simple implementation) for getting user input (e.g., for renaming). ([`MainViewModel.cs`](../src/Logonaut.UI/ViewModels/MainViewModel.cs))

## Interaction and Data Flow

### 1. Application Startup (Loading)

1.  **Trigger:** Application starts.
2.  **`MainViewModel` Constructor:** Instantiates dependencies.
3.  **`LoadPersistedSettings()`:**
    *   Calls `SettingsManager.LoadSettings()` to deserialize `LogonautSettings` from JSON. `SettingsManager` ensures at least one default profile exists if the file is missing or invalid.
    *   Creates `FilterProfileViewModel` instances for each loaded `FilterProfile` model.
    *   Populates `MainViewModel.AvailableProfiles` collection.
    *   Identifies the profile matching `LogonautSettings.LastActiveProfileName`.
    *   Sets `MainViewModel.ActiveFilterProfile` to the identified (or default) `FilterProfileViewModel`.
4.  **`OnActiveFilterProfileChanged()`:** The setter for `ActiveFilterProfile` triggers this partial method.
5.  **`UpdateActiveTreeRootNodes()`:** Called by `OnActiveFilterProfileChanged`. Clears `MainViewModel.ActiveTreeRootNodes` and adds the `RootFilterViewModel` from the *newly set* `ActiveFilterProfile`.
6.  **`TriggerFilterUpdate()`:** Called by `OnActiveFilterProfileChanged`. This signals the `LogFilterProcessor` to perform a full re-filter using the `RootFilter` from the `ActiveFilterProfile.Model`. See [ReactiveIncrementalFiltering.md](ReactiveIncrementalFiltering.md).
7.  **UI Update:** The `ComboBox` (`ItemsSource="{Binding AvailableProfiles}", SelectedItem="{Binding ActiveFilterProfile}"`) reflects the loaded profiles and selects the active one. The `TreeView` (`ItemsSource="{Binding ActiveTreeRootNodes}"`) displays the tree for the active profile.

### 2. Selecting a Different Profile

1.  **Trigger:** User selects a different profile name from the `ComboBox`.
2.  **Binding:** WPF updates the `MainViewModel.ActiveFilterProfile` property via the `SelectedItem` binding.
3.  **`OnActiveFilterProfileChanged()`:** This partial method is executed because the property value changed.
4.  **`UpdateActiveTreeRootNodes()`:** Updates the `ActiveTreeRootNodes` collection to contain the root `FilterViewModel` of the *newly selected* profile.
5.  **`TriggerFilterUpdate()`:** Signals the `LogFilterProcessor` to re-filter using the new active profile's filter tree.
6.  **`SaveCurrentSettings()`:** Called by `OnActiveFilterProfileChanged` to persist the newly selected profile as the last active one.
7.  **UI Update:** The `TreeView` updates its display based on the change to `ActiveTreeRootNodes`. The log view updates based on the results from the `LogFilterProcessor`.

### 3. Creating a New Profile

1.  **Trigger:** User clicks the "New Profile" `Button`.
2.  **`CreateNewProfileCommand`:** Executes in `MainViewModel`.
    *   Generates a unique default name (e.g., "New Profile X").
    *   Creates a new `FilterProfile` model (with a default `TrueFilter` or null root).
    *   Creates a `FilterProfileViewModel` wrapper for the new model.
    *   Adds the new `FilterProfileViewModel` to the `AvailableProfiles` collection.
    *   Sets `MainViewModel.ActiveFilterProfile` to the newly created `FilterProfileViewModel`.
    *   Calls `SaveCurrentSettings()`.
3.  **`OnActiveFilterProfileChanged()`:** Is triggered by setting `ActiveFilterProfile`.
4.  **(Flow continues as in step 2, points 4-7):** `UpdateActiveTreeRootNodes` updates the `TreeView` (it will be empty or show the default root), `TriggerFilterUpdate` applies the new (likely empty) filter.
5.  **UI Update:** The `ComboBox` shows the new profile selected. The `TreeView` clears or shows the default root.

### 4. Renaming the Active Profile

1.  **Trigger:** User clicks the "Rename Profile" `Button`. Command's `CanExecute` checks `ActiveFilterProfile != null`.
2.  **`RenameProfileCommand`:** Executes in `MainViewModel`.
    *   Gets the current name from `ActiveFilterProfile.Name`.
    *   Uses `IInputPromptService.ShowInputDialog` to get a new name from the user.
    *   Validates the new name (not empty, not conflicting with other profile names).
    *   If valid, sets `ActiveFilterProfile.Name = newName`. (The `FilterProfileViewModel.OnNameChanged` partial method updates the underlying `Model.Name`).
    *   Calls `SaveCurrentSettings()`.
3.  **UI Update:** The `ComboBox` display *might* not update automatically if only `DisplayMemberPath` is used. The underlying data is correct, and it will display correctly after re-selection or restart. (More complex binding or collection refreshing could force immediate UI update if required).

### 5. Deleting the Active Profile

1.  **Trigger:** User clicks the "Delete Profile" `Button`. Command's `CanExecute` checks `ActiveFilterProfile != null` and potentially `AvailableProfiles.Count > 1`.
2.  **`DeleteProfileCommand`:** Executes in `MainViewModel`.
    *   Checks if it's the last profile; shows message and returns if true.
    *   Prompts the user for confirmation using `MessageBox.Show`.
    *   If confirmed:
        *   Remembers the profile to remove and its index.
        *   Removes the `FilterProfileViewModel` from `AvailableProfiles`.
        *   Selects another profile (e.g., the previous one or the first) by setting `MainViewModel.ActiveFilterProfile`.
        *   Calls `SaveCurrentSettings()`.
3.  **`OnActiveFilterProfileChanged()`:** Is triggered by setting `ActiveFilterProfile` to the newly selected one.
4.  **(Flow continues as in step 2, points 4-7):** `UpdateActiveTreeRootNodes` updates the `TreeView`, `TriggerFilterUpdate` applies the selected profile's filter.
5.  **UI Update:** The deleted profile disappears from the `ComboBox`. Another profile becomes selected. The `TreeView` and log view update.

### 6. Editing Nodes within the Active Profile Tree

1.  **Trigger:** User interacts with the `TreeView` or clicks node management buttons ("Add Filter Node", "Remove Node", "Edit Node").
2.  **Selection:** `TreeView_SelectedItemChanged` event handler in `MainWindow.xaml.cs` updates `MainViewModel.SelectedFilterNode`.
3.  **Commands (`AddFilterCommand`, `RemoveFilterNodeCommand`, `ToggleEditNodeCommand`):** Execute in `MainViewModel`.
    *   These commands now operate on the `ActiveFilterProfile.RootFilterViewModel` and the `SelectedFilterNode`.
    *   They modify the structure of the `IFilter` tree within `ActiveFilterProfile.Model`.
    *   They update the `FilterViewModel` hierarchy (e.g., `SelectedFilterNode.AddChildFilter`, `ActiveFilterProfile.SetModelRootFilter`).
    *   Crucially, modifications made via `FilterViewModel` methods (like `AddChildFilter`, `RemoveChild`) or `FilterProfileViewModel.SetModelRootFilter` use the **callback** mechanism (`_filterConfigurationChangedCallback`) which points back to `MainViewModel.TriggerFilterUpdate`.
    *   The `Enabled` property setter and `EndEditCommand` in `FilterViewModel` also use this callback.
4.  **`TriggerFilterUpdate()`:** Is called via the callback. Signals the `LogFilterProcessor` with the *modified* filter tree from the *still active* profile.
5.  **UI Update:** The `TreeView` updates because its `ItemsSource` (`ActiveTreeRootNodes`) or the child collections within its nodes (`FilterViewModel.Children`) are modified (WPF handles `ObservableCollection` changes). The log view updates based on the results from the `LogFilterProcessor`. Changes are saved via `SaveCurrentSettings()` called at the end of the command handlers.

## Saving / Persistence

*   Settings, including all `FilterProfile` models and the `LastActiveProfileName`, are saved whenever `SaveCurrentSettings()` is called.
*   This currently happens after profile management actions (Create, Rename, Delete, selection change) and node editing actions (`AddFilter`, `RemoveFilterNode`, `ToggleEditNode` via its callback).
*   It's also called during application shutdown (`MainWindow.Closing` -> `MainViewModel.Cleanup`).

## Connection to Filtering Process

Any action that changes the effective filter rules (selecting a different profile, modifying the active profile's tree, changing the `ContextLines` property) ultimately calls `MainViewModel.TriggerFilterUpdate()`. This method passes the *current active profile's filter tree* and the context setting to `LogFilterProcessor.UpdateFilterSettings()`, initiating a debounced background re-filter of the entire log document.