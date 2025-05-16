# Logonaut User Requirements

This document outlines the functional requirements for the Logonaut application from a user's perspective.

The requirement ID is a unique ID and a version number that can be referenced from the source code and other documents.
The version number, a suffix with 'V<N>' is incremented when a requirement is updated.

## 1. Core Log Viewing & Tab Management

*   [ReqFileMonitorLiveUpdateV2] **File Monitoring & Tabs:** The application must allow the user to select a log file via a menu (`File > Open Log File` or `Ctrl+O`). Each opened file will appear in a new **tab**. The application will continuously monitor the file associated with the **active (selected) tab** for changes, updating its display in real-time. If a file is already open in a tab, selecting it via "Open Log File" will activate the existing tab.
*   [ReqFilterEfficientRealTimeV1] Filtering must efficiently handle real-time updates from monitored files or pasted content, applying filters to new data incrementally where possible to maintain UI responsiveness even with large log sources.
*   [ReqPasteFromClipboardv3] **Paste Input & Tabs:** The user must be able to paste log content directly from the clipboard (using `Ctrl+V` when the log view has focus). This action will create a **new tab** for the pasted content.
*   [ReqLargeFileResponsiveV1] **Large File Handling:** The application must remain responsive and usable even when viewing very large log files. Background processing should be used for filtering and loading.
*   [ReqStandardScrollingV1] **Scrolling:** Standard vertical scrolling must be supported (mouse wheel, keyboard: PgUp/PgDn, Arrows, Home/End) for the content within the active tab.
*   [ReqOverviewScrollbarMapV1] **Overview Scrollbar:** A custom overview scrollbar must provide visual document mapping and scroll control for the content within the active tab.
*   [ReqFileResetHandlingV1] **File Reset Handling:** If the monitored log file is reset (e.g., truncated), the application should ideally detect this, potentially clear the display for that tab, and continue monitoring from the beginning of the reset file. *(Current implementation might require reopening)*.
*   [ReqFileUnavailableHandlingV1] **File Unavailability:** If the log file associated with an active tab becomes unavailable (e.g., deleted, network drive disconnects), the application should handle this gracefully (e.g., stop tailing, display a message in that tab) without crashing.
*   [ReqRememberLastFolderV1] **Remember Last Folder:** The "Open Log File" dialog should remember the folder from the last opened file and open there by default.
*   [ReqTabManagementV1] **Tab Management:**
    *   [ReqTabActivateSwitchV1] The user must be able to switch between open tabs, making the selected tab active.
    *   [ReqTabActiveMonitoringV1] Only the currently active tab will monitor its source for new activity (if applicable, e.g., file tailing or simulator running).
    *   [ReqTabInactiveStateV1] Inactive tabs will become passive, preserving their content and filter state. They will display a timestamp of their last activity.
    *   [ReqTabCloseV1] The user must be able to close individual tabs (e.g., via an 'X' button on the tab header).
    *   [ReqTabRenameV1] The user must be able to rename tabs. The tab header should display this user-defined name.
*   [ReqPersistTabsV1] **Tab Session Persistence:**
    *   [ReqTabSaveSessionV1] The set of open tabs (excluding simulator tabs), their source (file path or path to saved pasted content), associated filter profile, user-defined tab name, last activity timestamp, scroll position, and highlighted line must be saved at shutdown.
    *   [ReqTabRestoreSessionV1] Upon restart, the application must restore the previously open tabs. All restored tabs will initially be inactive.
    *   [ReqTabSavePastedContentV1] Content from tabs created by pasting, which are not associated with a physical log file, must be saved to a local application data folder when the application closes. These saved content files will be used to restore the tab on next launch.
    *   [ReqTabDeleteSavedPastedV1] When a tab representing saved pasted content is closed by the user, its associated saved content file in the local application data folder must be deleted.
*   [ReqSimulatorTabBehaviorV1] **Simulator Tab Behavior:** Tabs created for the log simulator will be marked as such (e.g., "Simulator" in header), will not have their content or state saved, and will not be restored on application restart.

## 2. Display and Appearance (Per Active Tab)

