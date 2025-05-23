# Logonaut Tabbed Interface Implementation Plan

## Phase 0: Foundation & Refactoring (Internal Changes)

**Goal:** Prepare the `MainViewModel` and core components to support multiple log contexts without immediately showing a tabbed UI. The application should still function as a single-log-view application after this phase.

### Step 0.1: Introduce `TabViewModel` (Core Structure)
*   **Action:** Create a new `TabViewModel` class.
*   **Details:**
    *   Move the following properties and their related logic from `MainViewModel` into `TabViewModel`:
        *   `LogDocument LogDoc`
        *   `ILogSource CurrentActiveLogSource` (rename to `LogSource` in `TabViewModel`)
        *   `IReactiveFilteredLogStream ReactiveFilteredLogStream`
        *   `ObservableCollection<FilteredLogLine> FilteredLogLines`
        *   `long TotalLogLines`
        *   `string? CurrentLogFilePath` (or a more generic `SourceIdentifier`)
        *   `ObservableCollection<IFilter> FilterHighlightModels`
        *   `int HighlightedFilteredLineIndex`, `int HighlightedOriginalLineNumber`
        *   `string TargetOriginalLineNumberInput`, `string? JumpStatusMessage`, `bool IsJumpTargetInvalid` (and associated jump commands)
        *   Search state: `SearchText`, `IsCaseSensitiveSearch`, `_searchMatches`, `_currentSearchIndex`, `SearchMarkers`, `CurrentMatchOffset`, `CurrentMatchLength`, and search commands (`PreviousSearchCommand`, `NextSearchCommand`).
    *   `TabViewModel` constructor should accept:
        *   `string initialHeader`
        *   `string initialAssociatedProfileName`
        *   `SourceType initialSourceType`
        *   `string? initialSourceIdentifier` (e.g., file path or pasted content storage path)
        *   Services: `ILogSourceProvider`, `ICommandExecutor` (from `MainViewModel`), `SynchronizationContext`.
        *   It will create its own `LogDocument`, and will create its `ILogSource` and `IReactiveFilteredLogStream` when activated.
    *   `TabViewModel` should implement `IDisposable` to dispose its `LogSource` and `ReactiveFilteredLogStream`.
    *   Add `Header` property (string, for user-editable tab name).
    *   Add `DisplayHeader` property (string, combines `Header` and `LastActivityTimestamp` for tab display).
    *   Add `IsActive` property (bool) to `TabViewModel`.
    *   Add `AssociatedFilterProfileName` (string) to `TabViewModel`. When this changes, the tab's filter pipeline should be updated.
    *   Add `LastActivityTimestamp` (DateTime?) to `TabViewModel`.
    *   Add `SourceType SourceType` (enum: File, Pasted, Simulator).
    *   Add `SourceIdentifier` (string, stores file path or pasted content temp path).
    *   Add `PastedContentStoragePath` (string, only for `SourceType.Pasted`).
    *   Add `IsModified` (bool, for pasted content tabs to track if they need saving).
    *   Implement methods in `TabViewModel` for:
        *   `ActivateAsync()`: Creates/recreates `ILogSource`, `IReactiveFilteredLogStream`, subscribes, calls `PrepareAndGetInitialLinesAsync` (if needed), starts monitoring, triggers initial filter.
        *   `Deactivate()`: Calls `StopMonitoring`, disposes source/stream (optional), updates `LastActivityTimestamp`.
        *   `LoadPastedContent(string text)`: Initializes `LogDoc` with pasted text.
        *   `LoadFromFileAsync(string filePath)`: For file-based tabs.
        *   `StartSimulator()`: For simulator tabs.
        *   `TriggerFilterUpdate()`: Calls `UpdateFilterSettings` on its `ReactiveFilteredLogStream`.
        *   Search-related methods (UpdateSearchMatches, SelectAndScrollToCurrentMatch) now operate on this tab's `FilteredLogLines` and `LogOutputEditor` instance (passed or accessed).
    *   The `AddLineToLogDocument` callback for `IReactiveFilteredLogStream` will now be a method within `TabViewModel` that adds to *its own* `LogDoc`.
*   **Impact:**
    *   `MainViewModel` will become much leaner regarding single log state.
    *   Application still works with one implicit "tab" internally.
