# Logonaut Tabbed Interface Implementation Plan

## Phase 0: Foundation & Refactoring (Internal Changes)

**Goal:** Prepare the `MainViewModel` and core components to support multiple log contexts without immediately showing a tabbed UI. The application should still function as a single-log-view application after this phase.

### Step 0.1: `TabViewModel` Enhancements (File Reset Logic)

*   **Action:** Implement the planned "snapshot" behavior in `TabViewModel` for when a monitored file is reset.
*   **Details:**
    *   **Modify `TabViewModel.ActivateAsync(...)`:**
        *   When `SourceType` is `File`, ensure `TabViewModel` passes a new internal method, let's call it `HandleSourceFileRestarted`, as the callback to its `LogDataProcessor.ActivateAsync`.
    *   **Implement `TabViewModel.HandleSourceFileRestarted()` method:**
        *   This method will be invoked by the `LogTailer` -> `FileLogSource` -> `LogDataProcessor` callback chain when a file truncation is detected.
        *   **Store original file path:** Keep the current `SourceIdentifier` (which is the file path) in a new private field, e.g., `_originalFilePathBeforeSnapshot`.
        *   **Update `Header`:** Append a timestamp or "Snapshot" indicator to the current `Header` (e.g., `Header += $" (Snapshot @ {DateTime.Now:HH:mm:ss})"`). This makes the current tab visually distinct as a snapshot.
        *   **Call `DeactivateLogProcessing()`:** This will stop any active monitoring on the (now reset) file.
        *   **Change `SourceType`:** Set `this.SourceType = SourceType.Snapshot;` (A `Snapshot` enum value needs to be added to `SourceType`).
        *   **Change `SourceIdentifier`:** Set `this.SourceIdentifier = Guid.NewGuid().ToString();` to give this snapshot tab a unique ID, distinct from the original file path. This prevents it from trying to reload the original file if it were to be reactivated.
        *   **Set `IsModified = true;`**: This flags the snapshot tab's content (its `LogDocument`) as needing to be saved if persistence for pasted/snapshot content is implemented later (as per Phase 3.3).
        *   **Raise `SourceRestartDetected` event:**
            *   Define the event in `TabViewModel`: `public event Action<TabViewModel /*snapshotTab*/, string /*restartedFilePath*/>? SourceRestartDetected;`
            *   Invoke it: `SourceRestartDetected?.Invoke(this, _originalFilePathBeforeSnapshot);`
*   **Impact:**
    *   `TabViewModel` will now correctly transition into a "snapshot" state when its underlying file is reset, retaining its current `LogDocument` content.
    *   It will notify `MainViewModel` (which will subscribe to this event) that the original file path needs to be re-monitored (likely in a new tab, as per Phase 1/2).
*   **Testing:**
    *   Unit test `TabViewModel`:
        *   Mock the `LogDataProcessor` or the callback chain to simulate a file reset signal.
        *   Verify that `HandleSourceFileRestarted` correctly changes `Header`, `SourceType`, `SourceIdentifier`, sets `IsModified`.
        *   Verify the `SourceRestartDetected` event is raised with the correct `TabViewModel` instance (itself) and the original file path.

### Step 0.2: `MainViewModel` Responding to `TabViewModel`'s `SourceRestartDetected` (Single-Tab Context)

*   **Action:** Modify `MainViewModel` to subscribe to `_internalTabViewModel.SourceRestartDetected` and handle it in a way that fits the current single-tab model (i.e., by reloading the *same* internal tab).
*   **Details:**
    *   In `MainViewModel`'s constructor, after creating `_internalTabViewModel`:
        *   Subscribe: `_internalTabViewModel.SourceRestartDetected += HandleInternalTabSourceRestart;`
        *   Remember to unsubscribe in `Dispose`.
    *   **Implement `MainViewModel.HandleInternalTabSourceRestart(TabViewModel snapshotTab, string restartedFilePath)`:**
        *   For Phase 0, since there's only one tab, the "snapshotTab" *is* `_internalTabViewModel`.
        *   Log a message indicating that a file reset was detected for `restartedFilePath`.
        *   Call `this.LoadLogFileCoreAsync(restartedFilePath);`. This will reconfigure `_internalTabViewModel` to load the `restartedFilePath` from scratch. The previous content (now marked as a snapshot within `_internalTabViewModel`) will be overwritten.
        *   *(Self-correction from plan: The original plan was for MainViewModel to create a *new* tab here. For Phase 0, we keep it to one tab, so the internal tab is re-purposed. The snapshot tab essentially "disappears" in terms of its old content being visible, but the mechanism for `TabViewModel` to become a snapshot and raise the event is tested).*