*   [ReqDisplayRealTimeUpdateV1] **Real-Time Updates:** The log display area for the active tab must update automatically as new relevant log entries arrive or when filtering/settings change for that tab.
*   [ReqDisplayOriginalLineNumbersV1] **Original Line Numbers:** Line numbers displayed next to log entries must correspond to their **original line number** in the source file, regardless of filtering.
*   [ReqToggleLineNumbersV1] **Toggleable Line Numbers:** The original line number margin must be toggleable (`Line Numbers` checkbox). This setting is global but affects the display of the active tab.
*   [ReqHighlightTimestampsV1] **Timestamp Highlighting:** The application must provide an option (`Highlight Timestamps` checkbox) to automatically detect and visually distinguish common timestamp patterns at the beginning of lines. This setting is global but affects the display of the active tab.
*   [ReqHighlightSelectedLineV1] **Selected Line Highlighting:** The background of the log line currently selected by the user (via mouse click) in the active tab must be visually highlighted.
*   [ReqAdjustFontSizeV1] **Readability Customization:** The user must be able to adjust the font size for the log display. This setting is global but affects the display of the active tab.
*   [ReqSelectableThemesV1] **Theming Selection:** Color schemes must be adjustable via selectable themes.
*   [ReqThemeOptionsLightDarkV1] **Theme Options:** The application must provide at least a **Light ("Clinical Neon")** and a **Dark ("Neon Night")** theme option, selectable via the `Theme` menu.
*   [ReqThemedTitleBarV1] **Theme Title Bar:** The application should attempt to adapt the window's title bar to the selected theme on compatible Windows versions.
*   [ReqResizableWindowV1] **Window Resizing:** The application window must be resizable.
*   [ReqSplitPanelLayoutV1] **Panel Layout:** The main view must consist of **two primary panels** (Filters on left, Log View/Search on right) separated by a draggable splitter. The Log View panel will now host the `TabControl`.
*   [ReqStatusBarDisplayV1] **Status Bar Display:** The application must display a status bar.
*   [ReqStatusBarTotalLinesV2] **Status Bar - Total Lines (Active Tab):** The status bar must show the **total number of lines** read from the source log for the **active tab**, updated dynamically.
*   [ReqStatusBarFilteredLinesV2] **Status Bar - Filtered Lines (Active Tab):** The status bar must show the **number of lines currently visible** after filtering for the **active tab**, updated dynamically.
*   [ReqStatusBarSelectedLineV2] **Status Bar - Selected Line (Active Tab):** The status bar must show the **original line number** of the currently selected log line in the **active tab** (or '-' if none selected), updated dynamically.
*   [ReqGeneralBusyIndicatorV1] **Busy Indicator - General:** A general **busy indicator** (e.g., spinning icon) must be visible during background operations like filtering.
*   [ReqLoadingOverlayIndicatorV1] **Busy Indicator - Loading Overlay:** A distinct **overlay animation** (e.g., subtle scanlines) must appear directly over the log display area of a tab during its initial file loading phase.

## 3. Filtering System

*   **Filter Profiles:**
    *   [ReqCreateMultipleFilterProfilesV1] The user must be able to create multiple, distinct filter configurations, referred to as "Filter Profiles".
    *   [ReqFilterProfileNamingV1] Each profile must have a user-defined name.
    *   [ReqFilterProfileRenameInlinev2] Profile renaming must be possible **inline** within the profile selection area (e.g., by clicking a Rename button that makes the name editable).
    *   [ReqFilterProfileSelectActiveGlobalV1] The user must be able to select which profile is currently **globally active** using a `ComboBox`. This profile will be applied to the **currently active tab** or as the default for new tabs.
    *   [ReqFilterProfilePerTabV1] Each **tab** will have its own **associated filter profile**. Changing the globally selected profile will update the profile for the *currently active tab*.
    *   [ReqFilterProfileManageCRUDV1] The application must provide controls (`New`, `Rename`, `Delete` buttons) to manage filter profiles.
*   **Filter Rules (within a profile):**
    *   [ReqFilterRuleSubstringV1] Within the active profile, the user must be able to define filter rules based on **exact substring** matching.
    *   [ReqFilterRuleRegexV1] Within the active profile, the user must be able to define filter rules based on **regular expression** pattern matching.
    *   [ReqFilterRuleCombineLogicalV1] The user must be able to combine these rules using logical **AND**, **OR**, and **NOR** operators.
    *   [ReqFilterRuleTreeStructureV1] These rules must be manageable in a **hierarchical tree structure** displayed in the filter panel.
    *   ~~[ReqFilterNodeManageButtonsV1] Adding, removing, and editing filter nodes must be done via dedicated buttons operating on the selected node in the tree.~~
        *   *[OBSOLETE: Replaced by ReqDnDFilterManageV1. Button-based management removed in favor of Drag and Drop.]*
    *   [ReqFilterNodeEditInlinev2] Editing filter values (substrings/regex) must happen **inline** within the tree view (e.g., by clicking on the filter text or an Edit button).
    *   [ReqDnDFilterManageV1] Filter node management must be performed using Drag and Drop operations, including adding new nodes from a palette, moving/re-parenting existing nodes within the tree, and deleting nodes by dragging to a designated trash target.
    *   [ReqFilterSubstringDefaultFromSelectionV2] There shall be a special substring filter in the palette that dynamically contains selected text from the log view of the active tab.