*   **Testing:**
    *   Unit test `TabViewModel`'s ability to load a log file, process filters, and manage its active/inactive state (mocking services).
    *   Ensure existing `MainViewModel` tests (if any focusing on log processing) are adapted or new ones created for `TabViewModel`.

### Step 0.2: `MainViewModel` Manages a Single `TabViewModel`
*   **Action:** Modify `MainViewModel` to instantiate and manage a single `TabViewModel`.
*   **Details:**
    *   `MainViewModel` will have an `ActiveTabViewModel` property (of type `TabViewModel`).
    *   On startup, `MainViewModel` creates one `TabViewModel` (e.g., an empty "Untitled" pasted content tab) and sets it as `ActiveTabViewModel`. Calls `ActiveTabViewModel.ActivateAsync()`.
    *   `MainViewModel`'s commands (Open File, Paste, Simulator Start, etc.) will now delegate to the `ActiveTabViewModel` or create new tabs.
    *   UI elements (like `LogOutputEditor`, status bar stats) previously bound to `MainViewModel` properties will now need to bind to `MainViewModel.ActiveTabViewModel.RelevantProperty`.
    *   The globally selected `ActiveFilterProfile` in `MainViewModel` will set `ActiveTabViewModel.AssociatedFilterProfileName`.
*   **Impact:**
    *   Application still looks and behaves identically to the user.
    *   Internal architecture is now prepared for multiple tabs.
    *   Undo/Redo stack remains global in `MainViewModel`.
*   **Testing:**
    *   Ensure all existing application functionalities work as before (opening files, filtering, search, simulator).
    *   Unit test `MainViewModel`'s delegation to `ActiveTabViewModel`.

## Phase 1: Basic Tabbed UI and Multiple Tab Management

**Goal:** Introduce the `TabControl` UI and allow opening multiple file-based tabs.

### Step 1.1: Create `TabView` UserControl and Integrate `TabControl`
*   **Action:** Create `Logonaut.UI/Views/TabView.xaml` and `TabView.xaml.cs`. Add a `TabControl` to `MainWindow.xaml`.
*   **Details:**
    *   **`TabView.xaml`:**
        *   This UserControl will contain the layout for a single tab's content (i.e., the `LogOutputEditor`, its toolbar, search panel, overview ruler, loading overlay).
        *   Its `DataContext` will be a `TabViewModel`.
        *   Bindings within `TabView.xaml` will be to properties of its `TabViewModel` DataContext (e.g., `FilteredLogLines`, `SearchText`, `IsLoading` for the overlay specific to this tab).
        *   The `LogOutputEditor` instance within `TabView` will need to be passed to its `TabViewModel` (e.g., via a method `SetLogEditorInstance` on `TabViewModel`, called from `TabView.xaml.cs` on editor load).
    *   **`MainWindow.xaml`:**
        *   The right-hand panel (where `LogOutputEditor` currently is) will now host a `TabControl`.
        *   `MainViewModel` will now have an `ObservableCollection<TabViewModel> TabViewModels`.
        *   The `TabControl.ItemsSource` will bind to `TabViewModels`.
        *   `TabControl.SelectedItem` will bind to `MainViewModel.ActiveTabViewModel` (TwoWay).
        *   Define a `DataTemplate` for the `TabItem.Header` to display `TabViewModel.DisplayHeader` and include a Close button.
        *   The `TabControl.ContentTemplate` will be a `DataTemplate` containing an instance of `<views:TabView />`.
*   **Impact:**
    *   UI will change to show a tab. `MainViewModel` creates one `TabViewModel` at startup.
    *   Opening a new file will still replace the content of this single `TabViewModel` or its source.
*   **Testing:**
    *   Visual inspection: Tab header appears. `TabView` content (editor, etc.) is displayed for the initial tab.
    *   UI interaction: Ensure `ActiveTabViewModel` in `MainViewModel` updates correctly.

### Step 1.2: Opening New File-Based Tabs
*   **Action:** Modify "Open Log File" logic in `MainViewModel` to create a new `TabViewModel` for each new file.
*   **Details:**
    *   When `MainViewModel.OpenLogFileAsync` is invoked:
        *   **Check for existing tab:** Iterate `TabViewModels`. If a tab with the same `SourceIdentifier` (file path) exists, set it as `ActiveTabViewModel` and return.
        *   If not found, create a new `TabViewModel` instance:
            *   `initialHeader`: File name.
            *   `initialAssociatedProfileName`: `MainViewModel.ActiveFilterProfile.Name`.
            *   `initialSourceType`: `SourceType.File`.
            *   `initialSourceIdentifier`: Selected file path.
        *   Add the new `TabViewModel` to `MainViewModel.TabViewModels`.
        *   Set the new `TabViewModel` as `ActiveTabViewModel`. (This will trigger its `ActivateAsync` via `OnActiveTabViewModelChanged`).
