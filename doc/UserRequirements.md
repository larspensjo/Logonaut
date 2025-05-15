# Logonaut User Requirements

This document outlines the functional requirements for the Logonaut application from a user's perspective.

The requirement ID is a unique ID and a version number that can be referenced from the source code and other documents.
The version number, a suffix with 'V<N>' is incremented when a requirement is updated.

## 1. Core Log Viewing

*   [ReqFileMonitorLiveUpdateV1] **File Monitoring:** The application must allow the user to select a log file via a menu (`File > Open Log File` or `Ctrl+O`) and then continuously monitor that file for changes, updating the display in real-time as new lines are added.
*   [ReqFilterEfficientRealTimeV1] Filtering must efficiently handle real-time updates from monitored files or pasted content, applying filters to new data incrementally where possible to maintain UI responsiveness even with large log sources.
*   [ReqPasteFromClipboardv2] **Paste Input:** The user must be able to paste log content directly from the clipboard (using `Ctrl+V` when the log view has focus) to view it without saving it to a file first. Pasting should replace the current view content.
*   [ReqLargeFileResponsiveV1] **Large File Handling:** The application must remain responsive and usable even when viewing very large log files. Background processing should be used for filtering and loading.
*   [ReqStandardScrollingV1] **Scrolling:** Standard vertical scrolling must be supported (mouse wheel, keyboard: PgUp/PgDn, Arrows, Home/End).
*   [ReqOverviewScrollbarMapV1] **Overview Scrollbar:** A custom overview scrollbar must provide visual document mapping and scroll control.
*   [ReqFileResetHandlingV1] **File Reset Handling:** If the monitored log file is reset (e.g., truncated), the application should ideally detect this, potentially clear the display, and continue monitoring from the beginning of the reset file. *(Current implementation might require reopening)*.
*   [ReqFileUnavailableHandlingV1] **File Unavailability:** If the log file becomes unavailable (e.g., deleted, network drive disconnects), the application should handle this gracefully (e.g., stop tailing, display a message) without crashing.
*   [ReqRememberLastFolderV1] **Remember Last Folder:** The "Open Log File" dialog should remember the folder from the last opened file and open there by default.

## 2. Display and Appearance

*   [ReqDisplayRealTimeUpdateV1] **Real-Time Updates:** The log display area must update automatically as new relevant log entries arrive or when filtering/settings change.
*   [ReqDisplayOriginalLineNumbersV1] **Original Line Numbers:** Line numbers displayed next to log entries must correspond to their **original line number** in the source file, regardless of filtering.
*   [ReqToggleLineNumbersV1] **Toggleable Line Numbers:** The original line number margin must be toggleable (`Line Numbers` checkbox).
*   [ReqHighlightTimestampsV1] **Timestamp Highlighting:** The application must provide an option (`Highlight Timestamps` checkbox) to automatically detect and visually distinguish common timestamp patterns at the beginning of lines.
*   [ReqHighlightSelectedLineV1] **Selected Line Highlighting:** The background of the log line currently selected by the user (via mouse click) must be visually highlighted.
*   [ReqAdjustFontSizeV1] **Readability Customization:** The user must be able to adjust the font size for the log display.
*   [ReqSelectableThemesV1] **Theming Selection:** Color schemes must be adjustable via selectable themes.
*   [ReqThemeOptionsLightDarkV1] **Theme Options:** The application must provide at least a **Light ("Clinical Neon")** and a **Dark ("Neon Night")** theme option, selectable via the `Theme` menu.
*   [ReqThemedTitleBarV1] **Theme Title Bar:** The application should attempt to adapt the window's title bar to the selected theme on compatible Windows versions.
*   [ReqResizableWindowV1] **Window Resizing:** The application window must be resizable.
*   [ReqSplitPanelLayoutV1] **Panel Layout:** The main view must consist of **two primary panels** (Filters on left, Log View/Search on right) separated by a draggable splitter.
*   [ReqStatusBarDisplayV1] **Status Bar Display:** The application must display a status bar.
*   [ReqStatusBarTotalLinesV1] **Status Bar - Total Lines:** The status bar must show the **total number of lines** read from the source log, updated dynamically.
*   [ReqStatusBarFilteredLinesV1] **Status Bar - Filtered Lines:** The status bar must show the **number of lines currently visible** after filtering, updated dynamically.
*   [ReqStatusBarSelectedLineV1] **Status Bar - Selected Line:** The status bar must show the **original line number** of the currently selected log line (or '-' if none selected), updated dynamically.
*   [ReqGeneralBusyIndicatorV1] **Busy Indicator - General:** A general **busy indicator** (e.g., spinning icon) must be visible during background operations like filtering.
*   [ReqLoadingOverlayIndicatorV1] **Busy Indicator - Loading Overlay:** A distinct **overlay animation** (e.g., subtle scanlines) must appear directly over the log display area during the initial file loading phase.

