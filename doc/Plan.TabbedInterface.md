# Logonaut Tabbed Interface Implementation Plan (Updated)

This plan outlines the incremental steps to transition Logonaut from a single-view application to a full multi-tab interface.

**Analysis Summary:** Phase 0 (Foundation & Refactoring) is **complete** based on the current source code. The introduction of `TabViewModel` and `LogDataProcessor`, along with the logic for a file source to become a "snapshot" and notify its host, has been successfully implemented. The next steps involve building the UI and application logic to manage a collection of these tabs.

## Phase 0: Foundation & Refactoring (Internal Changes)

**Goal:** Prepare the `MainViewModel` and core components to support multiple log contexts without immediately showing a tabbed UI.

*   **Status:** **[COMPLETED]**
*   **Evidence:** The codebase already contains `TabViewModel` and `LogDataProcessor`. `MainViewModel` instantiates and uses a single `_internalTabViewModel`, delegating many properties and commands to it. The file reset/snapshot mechanism is fully implemented.

### Step 0.1: `TabViewModel` Enhancements (File Reset Logic)

*   **Status:** **[COMPLETED]**
*   **Details:** `TabViewModel.cs` contains the `HandleSourceFileRestarted()` method, which is passed as a callback to `LogDataProcessor` and invoked by `FileLogSource`. This method correctly transitions the `TabViewModel` into a `Snapshot` source type, updates its header, and raises the `SourceRestartDetected` event.

### Step 0.2: `MainViewModel` Responding to `TabViewModel`'s `SourceRestartDetected` (Single-Tab Context)

*   **Status:** **[COMPLETED]**
*   **Details:** `MainViewModel.cs` subscribes to `_internalTabViewModel.SourceRestartDetected` and its handler, `HandleInternalTabSourceRestart`, correctly calls `LoadLogFileCoreAsync` to reload the file in the existing internal tab.

---

## Phase 1: Basic Tabbed UI and Multiple Tab Management

**Goal:** Introduce the `TabControl` UI and allow opening multiple file-based tabs.

### Step 1.1: Create `TabView` UserControl and Integrate `TabControl`

*   **Action:** Create a `Logonaut.UI/Views/TabView.xaml` UserControl. Modify `MainWindow.xaml` to replace the direct log display area with a `TabControl`.
*   **Details:**
    *   **`TabView.xaml`:** This new UserControl will contain the layout for a single tab's content (the `LogOutputEditor`, its toolbar, search panel, overview ruler, loading overlay, etc.). Its `DataContext` will be a `TabViewModel`. Bindings within this view will be to properties of the `TabViewModel`.
    *   **`MainWindow.xaml`:** The right-hand panel (where `LogOutputEditor` currently is) will be replaced by a `TabControl`.
    *   **`MainViewModel.cs`:** Replace the single `_internalTabViewModel` field with an `ObservableCollection<TabViewModel> TabViewModels`. Also, add an `ActiveTabViewModel` property.
    *   **Binding:**
        *   Bind `TabControl.ItemsSource` to `TabViewModels`.
        *   Bind `TabControl.SelectedItem` (TwoWay) to `ActiveTabViewModel`.
        *   Create a `DataTemplate` for `TabItem.Header` to display `TabViewModel.DisplayHeader` and a close button.
        *   Create a `TabControl.ContentTemplate` that contains an instance of the new `<views:TabView />`.

### Step 1.2: Opening New File-Based Tabs

*   **Action:** Modify "Open Log File" logic in `MainViewModel` to create a new `TabViewModel` for each new file, rather than reusing the single internal one.
*   **Details:**
    *   In `MainViewModel.OpenLogFileAsync`:
        *   **Check for existing tab:** Iterate `TabViewModels`. If a tab with the same `SourceIdentifier` (file path) and `SourceType.File` exists, set it as `ActiveTabViewModel` and return.
        *   **Create new tab:** If the file is not already open, create a new `TabViewModel` instance, populating its initial properties (header, profile name, source type, identifier).
        *   Subscribe to its `SourceRestartDetected` event.
        *   Add the new `TabViewModel` to the `TabViewModels` collection.
        *   Set the new `TabViewModel` as the `ActiveTabViewModel`.

### Step 1.3: Tab Renaming/Notes

*   **Action:** Allow users to rename tab headers for better organization.
*   **Details:**
    *   Add an `IsEditingHeader` property to `TabViewModel`.
    *   Modify the `TabItem.Header` `DataTemplate` to include both a `TextBlock` (for display) and a `TextBox` (for editing), with their visibility bound to `IsEditingHeader`.
    *   Implement a "Rename" command or a double-click behavior on the tab header to toggle `IsEditingHeader`.
    *   `TabViewModel.Header`'s setter will update the underlying model and the `DisplayHeader`.

---

## Phase 2: Active/Inactive Tab Logic & Simulator/Pasted Tabs

**Goal:** Properly manage resources for active vs. inactive tabs and integrate simulator and pasted content into the new tabbed structure.

### Step 2.1: Implement Active/Inactive Tab Logic

*   **Action:** Implement robust activation/deactivation logic in `MainViewModel` that fires when `ActiveTabViewModel` changes.
*   **Details:**
    *   Create an `OnActiveTabViewModelChanged` method in `MainViewModel`.
    *   When the active tab changes from `oldValue` to `newValue`:
        *   If `oldValue` is not null, call `oldValue.DeactivateLogProcessing()`. This will stop its `LogSource` monitoring and set its `LastActivityTimestamp`.
        *   If `newValue` is not null, call `newValue.ActivateAsync(...)`. This will initialize and start its `LogSource` and apply its associated filters.
    *   This ensures only the visible tab consumes resources for live monitoring.