*   **Impact:**
    *   Users can now open multiple files, each in its own tab.
    *   Only the `ActiveTabViewModel`'s `LogSource` will be actively monitoring.
*   **Testing:**
    *   Open multiple distinct log files; each gets a tab.
    *   Open an already open file; existing tab is activated.
    *   Verify only the selected tab updates with new log lines if the file is tailed.

### Step 1.3: Tab Renaming/Notes
*   **Action:** Allow users to rename tab headers.
*   **Details:**
    *   Add an `IsEditingHeader` property to `TabViewModel`.
    *   Modify the `TabItem.Header` `DataTemplate`:
        *   Include an editable `TextBox` bound to `TabViewModel.Header` (visible when `IsEditingHeader` is true).
        *   Include a `TextBlock` bound to `TabViewModel.DisplayHeader` (visible when `IsEditingHeader` is false).
        *   Add a "Rename" option to the tab's context menu (see Phase 5.1) or a double-click behavior on the header to toggle `IsEditingHeader`.
    *   `TabViewModel.Header` setter should update `DisplayHeader`.
*   **Impact:** Users can customize tab identification.
*   **Testing:**
    *   Rename a tab; header updates.
    *   Persistence will come later, so renames are session-only for now.

## Phase 2: Active/Inactive Tab Logic & Simulator/Pasted Tabs

**Goal:** Properly manage resources for active vs. inactive tabs and integrate simulator and pasted content tabs.

### Step 2.1: Activate/Deactivate Tab Logic
*   **Action:** Implement robust activation/deactivation logic in `MainViewModel` when `ActiveTabViewModel` changes.
*   **Details:**
    *   In `MainViewModel.OnActiveTabViewModelChanged(TabViewModel oldValue, TabViewModel newValue)`:
        *   **Old Active Tab (if `oldValue` is not null):**
            *   Call `oldValue.Deactivate()`.
        *   **New Active Tab (if `newValue` is not null):**
            *   Call `newValue.ActivateAsync()`.
    *   `TabViewModel.Deactivate()`:
        *   Calls `StopMonitoring()` on its `LogSource`.
        *   Sets `LastActivityTimestamp`.
        *   Disposes its `ReactiveFilteredLogStream` and `LogSource`.
        *   Sets `IsActive = false`.
        *   Updates `DisplayHeader`.
    *   `TabViewModel.ActivateAsync()`:
        *   Sets `IsActive = true`.
        *   Creates `LogSource` (based on `SourceType` and `SourceIdentifier`).
        *   Creates `ReactiveFilteredLogStream`.
        *   Subscribes to stream updates (updating its own `FilteredLogLines`, etc.).
        *   Calls `LogSource.PrepareAndGetInitialLinesAsync(...)` if first activation or if needed (e.g., file changed).
        *   Calls `LogSource.StartMonitoring()`.
        *   Triggers its filter update using its `AssociatedFilterProfileName` (fetching the profile from `MainViewModel.AvailableProfiles`).
        *   Updates `DisplayHeader`.
*   **Impact:**
    *   Resource usage is optimized.
    *   Switching tabs correctly resumes/pauses monitoring and updates views.
*   **Testing:**
    *   Switch between tabs; verify monitoring stops/starts for the correct file.
    *   Verify `LastActivityTimestamp` on inactive tab headers.
    *   Verify log view updates correctly for the newly activated tab.

### Step 2.2: Simulator Tabs
*   **Action:** Adapt simulator logic in `MainViewModel` to work with tabs.
*   **Details:**
    *   When `MainViewModel.ToggleSimulatorCommand` (or a new "Start Simulator" command) is invoked to *start* simulation:
        *   Create a new `TabViewModel`:
            *   `initialHeader`: "Simulator" (or "Simulator 1").
            *   `initialAssociatedProfileName`: `MainViewModel.ActiveFilterProfile.Name`.
            *   `initialSourceType`: `SourceType.Simulator`.
            *   `initialSourceIdentifier`: null or a unique simulator ID.
        *   Add to `TabViewModels` and set as `ActiveTabViewModel`. (This triggers its `ActivateAsync`, which will internally call its `StartSimulator` method or similar to set up the `ISimulatorLogSource`).
        *   Simulator controls (LPS, Error Freq, Burst) in `MainViewModel` now operate on `ActiveTabViewModel.LogSource` (cast to `ISimulatorLogSource` if `ActiveTabViewModel.SourceType == SourceType.Simulator`).
    *   If stopping the simulator via UI, it effectively deactivates/closes the simulator tab.
