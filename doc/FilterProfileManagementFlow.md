# Logonaut: Filter Profile Management Flow (Simplified)

This document outlines the core flow for managing named filter profiles in Logonaut. It is just the high level overview.

## Key Components Involved

*   **UI:** ComboBox for selection, Buttons (New, Rename, Delete), TreeView for filter rules.
*   **ViewModels:** `MainViewModel` (orchestrator), `FilterProfileViewModel` (represents a profile in UI), `FilterViewModel` (represents a filter node).
*   **Models:** `FilterProfile` (stores name and filter rules), `LogonautSettings` (holds all profiles).
*   **Services:** `SettingsManager` (loads/saves JSON), `LogFilterProcessor` (applies filters).

## Core Flows

### 1. Application Startup

1.  `MainViewModel` starts.
2.  `SettingsManager` loads `LogonautSettings` (profiles, last active name).
3.  `MainViewModel` creates `FilterProfileViewModel`s for each profile and populates the `AvailableProfiles` list (for ComboBox).
4.  `MainViewModel` sets the `ActiveFilterProfile` based on the loaded settings.
5.  Setting the active profile updates the `TreeView` and triggers the `LogFilterProcessor` to apply the initial filter.

### 2. Selecting a Profile

1.  User selects a profile in the ComboBox.
2.  `MainViewModel.ActiveFilterProfile` is updated.
3.  The `TreeView` display updates to show the selected profile's filters.
4.  `LogFilterProcessor` is signaled to re-filter the log using the new profile's rules.
5.  The selected profile name is saved as the `LastActiveProfileName` via `SettingsManager`.

### 3. Creating a New Profile

1.  User clicks the "New Profile" button.
2.  `MainViewModel` creates a new `FilterProfile` model (with a unique default name) and its `FilterProfileViewModel`.
3.  The new VM is added to the `AvailableProfiles` list and set as the `ActiveFilterProfile`.
4.  This triggers UI/filter updates and saves settings (as in Selecting).
5.  The UI enters inline editing mode for the new profile's name.

### 4. Renaming the Active Profile (Inline Edit)

1.  User clicks the "Rename" button.
2.  The active `FilterProfileViewModel` enters edit mode (`IsEditing = true`).
3.  UI changes: ComboBox text is replaced by an editable `TextBox` bound to the profile name.
4.  User types a new name in the `TextBox`.
5.  User commits (Enter/Lost Focus) or cancels (Escape).
6.  `MainViewModel` observes the name change and validates it (checks for empty or duplicate names).
7.  **If Valid:** Updates the underlying `FilterProfile` model and saves settings.
8.  **If Invalid:** Reverts the name in the `FilterProfileViewModel` and shows an error.
9.  The `FilterProfileViewModel` exits edit mode (`IsEditing = false`), and the UI switches back to showing the ComboBox text.

### 5. Deleting the Active Profile

1.  User clicks the "Delete Profile" button.
2.  `MainViewModel` confirms with the user (unless it's the last profile).
3.  The corresponding `FilterProfileViewModel` is removed from `AvailableProfiles`.
4.  Another profile is automatically selected (e.g., the previous one, or a new "Default" if it was the last one).
5.  This selection change triggers UI/filter updates and saves settings.

### 6. Editing Filter Nodes

1.  User interacts with the `TreeView` for the *active* profile (add, remove, enable/disable, edit filter value).
2.  Commands in `MainViewModel` modify the `FilterViewModel` hierarchy and the underlying `IFilter` structure within the `ActiveFilterProfile.Model`.
3.  Changes within the ViewModels trigger a callback to `MainViewModel`.
4.  `MainViewModel` signals `LogFilterProcessor` to re-filter using the *modified active profile*.
5.  Settings are saved.

### 7. Saving / Persistence

*   `MainViewModel` uses `SettingsManager` to save the current `LogonautSettings` (containing all `FilterProfile` models and `LastActiveProfileName`) to `settings.json`.
*   Saving occurs automatically after profile management actions (select, create, delete, rename commit) and filter node edits. It also happens on application shutdown.