*   **Filtering Behavior:**
    *   [ReqFilterDisplayMatchingLinesV2] The log display for each tab must show only the lines matching the rules of **its associated** filter profile.
    *   [ReqFilterContextLinesV1] The application must provide a setting (`Context Lines` input with increment/decrement buttons) to include a specified number of **context lines** before and after each matching line. This is a global setting applied to the active tab's filtering.
    *   [ReqFilterHighlightMatchesV1] Text segments that cause a line to match a filter rule (substrings or regex matches) must be visually highlighted in the output (distinct background color).
    *   [ReqFilterHighlightPerRuleColorV1] User-Configurable Per-Filter Highlighting Color: For individual Substring and Regex filters, the user must be able to select a distinct highlight color from a predefined, theme-aware palette (e.g., Default, Red, Green, Blue, Yellow) using a `ComboBox` next to the filter rule in the tree. The selected color should apply to both the filter match highlighting in the log view and the visual representation of the color choice in the `ComboBox` itself.
    *   [ReqFilterNodeToggleEnableV1] Individual filter rules (nodes) within the active profile's tree must be toggleable (enabled/disabled) via a `CheckBox` next to the rule, without removing them.
*   [ReqFilterDynamicUpdateViewV3] Dynamic Filter Updates: The filtered log view of the **active tab** must update automatically and efficiently whenever its associated filter profile is changed or the rules/settings (including highlight color) within that profile are modified. Inactive tabs will apply updated profile settings upon their next activation.
*   **Persistence:**
    *   [ReqPersistFilterProfilesV3] All created filter profiles (names, structures, and individual filter highlight color settings) must be saved when the application closes and reloaded on startup.
    *   [ReqPersistLastActiveProfileV1] The application must remember and automatically select the last globally active profile upon restarting.

## 4. Search Functionality (Per Active Tab)

*   [ReqSearchTextEntryV2] **Text Search (Active Tab):** The user must be able to enter text (`Ctrl+F` to focus) to search for within the currently displayed (filtered) log content of the **active tab**.
*   [ReqSearchHighlightResultsV2] **Highlighting Search Results (Active Tab):** All occurrences of the search term must be visually highlighted in the log display of the **active tab** (distinct from filter highlighting).
*   [ReqSearchRulerMarkersV2] **Overview Ruler Search Markers (Active Tab):** Search results for the **active tab** must be marked on its overview scrollbar.
*   [ReqSearchNavigateResultsV2] **Search Navigation (Active Tab):** Buttons (`Previous`/`Next`) and keyboard shortcuts (`F3` for Next, `Shift+F3` for Previous) must be provided to navigate between search results within the **active tab**.
*   [ReqSearchSelectedTextShortcutV2] **Search Selected Text:** The user must be able to press `Ctrl+F3` to automatically search for the text currently selected in the log display of the active tab.
*   [ReqSearchStatusIndicatorV2] **Search Status (Active Tab):** A status indicator must display the number of matches found and the current match index for the **active tab** (e.g., "Match 3 of 15", "Phrase not found").
*   [ReqSearchCaseSensitiveOptionV1] **Case Sensitivity Option:** An option (`Case Sensitive` checkbox) must be provided to toggle case sensitivity for the search. This is a global setting applied to searches in the active tab.

## 5. User Interaction

*   [ReqFilterControlsPanelV1] **Filter Controls Panel:** A dedicated panel must provide intuitive controls for profile and filter rule management as described in section 3.
*   [ReqLineSelectionClickV2] **Line Selection (Active Tab):** Clicking on a line number or log entry in the display area of the **active tab** must select that line, highlighting it and updating the selected line number in the status bar.
*   [ReqGoToLineInputBoxV2] **Go To Line Input (Active Tab):** The user must be able to enter an original line number into a dedicated text box (`Ctrl+G` to focus) in the status bar, relevant to the **active tab**.
*   [ReqGoToLineExecuteJumpV2] **GoTo Line Execution (Active Tab):** Pressing Enter (or clicking a button) must jump to (select and scroll to) the entered line number if it exists in the *filtered* view of the **active tab**.
*   [ReqGoToLineFeedbackNotFoundv2] **Go To Line Feedback:** Feedback (e.g., a temporary status message and/or visual cue like background color change) must indicate if the entered line number is not found in the filtered view of the active tab or if the input is invalid.
*   **Auto Scroll:**
    *   [ReqAutoScrollOptionv3] **Auto Scroll Control:** An **anchor toggle button** in the status bar must be available to enable/disable automatically scrolling to the end of the log view of the **active tab** when new lines are added. The button's visual state (e.g., icon color/style) must clearly indicate whether auto-scroll is enabled or disabled. When enabled and filter changes occur for the active tab, its view should scroll to the new end.
    *   [ReqAutoScrollDisableOnManualv2] **Automatic Auto Scroll Disabling:** Auto Scroll for the active tab must be **automatically disabled** if the user manually scrolls its log view away from the end.