*   **Impact:** Simulator runs in its own dedicated tab.
*   **Testing:**
    *   Start simulator; new tab appears and becomes active.
    *   Simulator controls affect the active simulator tab.
    *   Switching away from simulator tab deactivates (pauses) it.

### Step 2.3: Pasted Content Tabs
*   **Action:** Implement logic for creating tabs from pasted content.
*   **Details:**
    *   When `MainViewModel.LoadLogFromText(string text)` is called (from `Ctrl+V` handler in `MainWindow.xaml.cs`):
        *   Create a new `TabViewModel`:
            *   `initialHeader`: "Pasted Content" (or "Pasted 1").
            *   `initialAssociatedProfileName`: `MainViewModel.ActiveFilterProfile.Name`.
            *   `initialSourceType`: `SourceType.Pasted`.
            *   `initialSourceIdentifier`: null initially.
        *   Add to `TabViewModels` and set as `ActiveTabViewModel`.
        *   The `ActiveTabViewModel.ActivateAsync()` for a pasted tab will involve:
            *   Calling its `LoadPastedContent(text)` method to populate its `LogDoc`.
            *   Setting up its `ReactiveFilteredLogStream` (with a "null" or "completed" `ILogSource` as there are no further lines).
            *   Triggering its filter update.
*   **Impact:** Pasted content gets its own tab.
*   **Testing:**
    *   Paste text; a new tab appears with the content.
    *   Filtering works on the pasted content tab.

### Step 2.4: Close Tab Functionality
*   **Action:** Implement ability to close tabs.
*   **Details:**
    *   Add a `CloseCommand` to `TabViewModel`.
    *   The close button in `TabItem.Header` `DataTemplate` binds to this command.
    *   `TabViewModel.CloseCommand` execution:
        *   Raises an event or calls a method on `MainViewModel` (passed via constructor or event aggregator) requesting its own closure.
        *   `MainViewModel` receives this request:
            *   Removes the `TabViewModel` from `TabViewModels`.
            *   Calls `Dispose()` on the removed `TabViewModel` (which handles `Deactivate` and resource cleanup).
            *   Handles deletion of `PastedContentStoragePath` file if applicable (see Phase 4.1).
            *   If `TabViewModels` becomes empty, create a default empty tab.
            *   Select another tab to be active.
*   **Impact:** Users can manage their workspace.
*   **Testing:**
    *   Close tabs; they disappear.
    *   Active tab selection logic works correctly after a close.
    *   Ensure resources of closed tabs are released.

## Phase 3: Persistence

**Goal:** Save and restore tab sessions and their states.

### Step 3.1: Define `TabSessionInfo`
*   **Action:** Create `TabSessionInfo` class in `Logonaut.Common`.
*   **Details:**
    *   Properties:
        *   `string? FilePath` (for file-based tabs)
        *   `string? PastedContentStoragePath` (for pasted content tabs)
        *   `bool IsPastedContent`
        *   `string TabHeaderName` (user-defined name)
        *   `string AssociatedFilterProfileName`
        *   `DateTime? LastActivityTimestamp`
        *   `double ScrollOffset` (vertical scroll position of the `LogOutputEditor`)
        *   `int HighlightedOriginalLineNumber` (to restore selected line)
        *   `SourceType` enum (File, Pasted, Simulator) - easier than multiple booleans.
*   **Impact:** Defines the data structure for saving tab state.
*   **Testing:** (No direct testing yet, part of settings save/load).

### Step 3.2: Modify `LogonautSettings`
*   **Action:** Add `List<TabSessionInfo> OpenTabs { get; set; }` to `LogonautSettings`.
*   **Details:** Initialize to an empty list.
*   **Impact:** Settings class can now store tab information.
*   **Testing:** (As above).