*   **Impact:**
    *   The application will now correctly reload a file if it's reset, maintaining the single-view behavior.
    *   The underlying `TabViewModel` properly transitions to a snapshot state internally, even if that state isn't fully utilized in the UI yet.
*   **Testing:**
    *   Modify the existing `LogFileTruncation_TriggersReload_ThroughCallbackMechanism` test:
        *   Ensure `MockLogSource.SimulateFileResetCallback()` is called.
        *   Verify `_mockFileLogSource.PrepareCallCount` is incremented (indicating `LoadLogFileCoreAsync` was called again by `MainViewModel`).
        *   Verify that after the reload, `_internalTabViewModel.SourceType` is back to `SourceType.File` and `SourceIdentifier` is the `restartedFilePath`.
        *   Verify `_internalTabViewModel.Header` no longer has the "(Snapshot)" text from before the reload (it gets reset by `LoadLogFileCoreAsync`).
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
        *   **Check for existing tab:** Iterate `TabViewModels`. If a tab with the same `SourceIdentifier` (file path) and `SourceType.File` exists and is active, set it as `ActiveTabViewModel` and return. (Consider if a non-active file tab should be reactivated or a new one opened - current behavior is to activate existing).
        *   If not found or different type, create a new `TabViewModel` instance:
            *   `initialHeader`: File name.
            *   `initialAssociatedProfileName`: `MainViewModel.ActiveFilterProfile.Name`.
            *   `initialSourceType`: `SourceType.File`.
            *   `initialSourceIdentifier`: Selected file path.
            *   Subscribe to its `SourceRestartDetected` event.
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
            *   Call `oldValue.DeactivateLogProcessing()`.
        *   **New Active Tab (if `newValue` is not null):**
            *   Call `newValue.ActivateAsync(...)` passing necessary global settings (profiles, context lines, etc.).
    *   `TabViewModel.DeactivateLogProcessing()`:
        *   Calls `LogDataProcessor.Deactivate()`.
        *   Sets `LastActivityTimestamp`.
        *   Sets `IsActive = false`.
        *   Updates `DisplayHeader`.
        *   Ensures busy tokens are cleared.
    *   `TabViewModel.ActivateAsync(...)`:
        *   Sets `IsActive = true`.
        *   Clears `LastActivityTimestamp`.
        *   (Re)Initializes `LogDataProcessor` for its `SourceType` and `SourceIdentifier`.
        *   Processor handles `LogSource` creation, `PrepareAndGetInitialLinesAsync`, `StartMonitoring`.
        *   Processor applies initial filter based on tab's `AssociatedFilterProfileName` and global context lines.
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
            *   Do *not* subscribe to `SourceRestartDetected` for simulator tabs.
        *   Add to `TabViewModels` and set as `ActiveTabViewModel`. (This triggers its `ActivateAsync`, which will internally set up `LogDataProcessor` for `ISimulatorLogSource`).
        *   Simulator controls (LPS, Error Freq, Burst) in `MainViewModel` now operate on `ActiveTabViewModel.LogSourceExposeDeprecated` (cast to `ISimulatorLogSource` if `ActiveTabViewModel.SourceType == SourceType.Simulator`).
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
            *   `initialSourceIdentifier`: A new unique ID (e.g., `Guid.NewGuid().ToString()`), used for potential persistence.
            *   Do *not* subscribe to `SourceRestartDetected` for pasted tabs.
        *   Call `newTabViewModel.LoadPastedContent(text)` to populate its `LogDataProcessor.LogDocDeprecated`.
        *   Add to `TabViewModels` and set as `ActiveTabViewModel`.
        *   The `ActiveTabViewModel.ActivateAsync(...)` for a pasted tab will then involve:
            *   Setting up its `ReactiveFilteredLogStream` (with a "null" or "completed" `ILogSource` as there are no further lines from the source itself).
            *   Triggering its filter update on the already loaded content.
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
            *   Unsubscribe from the closing tab's `SourceRestartDetected` event if applicable.
            *   Removes the `TabViewModel` from `TabViewModels`.
            *   Calls `Dispose()` on the removed `TabViewModel` (which handles `DeactivateLogProcessing` and resource cleanup via `LogDataProcessor`).
            *   Handles deletion of `PastedContentStoragePath` file if applicable (see Phase 4.1).
            *   If `TabViewModels` becomes empty, create a default empty tab.
            *   Select another tab to be active.