## 3. Filtering System

*   **Filter Profiles:**
    *   [ReqCreateMultipleFilterProfilesV1] The user must be able to create multiple, distinct filter configurations, referred to as "Filter Profiles".
    *   [ReqFilterProfileNamingV1] Each profile must have a user-defined name.
    *   [ReqFilterProfileRenameInlinev2] Profile renaming must be possible **inline** within the profile selection area (e.g., by clicking a Rename button that makes the name editable).
    *   [ReqFilterProfileSelectActiveV1] The user must be able to select which profile is currently **active** using a `ComboBox`.
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
    *   [ReqFilterSubstringDefaultFromSelectionV2] There shall be a special substring filter in the palette that automaticallydynamically contains selected text from the log view.
*   **Filtering Behavior:**
    *   [ReqFilterDisplayMatchingLinesV1] The log display must show only the lines matching the rules of the **active** filter profile.
    *   [ReqFilterContextLinesV1] The application must provide a setting (`Context Lines` input with increment/decrement buttons) to include a specified number of **context lines** before and after each matching line.
    *   [ReqFilterHighlightMatchesV1] Text segments that cause a line to match a filter rule (substrings or regex matches) must be visually highlighted in the output (distinct background color).
    *   [ReqFilterHighlightPerRuleColorV1] User-Configurable Per-Filter Highlighting Color: For individual Substring and Regex filters, the user must be able to select a distinct highlight color from a predefined, theme-aware palette (e.g., Default, Red, Green, Blue, Yellow) using a `ComboBox` next to the filter rule in the tree. The selected color should apply to both the filter match highlighting in the log view and the visual representation of the color choice in the `ComboBox` itself.
    *   [ReqFilterNodeToggleEnableV1] Individual filter rules (nodes) within the active profile's tree must be toggleable (enabled/disabled) via a `CheckBox` next to the rule, without removing them.
*   [ReqFilterDynamicUpdateViewV2] Dynamic Filter Updates: The filtered log view must update automatically and efficiently whenever the active filter profile is changed or the rules/settings (including highlight color) within the active profile are modified.
*   **Persistence:**
    *   [ReqPersistFilterProfilesV3] All created filter profiles (names, structures, and individual filter highlight color settings) must be saved when the application closes and reloaded on startup.
    *   [ReqPersistLastActiveProfileV1] The application must remember and automatically select the last active profile upon restarting.

## 4. Search Functionality

*   [ReqSearchTextEntryV1] **Text Search:** The user must be able to enter text (`Ctrl+F` to focus) to search for within the currently displayed (filtered) log content.
*   [ReqSearchHighlightResultsV1] **Highlighting Search Results:** All occurrences of the search term must be visually highlighted in the log display (distinct from filter highlighting).
*   [ReqSearchRulerMarkersV1] **Overview Ruler Search Markers:** Search results must be marked on the overview scrollbar.
*   [ReqSearchNavigateResultsV1] **Search Navigation:** Buttons (`Previous`/`Next`) and keyboard shortcuts (`F3` for Next, `Shift+F3` for Previous) must be provided to navigate between search results.
*   [ReqSearchSelectedTextShortcutV1] **Search Selected Text:** The user must be able to press `Ctrl+F3` to automatically search for the text currently selected in the log display.
*   [ReqSearchStatusIndicatorV1] **Search Status:** A status indicator must display the number of matches found and the current match index (e.g., "Match 3 of 15", "Phrase not found").
*   [ReqSearchCaseSensitiveOptionV1] **Case Sensitivity Option:** An option (`Case Sensitive` checkbox) must be provided to toggle case sensitivity for the search.

## 5. User Interaction