### Step 3.3: Save Tab State
*   **Action:** Implement saving of `OpenTabs` in `MainViewModel.SaveCurrentSettings`.
*   **Details:**
    *   Iterate `MainViewModel.TabViewModels`.
    *   For each `TabViewModel` that is NOT `IsSimulatorSession`:
        *   Create a `TabSessionInfo` object.
        *   Populate it from `TabViewModel` properties (FilePath, ProfileName, Header, Timestamps, ScrollPosition, HighlightedLine).
        *   **For pasted content:**
            *   If `TabViewModel.IsPastedContent` is true and `PastedContentStoragePath` is null (or content changed):
                *   Generate a unique filename (e.g., `guid.logonautpaste`).
                *   Save `TabViewModel.LogDoc.ToList()` content to this file in `AppData/Logonaut/PastedLogs/`.
                *   Store this path in `TabSessionInfo.PastedContentStoragePath`.
        *   Add to `LogonautSettings.OpenTabs`.
*   **Impact:** Tab states (excluding simulator) are persisted.
*   **Testing:**
    *   Open various tabs (file, pasted).
    *   Close app; inspect `settings.json` and `PastedLogs` folder.
    *   Verify `TabSessionInfo` and pasted content files are created correctly.

### Step 3.4: Restore Tab State
*   **Action:** Implement restoring of tabs in `MainViewModel` constructor (or a `LoadPersistedState` method called after settings are loaded).
*   **Details:**
    *   After loading `LogonautSettings`:
        *   Iterate `settings.OpenTabs`.
        *   For each `TabSessionInfo`:
            *   Create a new `TabViewModel`.
            *   Set `IsFileBased` or `IsPastedContent`.
            *   Set `Header`, `AssociatedFilterProfileName`, `LastActivityTimestamp`.
            *   If `IsFileBased`, set `CurrentLogFilePath`.
            *   If `IsPastedContent` and `PastedContentStoragePath` exists:
                *   Load content from the stored temp file into `TabViewModel.LogDoc`.
            *   Store `ScrollOffset` and `HighlightedOriginalLineNumber` in `TabViewModel` to be applied when the tab view is ready.
            *   Add to `MainViewModel.TabViewModels`.
        *   All restored tabs are initially inactive (no `StartMonitoring` yet).
        *   If no tabs were restored (e.g., first run or empty `OpenTabs`), create a default empty tab.
        *   Select the first tab (or a last active remembered tab if you store that too) as `ActiveTabViewModel`. This will trigger its activation logic (Step 2.1).
*   **Impact:** Application restores its previous tab layout on startup.
*   **Testing:**
    *   Start app; previously open tabs (file, pasted) are restored.
    *   Headers, profiles should be correct.
    *   Pasted content is restored.
    *   Initially, no tab is actively monitoring. Activating one starts monitoring.