*   **Impact:** Users can manage their workspace.
*   **Testing:**
    *   Close tabs; they disappear.
    *   Active tab selection logic works correctly after a close.
    *   Ensure resources of closed tabs are released.

### Step 2.5: File Reset Handling in Tabs
*   **Action:** Implement the new behavior for log file restarts: inactivate the current tab (make it a snapshot) and create a new tab for the restarted file.
*   **Details:**
    *   **`TabViewModel.ActivateAsync(...)` Modification:**
        *   When `SourceType` is `File`, `TabViewModel` will pass its internal `OnSourceFileRestarted` method as the callback to `LogDataProcessor.ActivateAsync`.
    *   **`TabViewModel.OnSourceFileRestarted()` Method (defined in Step 0.1):**
        *   Invoked by the `LogTailer` -> `FileLogSource` -> `LogDataProcessor` callback chain when a file truncation is detected.
        *   Stores its original `SourceIdentifier` (file path).
        *   Updates its `Header` (e.g., `Header += $" (Snapshot @ {DateTime.Now:HH:mm:ss})" `).
        *   Calls `DeactivateLogProcessing()` (stops monitoring).
        *   Changes its `SourceType` to `SourceType.Snapshot` (or `SourceType.Pasted`).
        *   Changes its `SourceIdentifier` to a new unique ID (e.g., `Guid.NewGuid().ToString()`). This prevents it from trying to reload the original file and facilitates saving its current `LogDoc` content if it's treated like a pasted tab.
        *   Sets `IsModified = true` (if its content is to be saved like pasted tabs).
        *   Raises its `SourceRestartDetected` event, passing `this` (the snapshot tab) and the original `restartedFilePath`.
    *   **`MainViewModel` Subscribes to `SourceRestartDetected`:**
        *   When creating any `TabViewModel` with `SourceType.File`, `MainViewModel` subscribes to its `SourceRestartDetected` event.
        *   **`MainViewModel.HandleTabSourceRestart(TabViewModel snapshotTab, string restartedFilePath)`:**
            *   This method is the event handler.
            *   `snapshotTab` has already transitioned itself into a snapshot state.
            *   Create a new `TabViewModel`:
                *   `initialHeader`: `Path.GetFileName(restartedFilePath)`.
                *   `initialAssociatedProfileName`: `snapshotTab.AssociatedFilterProfileName` (or current global active profile).
                *   `initialSourceType`: `SourceType.File`.
                *   `initialSourceIdentifier`: `restartedFilePath`.
                *   Subscribe to the new tab's `SourceRestartDetected` event.
            *   Add this new `TabViewModel` to `MainViewModel.TabViewModels`.
            *   Set the new `TabViewModel` as `ActiveTabViewModel`. This will trigger its `ActivateAsync`, causing it to load and monitor the (now truncated/restarted) file from the beginning.
*   **Impact:**
    *   When a monitored file is reset, the original tab becomes an inactive snapshot of the pre-reset content.
    *   A new tab is automatically created and activated, monitoring the file from its reset state.
    *   This is per-tab, addressing the limitations of the previous global restart mechanism.