*   **Keyboard Shortcuts:** Common actions must be accessible via standard keyboard shortcuts:
    *   [ReqShortcutOpenFileV1] `Ctrl+O`: Open File (creates new tab or activates existing)
    *   [ReqShortcutPasteLogv2] `Ctrl+V`: Paste Log Content (creates new tab, when log view focused)
    *   [ReqShortcutFocusSearchV1] `Ctrl+F`: Focus Search Input (for active tab)
    *   [ReqShortcutFocusGoToLineV1] `Ctrl+G`: Focus Go To Line Input (for active tab)
    *   [ReqShortcutSearchSelectedv2] `Ctrl+F3`: Search for Selected Text (in active tab, when log view focused)
    *   [ReqShortcutFindNextV1] `F3`: Find Next Search Result (in active tab)
    *   [ReqShortcutFindPreviousV1] `Shift+F3`: Find Previous Search Result (in active tab)
    *   [ReqShortcutToggleSimulatorV2] `F12`: Toggle Simulator Configuration Panel visibility.
*   [ReqUIResponsivenessV1] **Responsiveness:** The UI must remain responsive during filtering and log updates, using background processing and visual indicators for long operations.

## 6. Configuration and Persistence

*   [ReqSettingsLoadSaveV1] **Load/Save Settings:** Application settings must be loaded at startup and saved automatically on change and at shutdown.
*   **Saved Settings:** Saved settings must include:
    *   [ReqPersistSettingFilterProfilesV2] All defined filter profiles (names, filter trees, and individual filter highlight color settings).
    *   [ReqPersistSettingLastProfileV1] The name/identifier of the last globally active filter profile.
    *   [ReqPersistSettingContextLinesV1] The context line setting.
    *   [ReqPersistSettingShowLineNumsV1] Display options (show line numbers toggle state).
    *   [ReqPersistSettingHighlightTimeV1] Display options (highlight timestamps toggle state).
    *   [ReqPersistSettingSearchCaseV1] Search options (case sensitivity toggle state).
    *   [ReqPersistSettingAutoScrollV1] **Auto scroll to tail toggle state.**
    *   [ReqPersistSettingThemeV1] Selected theme.
    *   [ReqPersistSettingSimulatorV1] **Simulator Settings:** Simulator configuration (rate, error frequency, burst size) must be saved and restored across sessions.
    *   [ReqPersistSettingOpenTabsV1] The state of open tabs as defined in [ReqPersistTabsV1].

## 7. Log Simulator (for Testing/Demo)

*   [ReqSimulatorTogglePanelV1] **Toggle Simulator Panel:** The user must be able to toggle the visibility of a dedicated Simulator Configuration panel using a keyboard shortcut (`F12`).
*   [ReqSimulatorStartStopRestartV1] **Simulator Start/Stop/Restart Controls:** The simulator panel must provide controls to Start, Stop, and Restart the generation of simulated log lines.
*   [ReqSimulatorRateControlV1] **Simulator Rate Control:** The simulator panel must provide a control (e.g., a slider) to adjust the approximate rate of log line generation (Lines PerSecond).
*   [ReqSimulatorClearLogV2] **Simulator Clear Log:** The simulator panel must provide a button to clear the log content of the **currently active tab** (useful if it's a simulator tab, but could apply to any tab).
*   [ReqSimulatorErrorFrequencyV1] **Simulator Error Frequency:** The simulator panel must provide a control (e.g., a slider) to configure the approximate frequency of ERROR messages generated by the simulator (e.g., 1 error every N lines).
*   [ReqSimulatorBurstSizeConfigV1] **Simulator Burst Size Configuration:** The simulator panel must provide a control (e.g., a slider) to configure the number of lines generated in a single burst.
*   [ReqSimulatorBurstModeV1] **Simulator Burst Trigger:** The simulator panel must provide a button to trigger the immediate generation of a configured number of lines ('Burst').
*   ~~[ReqSimulatorReplaceFileV1] **Simulator Activation:** Starting the simulator must stop any active file monitoring and replace the current log view with simulated content. Opening a file must stop the simulator.~~
    * Obsolete, replaced by [ReqSimulatorActivationCreatesTabV1].
*   [ReqSimulatorActivationCreatesTabV1] **Simulator Activation:** Starting the simulator will create a new "Simulator" tab and make it active. If a file-based tab was active, it becomes inactive. If another simulator tab was active, it stops, and the new one starts. Opening a file will stop any active simulator and switch to the file tab.