### Step 2.2: Implement Simulator Tabs

*   **Action:** Adapt the existing simulator logic in `MainViewModel` to create and manage a dedicated simulator tab.
*   **Details:**
    *   When the "Start Simulator" command is invoked, create a new `TabViewModel` with `SourceType.Simulator`.
    *   Add it to the `TabViewModels` collection and set it as the `ActiveTabViewModel`.
    *   The `ActivateAsync` method of this new tab will correctly set up its `LogDataProcessor` to use an `ISimulatorLogSource`.
    *   The simulator UI controls (LPS, Error Freq, etc.) in `MainViewModel` will now operate on the `ActiveTabViewModel`'s log source (after casting to `ISimulatorLogSource`).

### Step 2.3: Implement Pasted Content Tabs

*   **Action:** Refactor the `Ctrl+V` (paste) logic to create a new tab for the pasted content.
*   **Details:**
    *   When `MainViewModel.LoadLogFromText(string text)` is called:
        *   Create a new `TabViewModel` with `SourceType.Pasted` and a unique `SourceIdentifier` (e.g., a GUID).
        *   Call a method on the new `TabViewModel` (e.g., `LoadPastedContent(text)`) to populate its `LogDataProcessor`'s internal `LogDocument`.
        *   Add the tab to `TabViewModels` and make it active.
        *   Its `ActivateAsync` method will then apply the current filters to the already-loaded content.

### Step 2.4: Implement Close Tab Functionality

*   **Action:** Add the ability for users to close tabs.
*   **Details:**
    *   Add a `CloseTabCommand` to `TabViewModel`. The 'X' button in the `TabItem.Header` template will bind to this command.
    *   The command will raise an event or call a method on `MainViewModel`, requesting its own closure.
    *   `MainViewModel` will handle this request by:
        1.  Unsubscribing from any events on the closing tab.
        2.  Calling `Dispose()` on the `TabViewModel` to release its resources.
        3.  Removing the `TabViewModel` from the `TabViewModels` collection.
        4.  Selecting a new `ActiveTabViewModel` (e.g., the previous one, or the next one).
        5.  If the last tab is closed, create a new, empty default tab.

### Step 2.5: File Reset Handling in Tabs

*   **Action:** Implement the multi-tab behavior for log file restarts: inactivate the current tab (making it a snapshot) and create a new tab for the restarted file.
*   **Details:**
    *   This step now primarily involves the `MainViewModel.HandleTabSourceRestart` method. The `TabViewModel` already handles transitioning itself into a snapshot and raising the `SourceRestartDetected` event (**COMPLETED**).
    *   Modify `MainViewModel.HandleTabSourceRestart(TabViewModel snapshotTab, string restartedFilePath)`:
        *   This handler will no longer re-purpose the existing tab.
        *   Instead, it will create a *new* `TabViewModel` configured to monitor the `restartedFilePath`.
        *   It will add this new tab to the `TabViewModels` collection and set it as the `ActiveTabViewModel`.
        *   The original tab (`snapshotTab`) remains in the collection as an inactive snapshot of the pre-reset content.

---

## Phase 3: Persistence

**Goal:** Save and restore tab sessions and their states across application restarts.

### Step 3.1: Define `TabSessionInfo`

*   **Action:** Create a `TabSessionInfo` class in `Logonaut.Common` to hold the serializable state of a tab.
*   **Details:** Properties should include: `Header`, `AssociatedFilterProfileName`, `SourceType`, `SourceIdentifier`, `PastedContentStoragePath` (for pasted/snapshot tabs), scroll position, etc.

### Step 3.2: Modify `LogonautSettings`

*   **Action:** Add a `List<TabSessionInfo> OpenTabs` property to the `LogonautSettings` class.

### Step 3.3: Implement Save Tab State

*   **Action:** In `MainViewModel.SaveCurrentSettings`, iterate through the `TabViewModels` collection and persist their state.
*   **Details:**
    *   For each `TabViewModel` (excluding simulator tabs), create a `TabSessionInfo` object.
    *   For `Pasted` or `Snapshot` tabs, save the content of their `LogDocument` to a unique file in a local app data folder (e.g., `AppData/Logonaut/PastedLogs/`). Store this file path in `TabSessionInfo.PastedContentStoragePath`.
    *   Populate the `LogonautSettings.OpenTabs` list with these `TabSessionInfo` objects before saving.

### Step 3.4: Implement Restore Tab State

*   **Action:** In `MainViewModel`'s startup logic (after settings are loaded), restore the saved tabs.
*   **Details:**
    *   Iterate through the loaded `LogonautSettings.OpenTabs` list.
    *   For each `TabSessionInfo`, create a corresponding `TabViewModel`.
    *   If it's a `Pasted` or `Snapshot` tab, load its content from the file specified in `PastedContentStoragePath`.
    *   Add the restored `TabViewModel` to the `TabViewModels` collection. All restored tabs should start as inactive.
    *   Select a tab to be active (e.g., the first one, or a saved "last active" tab).

---

## Phase 4 & 5: Polish, UX Enhancements, and Future Ideas

These phases remain as future work and their plans are still valid. They include:
*   Refining how filter profile changes affect tabs.
*   Deleting saved pasted content when a tab is closed.
*   Adding a tab context menu (Close, Close Others, etc.).
*   Visual indicators for changes in inactive tabs.
*   Tab "pinning" or color-coding for better organization.