*   **Testing:**
    *   Open a file in a tab.
    *   Truncate or replace the file with shorter content.
    *   Verify:
        *   The original tab's header changes (e.g., "mylog.txt (Snapshot...)").
        *   The original tab becomes inactive and stops monitoring.
        *   A new tab (e.g., "mylog.txt") appears and becomes active.
        *   The new tab displays the content of the reset file.
        *   The snapshot tab retains the old content.

## Phase 3: Persistence

**Goal:** Save and restore tab sessions and their states.

### Step 3.1: Define `TabSessionInfo`
*   **Action:** Create `TabSessionInfo` class in `Logonaut.Common`.
*   **Details:**
    *   Properties:
        *   `string TabHeaderName` (user-defined name)
        *   `string AssociatedFilterProfileName`
        *   `DateTime? LastActivityTimestamp`
        *   `double ScrollOffset` (vertical scroll position of the `LogOutputEditor`)
        *   `int HighlightedOriginalLineNumber` (to restore selected line)
        *   `SourceType SourceType` (enum: File, Pasted, Simulator, Snapshot)
        *   `string? SourceIdentifier` (file path for File, unique ID for Pasted/Snapshot)
        *   `string? OriginalFilePathForSnapshot` (Only for `SourceType.Snapshot`, stores the original file path before it became a snapshot. Optional, for display/info purposes).
        *   `string? PastedContentStoragePath` (Path to saved LogDoc content for `SourceType.Pasted` and `SourceType.Snapshot`).
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
    *   For each `TabViewModel` that is NOT `SourceType.Simulator`:
        *   Create a `TabSessionInfo` object.
        *   Populate it from `TabViewModel` properties (Header, ProfileName, Timestamps, ScrollPosition, HighlightedLine, SourceType, SourceIdentifier).
        *   **For `SourceType.Pasted` or `SourceType.Snapshot`:**
            *   If `TabViewModel.PastedContentStoragePath` is null (or content changed, indicated by `IsModified` for `Pasted`):
                *   Generate a unique filename (e.g., using `TabViewModel.SourceIdentifier`.logonautsession).
                *   Save `TabViewModel.LogDocDeprecated.ToList()` content to this file in `AppData/Logonaut/PastedLogs/`.
                *   Store this path in `TabSessionInfo.PastedContentStoragePath`.
                *   Update `TabViewModel.PastedContentStoragePath` and set `IsModified = false`.
            *   If `TabViewModel.SourceType == SourceType.Snapshot`, also save `TabViewModel.OriginalFilePathBeforeSnapshot` to `TabSessionInfo.OriginalFilePathForSnapshot`.
        *   Add `TabSessionInfo` to `LogonautSettings.OpenTabs`.
*   **Impact:** Tab states (excluding simulator) are persisted. Snapshot tabs save their captured content.
*   **Testing:**
    *   Open various tabs (file, pasted). Trigger a file reset to create a snapshot tab.
    *   Close app; inspect `settings.json` and `PastedLogs` folder.
    *   Verify `TabSessionInfo` (with correct `SourceType` and paths) and snapshot/pasted content files are created correctly.

### Step 3.4: Restore Tab State
*   **Action:** Implement restoring of tabs in `MainViewModel` constructor (or a `LoadPersistedState` method called after settings are loaded).
*   **Details:**
    *   After loading `LogonautSettings`:
        *   Iterate `settings.OpenTabs`.
        *   For each `TabSessionInfo`:
            *   Create a new `TabViewModel`.
            *   Set `Header`, `AssociatedFilterProfileName`, `LastActivityTimestamp`, `SourceType`, `SourceIdentifier`.
            *   Subscribe to `SourceRestartDetected` if `SourceType == SourceType.File`.
            *   If `SourceType == SourceType.Pasted` or `SourceType == SourceType.Snapshot`:
                *   If `PastedContentStoragePath` exists, load content from this file into `TabViewModel.LogDataProcessor.LogDocDeprecated` via `TabViewModel.LoadPastedContent()` or a similar direct load mechanism for snapshots.
                *   Set `TabViewModel.PastedContentStoragePath` to the loaded path.
            *   Store `ScrollOffset` and `HighlightedOriginalLineNumber` in `TabViewModel` to be applied when the tab view is ready.
            *   Add to `MainViewModel.TabViewModels`.
        *   All restored tabs are initially inactive (no `ActivateAsync` called yet).
        *   If no tabs were restored, create a default empty tab.
        *   Select the first tab (or a last active remembered tab if stored) as `ActiveTabViewModel`. This will trigger its activation logic (Step 2.1).