### Step 3.5: Restore Scroll Position and Selected Line
*   **Action:** When a tab becomes active (or its view is loaded), apply stored scroll and selection.
*   **Details:**
    *   `TabViewModel` will need to store `RestoredScrollOffset` and `RestoredHighlightedOriginalLineNumber`.
    *   When a `TabViewModel`'s view content is loaded (e.g., its `LogOutputEditor` is ready):
        *   If `RestoredScrollOffset` is valid, scroll the editor.
        *   Set `HighlightedOriginalLineNumber` to `RestoredHighlightedOriginalLineNumber`. This will trigger highlighting and potentially scrolling via existing mechanisms.
    *   This might require an event from the View (e.g., `LogOutputEditor.Loaded` for that specific tab's editor) back to the `TabViewModel` or `MainViewModel` to trigger this restoration.
*   **Impact:** Better UX, restores user's context within each tab.
*   **Testing:**
    *   Scroll and select lines in tabs. Close and reopen app.
    *   Verify scroll position and selection are restored for each tab when it's viewed.

## Phase 4: Pasted Content Management & Filter Profile Interaction

**Goal:** Refine pasted content handling and profile interactions across tabs.

### Step 4.1: Deleting Saved Pasted Content
*   **Action:** When a `TabViewModel` representing pasted content is closed, delete its associated temp storage file.
*   **Details:**
    *   In `MainViewModel`'s logic for handling tab closure:
        *   If the closing `TabViewModel.IsPastedContent` is true and `TabViewModel.PastedContentStoragePath` is not null:
            *   Delete the file at `PastedContentStoragePath`.
*   **Impact:** Prevents orphaned pasted content files in AppData.
*   **Testing:**
    *   Create a pasted content tab. Close app (content saved). Reopen (content restored).
    *   Close the pasted content tab from the UI. Verify the temp file is deleted.

### Step 4.2: Profile Change Applies to Active Tab Only
*   **Action:** Ensure changing the selected profile in `MainViewModel`'s UI only affects the `ActiveTabViewModel`.
*   **Details:**
    *   When `MainViewModel.ActiveFilterProfile` (the ComboBox selection) changes:
        *   If `MainViewModel.ActiveTabViewModel` is not null:
            *   Set `ActiveTabViewModel.AssociatedFilterProfileName` to the new profile's name.
            *   This change on `ActiveTabViewModel` should trigger it to re-evaluate its filters.
*   **Impact:** Profile selection is now correctly scoped to the active tab.
*   **Testing:**
    *   Have multiple tabs open with different profiles.
    *   Select Tab A. Change global profile to Profile X. Verify Tab A now uses Profile X.
    *   Select Tab B (which was using Profile Y). Verify Tab B is still using Profile Y. Change global profile to Profile Z. Verify Tab B now uses Profile Z, and Tab A is still on X.

### Step 4.3: Filter Settings Change in a Profile Affects All Tabs Using It (On Activation)
*   **Action:** When a filter within a `FilterProfileViewModel` is modified (e.g., text change, enabled toggle), this change is saved to the model (`FilterProfile`). Inactive tabs using this profile will see the changes when they are next activated.
*   **Details:**
    *   Current undo/redo system in `MainViewModel` already modifies the `FilterProfile` model directly or via `FilterViewModel` wrappers.
    *   When an inactive `TabViewModel` becomes active (Step 2.1):
        *   It re-applies its filter. It will fetch its `AssociatedFilterProfileName` and get the (potentially updated) `FilterProfile` model from `MainViewModel.AvailableProfiles`.
        *   The `ReactiveFilteredLogStream` for that tab will then use the latest definition from this profile.
*   **Impact:** Filter changes are consistently applied. No immediate visual update for inactive tabs, which is acceptable as per requirements.
*   **Testing:**
    *   Tab A uses Profile P1. Tab B uses Profile P1.
    *   Activate Tab A. Modify a filter in P1.
    *   Activate Tab B. Verify Tab B's view reflects the modified P1.

## Phase 5: Polish, UX Enhancements, and Future Ideas

**Goal:** Add smaller UX improvements and consider future directions.

### Step 5.1: Tab Context Menu
*   **Action:** Implement a context menu for tabs.
*   **Details:**
    *   Add `ContextMenu` to `TabItem.Header` `DataTemplate`.
    *   Bind `MenuItem` commands to commands in `TabViewModel` or `MainViewModel`.
    *   Commands: Close, Close Others, Reload (for file tabs), Copy Path.
*   **Impact:** Improved tab management.
*   **Testing:** Verify context menu commands work as expected.

### Step 5.2: Visual Indication for File Changes in Inactive Tabs
*   **Action:** (Future Idea) Add a small visual cue (e.g., a dot on the tab header) if an inactive file-based tab's underlying file has changed on disk since it was last active.
*   **Details:**
    *   This would require `TabViewModel`s for inactive file tabs to periodically (or via `FileSystemWatcher`) check `LastWriteTime` of their file.
    *   If changed, set a boolean flag `HasUnseenChanges` on `TabViewModel`.
    *   Bind this flag to a visual element in the tab header template.
    *   When tab is activated, flag is cleared, and `PrepareAndGetInitialLinesAsync` needs to be smart about handling new content (e.g., tailing from last known good position, or offering a full reload).
*   **Impact:** Users are aware of updates in background tabs.
*   **Testing:** Modify a file for an inactive tab; indicator appears. Activate tab; content updates and indicator clears.

### Step 5.3: "Pinning" Tabs / Color-Coding Tabs
*   **Action:** (Future Ideas)
    *   **Pinning:** Add `IsPinned` to `TabViewModel` and `TabSessionInfo`. Pinned tabs could appear first, or have a different close behavior.
    *   **Color-Coding:** Add `TabColor` (Brush or string key) to `TabViewModel` and `TabSessionInfo`. Allow user to pick a color via context menu. Tab header background uses this color.
*   **Impact:** Enhanced organization.
*   **Testing:** Visual verification and persistence of these states.