*   [ReqFilterControlsPanelV1] **Filter Controls Panel:** A dedicated panel must provide intuitive controls for profile and filter rule management as described in section 3.
*   [ReqLineSelectionClickV1] **Line Selection:** Clicking on a line number or log entry in the display area must select that line, highlighting it and updating the selected line number in the status bar.
*   [ReqGoToLineInputBoxV1] **Go To Line Input:** The user must be able to enter an original line number into a dedicated text box (`Ctrl+G` to focus) in the status bar.
*   [ReqGoToLineExecuteJumpV1] **Go To Line Execution:** Pressing Enter (or clicking a button) must jump to (select and scroll to) the entered line number if it exists in the *filtered* view.
*   [ReqGoToLineFeedbackNotFoundv2] **Go To Line Feedback:** Feedback (e.g., a temporary status message and/or visual cue like background color change) must indicate if the entered line number is not found in the filtered view or if the input is invalid.
*   **Auto Scroll:**
    *   [ReqAutoScrollOptionv3] **Auto Scroll Control:** An **anchor toggle button** in the status bar must be available to enable/disable automatically scrolling to the end of the log view when new lines are added. The button's visual state (e.g., icon color/style) must clearly indicate whether auto-scroll is enabled or disabled. When enabled and filter changes occur, the view should scroll to the new end.
    *   [ReqAutoScrollDisableOnManualv2] **Automatic Auto Scroll Disabling:** Auto Scroll must be **automatically disabled** if the user manually scrolls the log view away from the end (e.g., using the mouse wheel upwards, keyboard navigation keys like PgUp/Dn/Arrows/Home, or interacting with the scrollbar/overview ruler).
*   **Keyboard Shortcuts:** Common actions must be accessible via standard keyboard shortcuts:
    *   [ReqShortcutOpenFileV1] `Ctrl+O`: Open File
    *   [ReqShortcutPasteLogv2] `Ctrl+V`: Paste Log Content (when log view focused)
    *   [ReqShortcutFocusSearchV1] `Ctrl+F`: Focus Search Input
    *   [ReqShortcutFocusGoToLineV1] `Ctrl+G`: Focus Go To Line Input
    *   [ReqShortcutSearchSelectedv2] `Ctrl+F3`: Search for Selected Text (when log view focused)
    *   [ReqShortcutFindNextV1] `F3`: Find Next Search Result
    *   [ReqShortcutFindPreviousV1] `Shift+F3`: Find Previous Search Result
    *   [ReqShortcutToggleSimulatorV2] `F12`: Toggle Simulator Configuration Panel visibility.
*   [ReqUIResponsivenessV1] **Responsiveness:** The UI must remain responsive during filtering and log updates, using background processing and visual indicators for long operations.

## 6. Configuration and Persistence

*   [ReqSettingsLoadSaveV1] **Load/Save Settings:** Application settings must be loaded at startup and saved automatically on change and at shutdown.
*   **Saved Settings:** Saved settings must include:
    *   [ReqPersistSettingFilterProfilesV2] All defined filter profiles (names, filter trees, and individual filter highlight color settings).
    *   [ReqPersistSettingLastProfileV1] The name/identifier of the last active filter profile.
    *   [ReqPersistSettingContextLinesV1] The context line setting.
    *   [ReqPersistSettingShowLineNumsV1] Display options (show line numbers toggle state).
    *   [ReqPersistSettingHighlightTimeV1] Display options (highlight timestamps toggle state).
    *   [ReqPersistSettingSearchCaseV1] Search options (case sensitivity toggle state).
    *   [ReqPersistSettingAutoScrollV1] **Auto scroll to tail toggle state.**
    *   [ReqPersistSettingThemeV1] Selected theme.
    *   [ReqPersistSettingSimulatorV1] **Simulator Settings:** Simulator configuration (rate, error frequency, burst size) must be saved and restored across sessions.

## 7. Log Simulator (for Testing/Demo)

*   [ReqSimulatorTogglePanelV1] **Toggle Simulator Panel:** The user must be able to toggle the visibility of a dedicated Simulator Configuration panel using a keyboard shortcut (`Ctrl+Alt+Shift+S`).
*   [ReqSimulatorStartStopRestartV1] **Simulator Start/Stop/Restart Controls:** The simulator panel must provide controls to Start, Stop, and Restart the generation of simulated log lines.
*   [ReqSimulatorRateControlV1] **Simulator Rate Control:** The simulator panel must provide a control (e.g., a slider) to adjust the approximate rate of log line generation (Lines Per Second).
*   [ReqSimulatorClearLogV1] **Simulator Clear Log:** The simulator panel must provide a button to clear the currently displayed log content.
*   [ReqSimulatorErrorFrequencyV1] **Simulator Error Frequency:** The simulator panel must provide a control (e.g., a slider) to configure the approximate frequency of ERROR messages generated by the simulator (e.g., 1 error every N lines).
*   [ReqSimulatorBurstSizeConfigV1] **Simulator Burst Size Configuration:** The simulator panel must provide a control (e.g., a slider) to configure the number of lines generated in a single burst.
*   [ReqSimulatorBurstModeV1] **Simulator Burst Trigger:** The simulator panel must provide a button to trigger the immediate generation of a configured number of lines ('Burst').
*   [ReqSimulatorReplaceFileV1] **Simulator Activation:** Starting the simulator must stop any active file monitoring and replace the current log view with simulated content. Opening a file must stop the simulator.