*   **Impact:** Application restores its previous tab layout on startup, including snapshot tabs with their content.
*   **Testing:**
    *   Start app; previously open tabs (file, pasted, snapshot) are restored.
    *   Headers, profiles, SourceTypes should be correct.
    *   Pasted/Snapshot content is restored.
    *   Initially, no tab is actively monitoring. Activating one starts monitoring (for File/Simulator tabs). Snapshot/Pasted tabs will just display their content.

### Step 3.5: Restore Scroll Position and Selected Line
*   **Action:** When a tab becomes active (or its view is loaded), apply stored scroll and selection.
*   **Details:**
    *   `TabViewModel` will need to store `RestoredScrollOffset` and `RestoredHighlightedOriginalLineNumber` (populated during Step 3.4).
    *   When a `TabViewModel`'s view content is loaded (e.g., its `LogOutputEditor` is ready, possibly after `ActivateAsync` has populated `FilteredLogLines`):
        *   If `RestoredScrollOffset` is valid, scroll the editor.
        *   Set `HighlightedOriginalLineNumber` to `RestoredHighlightedOriginalLineNumber`. This will trigger highlighting and potentially scrolling via existing mechanisms.
    *   This might require an event from the View (e.g., `LogOutputEditor.Loaded` for that specific tab's editor, or a flag in `TabViewModel` checked after `ActivateAsync`) to trigger this restoration.
*   **Impact:** Better UX, restores user's context within each tab.
*   **Testing:**
    *   Scroll and select lines in tabs. Close and reopen app.
    *   Verify scroll position and selection are restored for each tab when it's viewed/activated.

## Phase 4: Pasted Content Management & Filter Profile Interaction

**Goal:** Refine pasted content handling and profile interactions across tabs.

### Step 4.1: Deleting Saved Pasted Content
*   **Action:** When a `TabViewModel` representing pasted or snapshot content is closed, delete its associated temp storage file.
*   **Details:**
    *   In `MainViewModel`'s logic for handling tab closure:
        *   If the closing `TabViewModel.SourceType` is `Pasted` or `Snapshot`, and `TabViewModel.PastedContentStoragePath` is not null:
            *   Delete the file at `PastedContentStoragePath`.
*   **Impact:** Prevents orphaned pasted/snapshot content files in AppData.
*   **Testing:**
    *   Create a pasted content tab / snapshot tab. Close app (content saved). Reopen (content restored).
    *   Close the pasted/snapshot content tab from the UI. Verify the temp file is deleted.

### Step 4.2: Profile Change Applies to Active Tab Only
*   **Action:** Ensure changing the selected profile in `MainViewModel`'s UI only affects the `ActiveTabViewModel`.
*   **Details:**
    *   When `MainViewModel.ActiveFilterProfile` (the ComboBox selection) changes:
        *   If `MainViewModel.ActiveTabViewModel` is not null:
            *   Set `ActiveTabViewModel.AssociatedFilterProfileName` to the new profile's name.
            *   This change on `ActiveTabViewModel` should trigger it to re-evaluate its filters via its `TriggerFilterUpdate` method (or by calling `ActivateAsync` again, which re-applies filters).
*   **Impact:** Profile selection is now correctly scoped to the active tab.
*   **Testing:**
    *   Have multiple tabs open with different profiles.
    *   Select Tab A. Change global profile to Profile X. Verify Tab A now uses Profile X.
    *   Select Tab B (which was using Profile Y). Verify Tab B is still using Profile Y. Change global profile to Profile Z. Verify Tab B now uses Profile Z, and Tab A is still on X.

### Step 4.3: Filter Settings Change in a Profile Affects All Tabs Using It (On Activation or explicit refresh)
*   **Action:** When a filter within a `FilterProfileViewModel` is modified (e.g., text change, enabled toggle), this change is saved to the model (`FilterProfile`). The active tab using this profile should update immediately. Inactive tabs using this profile will see the changes when they are next activated.
*   **Details:**
    *   Current undo/redo system in `MainViewModel` already modifies the `FilterProfile` model directly or via `FilterViewModel` wrappers.
    *   When `ActiveFilterProfile`'s underlying model changes, `MainViewModel` should call `ActiveTabViewModel?.TriggerFilterUpdate(...)`.
    *   When an inactive `TabViewModel` becomes active (Step 2.1, via `ActivateAsync`):
        *   It re-applies its filter. It will fetch its `AssociatedFilterProfileName` and get the (potentially updated) `FilterProfile` model from `MainViewModel.AvailableProfiles`.
        *   The `LogDataProcessor` for that tab will then use the latest definition from this profile.
*   **Impact:** Filter changes are consistently applied. Active tab updates; inactive tabs update on activation.
*   **Testing:**
    *   Tab A uses Profile P1. Tab B uses Profile P1. Tab C uses Profile P2.
    *   Activate Tab A. Modify a filter in P1. Verify Tab A updates.
    *   Activate Tab B. Verify Tab B's view reflects the modified P1.
    *   Activate Tab C. Verify it's still using P2. Modify P2. Verify Tab C updates.

## Phase 5: Polish, UX Enhancements, and Future Ideas

**Goal:** Add smaller UX improvements and consider future directions.

### Step 5.1: Tab Context Menu
*   **Action:** Implement a context menu for tabs.
*   **Details:**
    *   Add `ContextMenu` to `TabItem.Header` `DataTemplate`.
    *   Bind `MenuItem` commands to commands in `TabViewModel` or `MainViewModel`.
    *   Commands: Close, Close Others, Reload (for file tabs, if snapshot behavior isn't always desired), Copy Path/Identifier.
*   **Impact:** Improved tab management.
*   **Testing:** Verify context menu commands work as expected.

### Step 5.2: Visual Indication for File Changes in Inactive Tabs
*   **Action:** (Future Idea) Add a small visual cue (e.g., a dot on the tab header) if an inactive file-based tab's underlying file has changed on disk since it was last active.
*   **Details:**
    *   This would require `TabViewModel`s for inactive file tabs to periodically (or via `FileSystemWatcher`, carefully managed to avoid resource overuse) check `LastWriteTime` of their file.
    *   If changed, set a boolean flag `HasUnseenChanges` on `TabViewModel`.
    *   Bind this flag to a visual element in the tab header template.
    *   When tab is activated, flag is cleared, and `ActivateAsync` (which calls `PrepareAndGetInitialLinesAsync`) needs to handle new content (e.g., by reading the whole file again, or more complex diffing/tailing from last known good position. The file reset mechanism might cover some of this if the change is a truncation).
*   **Impact:** Users are aware of updates in background tabs.
*   **Testing:** Modify a file for an inactive tab; indicator appears. Activate tab; content updates and indicator clears.

### Step 5.3: "Pinning" Tabs / Color-Coding Tabs
*   **Action:** (Future Ideas)
    *   **Pinning:** Add `IsPinned` to `TabViewModel` and `TabSessionInfo`. Pinned tabs could appear first, or have a different close behavior.
    *   **Color-Coding:** Add `TabColor` (Brush or string key) to `TabViewModel` and `TabSessionInfo`. Allow user to pick a color via context menu. Tab header background uses this color.
*   **Impact:** Enhanced organization.
*   **Testing:** Visual verification and persistence of these states.
