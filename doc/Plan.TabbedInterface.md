# Logonaut Tabbed Interface Implementation Plan

This document outlines the incremental steps to transition Logonaut from a single-view application to a full multi-tab interface. **This version has been updated to reflect the current implementation status.**

## Phase 0: Foundation & Refactoring (Internal Changes)

*   **Status:** **[COMPLETED]**
*   **Summary:** The foundational refactoring is complete. The codebase includes `TabViewModel` and `LogDataProcessor`. The `MainViewModel` has been adapted to manage a collection of tabs. The mechanism for a file source to become a "snapshot" and notify its host via the `SourceRestartDetected` event is fully implemented in `TabViewModel`.

---

## Phase 1: Basic Tabbed UI and Multiple Tab Management

### Step 1.1: Create `TabView` UserControl and Integrate `TabControl`

*   **Status:** **[COMPLETED]**
*   **Summary:** The `TabView.xaml` UserControl has been created. The `MainWindow.xaml` has been updated with a `TabControl` correctly bound to the `TabViewModels` collection and `ActiveTabViewModel` property in the `MainViewModel`, successfully rendering the tabbed UI.

### Step 1.2: Opening New File-Based Tabs

*   **Status:** **[COMPLETED]**
*   **Summary:** The `OpenLogFile` command logic has been successfully refactored. It now correctly creates a new `TabViewModel` for each new file, adds it to the collection, and sets it as the active tab. It also correctly focuses an existing tab if the same file is opened again.

### Step 1.3: Tab Renaming

*   **Status:** **[COMPLETED]**
*   **Summary:** Inline tab renaming is fully implemented. The `TabViewModel` contains the necessary properties and commands (`IsEditingHeader`, `BeginEditHeaderCommand`, etc.), and the UI correctly switches between a `TextBlock` and a `TextBox` for editing.

---

## Phase 2: Active/Inactive Tab Logic & Simulator/Pasted Tabs

### Step 2.1: Implement Active/Inactive Tab Logic

*   **Status:** **[PARTIALLY IMPLEMENTED]**
*   **Plan:** Implement robust activation/deactivation logic in `MainViewModel`'s `OnActiveTabViewModelChanged` method. When a tab becomes inactive, its background monitoring (`LogSource`) should be stopped. When a tab becomes active, its `LogSource` should be started.
*   **Current State:** The `OnActiveTabViewModelChanged` method correctly sets the `IsActive` flag on the `TabViewModel`, which updates the UI (e.g., hiding/showing the `TabView`). However, it **does not** call `DeactivateLogProcessing()` on the old tab or `ActivateAsync()` on the new tab. This means that currently, **all file-based tabs continue to monitor their files in the background**, even when they are not visible.

### Step 2.2: Implement Simulator Tabs

*   **Status:** **[PARTIALLY IMPLEMENTED]**
*   **Plan:** Adapt the simulator logic to create and manage a dedicated, new simulator tab when the "Start Simulator" command is invoked.
*   **Current State:** The implementation does not create a new tab. Instead, it **re-purposes the currently active tab**, changing its `SourceType` to `Simulator` and activating the `SimulatorLogSource` within it.

### Step 2.3: Implement Pasted Content Tabs

*   **Status:** **[COMPLETED]**
*   **Summary:** The paste logic has been successfully refactored. Pasting content now correctly creates a new `TabViewModel` with `SourceType.Pasted`, adds it to the collection, and activates it, showing the pasted content.

### Step 2.4: Implement Close Tab Functionality

*   **Status:** **[COMPLETED]**
*   **Summary:** The ability to close tabs is fully implemented. The 'X' button on the tab header correctly invokes a command that removes the `TabViewModel` from the `MainViewModel`, disposes its resources, and selects a new active tab. Closing the last tab correctly creates a new, empty tab.

### Step 2.5: File Reset Handling in Tabs

*   **Status:** **[IN PROGRESS - CRITICAL LOGIC MISSING]**
*   **Plan:** When a monitored log file is reset, the `MainViewModel.HandleTabSourceRestart` method should be called. This handler is responsible for creating a *new* tab to monitor the restarted file, while leaving the original tab as an inactive snapshot.
*   **Current State:** The detection and snapshot creation work correctly. `LogTailer` detects the reset, and `TabViewModel.HandleSourceFileRestarted` successfully transitions the tab to a `Snapshot` state and raises the `SourceRestartDetected` event. However, the `MainViewModel.HandleTabSourceRestart` method that receives this event is **currently a placeholder and contains no logic to create the new tab**. This is the source of the file reset issue.

### Step 2.6: Unique filters for each tab

*   **Status:** **[PARTIALLY IMPLEMENTED]**
*   **Plan:** Each tab should have its own associated filter profile. Switching tabs should restore the view for that tab's specific profile.
*   **Current State:** When a new tab is created, it correctly inherits the filter profile that was active at the time of its creation. However, the system does not maintain a persistent, unique profile for each tab. Instead, when you switch to a tab, its filter configuration is **overwritten by the currently selected global filter profile**.

---

## Phase 3: Persistence

*   **Status:** **[NOT STARTED]**
*   **Action:** Save and restore tab sessions (including file paths, pasted content, and associated filter profiles) across application restarts.
*   **Details:** This will involve creating a serializable `TabSessionInfo` class, adding a list of these to `LogonautSettings`, and implementing the save/restore logic in `MainViewModel`. Content from pasted/snapshot tabs will need to be saved to temporary files.

---

## Phase 4 & 5: Polish, UX Enhancements, and Future Ideas

*   **Status:** **[NOT STARTED]**
*   **Actions:** These phases include future work such as refining filter profile interactions, adding a tab context menu, showing visual indicators for changes in inactive tabs, and tab "pinning".
