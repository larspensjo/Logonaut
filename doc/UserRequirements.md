# Logonaut User Requirements

This document outlines the functional requirements for the Logonaut application from a user's perspective.

The requirement ID is a unique ID and a version number that can be referenced from the source code and other documents. The version number, a suffix with 'V<N>', is incremented when a requirement is updated.

## 1. Core Log Viewing & Tab Management

*   [ReqFileMonitorLiveUpdateV2] **File Monitoring & Tabs:** The application must allow the user to select a log file via a menu (`File > Open Log File` or `Ctrl+O`). Each opened file will appear in a new **tab**. The application will continuously monitor the file associated with the **active (selected) tab** for changes, updating its display in real-time. If a file is already open in a tab, selecting it via "Open Log File" will activate the existing tab.
*   [ReqPasteFromClipboardV3] **Paste Input & Tabs:** The user must be able to paste log content directly from the clipboard (using `Ctrl+V` when the log view has focus). This action will create a **new tab** for the pasted content.
*   [ReqLargeFileResponsiveV1] **Large File Handling:** The application must remain responsive and usable even when viewing very large log files. Background processing should be used for filtering and loading.
*   [ReqStandardScrollingV1] **Scrolling:** Standard vertical scrolling must be supported (mouse wheel, keyboard: PgUp/PgDn, Arrows, Home/End) for the content within the active tab.
*   [ReqOverviewScrollbarMapV1] **Overview Scrollbar:** A custom overview scrollbar must provide a visual document map and scroll control for the content within the active tab.
*   [ReqFileResetHandlingV2] **File Reset Handling:** If a monitored log file is reset (e.g., truncated), the application must detect this. The tab for the original file will be converted into an inactive "snapshot" of the content before the reset, and a new, active tab will be opened to monitor the file from the beginning.
*   [ReqFileUnavailableHandlingV1] **File Unavailability:** If the log file associated with an active tab becomes unavailable (e.g., deleted, network drive disconnects), the application should handle this gracefully (e.g., stop tailing, display a message in that tab) without crashing.
*   [ReqRememberLastFolderV1] **Remember Last Folder:** The "Open Log File" dialog should remember the folder from the last opened file and open there by default.
*   [ReqTabManagementV1] **Tab Management:**
    *   [ReqTabActivateSwitchV1] The user must be able to switch between open tabs by clicking on them, making the selected tab active.
    *   [ReqTabActiveMonitoringV1] Only the currently active tab will monitor its source for new activity (e.g., file tailing or simulator running).
    *   [ReqTabInactiveStateV1] Inactive tabs will become passive, preserving their content and filter state. They will display a timestamp of when they became inactive.
    *   [ReqTabCloseButtonV1] The user must be able to close individual tabs via an 'X' button on the tab header.
    *   [ReqTabRenameV1] The user must be able to rename tabs by double-clicking the tab header. The tab header should display this user-defined name.
*   [ReqPersistTabsV1] **Tab Session Persistence:**
    *   [ReqTabSaveSessionV1] The set of open tabs (excluding simulator tabs), their source (file path or path to saved pasted/snapshot content), associated filter profile, and user-defined tab name must be saved at shutdown.
    *   [ReqTabRestoreSessionV1] Upon restart, the application must restore the previously open tabs. All restored tabs will initially be inactive.
    *   [ReqTabSavePastedContentV1] Content from tabs created by pasting or from file-reset snapshots must be saved to a local application data folder when the application closes. These saved content files will be used to restore the tab on the next launch.
    *   [ReqTabDeleteSavedPastedV1] When a tab representing saved pasted or snapshot content is closed by the user, its associated saved content file in the local application data folder must be deleted.
*   [ReqSimulatorTabBehaviorV1] **Simulator Tab Behavior:** Tabs created for the log simulator will be marked as such (e.g., "Simulator" in header), will not have their content or state saved, and will not be restored on application restart.

## 2. Display and Appearance

*   [ReqDisplayRealTimeUpdateV1] **Real-Time Updates:** The log display area for the active tab must update automatically as new relevant log entries arrive or when filtering/settings change for that tab.
*   [ReqDisplayOriginalLineNumbersV1] **Original Line Numbers:** Line numbers displayed next to log entries must correspond to their **original line number** in the source file, regardless of filtering.
*   [ReqToggleLineNumbersV1] **Toggleable Line Numbers:** The original line number margin must be toggleable (`Line Numbers` checkbox). This is a global setting.
*   [ReqHighlightTimestampsV1] **Timestamp Highlighting:** The application must provide an option (`Highlight Timestamps` checkbox) to automatically detect and visually distinguish common timestamp patterns. This is a global setting.
*   [ReqHighlightSelectedLineV1] **Selected Line Highlighting:** The background of the log line currently selected by the user (via mouse click) in the active tab must be visually highlighted.
*   [ReqAdjustFontSizeV1] **Font Customization:** The user must be able to adjust the font family and size for the log display. This is a global setting.
*   [ReqSelectableThemesV1] **Theming Selection:** Color schemes must be adjustable via selectable themes.
*   [ReqThemeOptionsLightDarkV1] **Theme Options:** The application must provide at least a **Light ("Clinical Neon")** and a **Dark ("Neon Night")** theme option, selectable via the `Theme` menu.
*   [ReqThemedTitleBarV1] **Theme Title Bar:** The application should attempt to adapt the window's title bar to the selected theme on compatible Windows versions.
*   [ReqResizableWindowV1] **Window Resizing:** The application window must be resizable.
*   [ReqSplitPanelLayoutV1] **Panel Layout:** The main view must consist of **two primary panels** (Filters on left, Log View/Search on right) separated by a draggable splitter. The Log View panel will host the `TabControl`.
*   [ReqStatusBarDisplayV1] **Status Bar Display:** The application must display a status bar.
*   [ReqStatusBarTotalLinesV2] **Status Bar - Total Lines (Active Tab):** The status bar must show the **total number of lines** read from the source log for the **active tab**, updated dynamically.
*   [ReqStatusBarFilteredLinesV2] **Status Bar - Filtered Lines (Active Tab):** The status bar must show the **number of lines currently visible** after filtering for the **active tab**, updated dynamically.
*   [ReqStatusBarSelectedLineV2] **Status Bar - Selected Line (Active Tab):** The status bar must show the **original line number** of the currently selected log line in the **active tab** (or '-' if none selected), updated dynamically.
*   [ReqGeneralBusyIndicatorV1] **Busy Indicator:** A general **busy indicator** (e.g., spinning icon) must be visible during background operations like filtering.
*   [ReqLoadingOverlayIndicatorV1] **Loading Indicator:** A distinct **overlay animation** must appear directly over the log display area of a tab during its initial file loading phase.

## 3. Filtering System

*   **Filter Profiles:**
    *   [ReqCreateMultipleFilterProfilesV1] The user must be able to create multiple, distinct filter configurations, referred to as "Filter Profiles".
    *   [ReqFilterProfileNamingV1] Each profile must have a user-defined name.
    *   [ReqFilterProfileRenameInlineV2] Profile renaming must be possible **inline** within the profile selection area.
    *   [ReqFilterProfileSelectActiveV2] The user must be able to select which profile is currently active using a `ComboBox`.
    *   [ReqFilterProfilePerTabV2] Each **tab** will have its own **associated filter profile**. Changing the globally selected profile will update the profile for the *currently active tab*. Starting a new tab will use the profile from the previously active tab.
    *   [ReqFilterProfileManageCRUDV1] The application must provide controls (`New`, `Rename`, `Delete` buttons) to manage filter profiles.
*   **Filter Rules (within a profile):**
    *   [ReqFilterRuleSubstringV1] The user must be able to define filter rules based on **exact substring** matching.
    *   [ReqFilterRuleRegexV1] The user must be able to define filter rules based on **regular expression** pattern matching.
    *   [ReqFilterRuleCombineLogicalV1] The user must be able to combine these rules using logical **AND**, **OR**, and **NOR** operators.
    *   [ReqFilterRuleTreeStructureV1] These rules must be manageable in a **hierarchical tree structure** displayed in the filter panel.
    *   [ReqFilterNodeEditInlineV2] Editing filter values (substrings/regex) must happen **inline** within the tree view.
    *   [ReqDnDFilterManageV1] Filter node management must be performed using Drag and Drop from a palette to the tree.
    *   [ReqFilterSubstringFromSelectionV1] A special "Substring from Selection" filter must be available in the palette, which is enabled when text is selected in the active log view and uses that text as its initial value.
*   **Filtering Behavior:**
    *   [ReqFilterDisplayMatchingLinesV2] The log display for each tab must show only the lines matching the rules of **its associated** filter profile.
    *   [ReqFilterContextLinesV1] The application must provide a setting (`Context Lines` input with increment/decrement buttons) to include a specified number of **context lines** before and after each matching line. This is a global setting.
    *   [ReqFilterHighlightMatchesV1] Text segments that cause a line to match a filter rule must be visually highlighted in the output.
    *   [ReqFilterHighlightPerRuleColorV1] For individual Substring and Regex filters, the user must be able to select a distinct highlight color from a predefined, theme-aware palette.
    *   [ReqFilterNodeToggleEnableV1] Individual filter rules within a profile's tree must be toggleable (enabled/disabled) via a `CheckBox`.
*   [ReqFilterDynamicUpdateViewV3] **Dynamic Filter Updates:** The filtered log view of the **active tab** must update automatically whenever its associated filter profile is changed or the rules/settings within that profile are modified. Inactive tabs will apply updated profile settings upon their next activation.
*   **Persistence:**
    *   [ReqPersistFilterProfilesV3] All created filter profiles (names, structures, and individual filter highlight color settings) must be saved when the application closes and reloaded on startup.
    *   [ReqPersistLastActiveProfileV1] The application must remember and automatically select the last globally active profile upon restarting.

## 4. Search Functionality (Per Active Tab)

*   [ReqSearchTextEntryV2] **Text Search (Active Tab):** The user must be able to enter text (`Ctrl+F` to focus) to search for within the currently displayed (filtered) log content of the **active tab**.
*   [ReqSearchHighlightResultsV2] **Highlighting Search Results (Active Tab):** All occurrences of the search term must be visually highlighted in the log display of the **active tab** (distinct from filter highlighting).
*   [ReqSearchRulerMarkersV2] **Overview Ruler Search Markers (Active Tab):** Search results for the **active tab** must be marked on its overview scrollbar.
*   [ReqSearchNavigateResultsV2] **Search Navigation (Active Tab):** Buttons and keyboard shortcuts (`F3` for Next, `Shift+F3` for Previous) must be provided to navigate between search results within the **active tab**.
*   [ReqSearchSelectedTextShortcutV1] **Search for Selected Text:** The user must be able to press `Ctrl+F3` to automatically search for the text currently selected in the log display of the active tab.
*   [ReqSearchStatusIndicatorV2] **Search Status (Active Tab):** A status indicator must display the number of matches found and the current match index for the **active tab** (e.g., "Match 3 of 15").
*   [ReqSearchCaseSensitiveOptionV1] **Case Sensitivity Option:** An option (`Case Sensitive` checkbox) must be provided to toggle case sensitivity for the search. This is a global setting.

## 5. User Interaction

*   [ReqFilterControlsPanelV1] **Filter Controls Panel:** A dedicated panel must provide intuitive controls for profile and filter rule management.
*   [ReqLineSelectionClickV2] **Line Selection (Active Tab):** Clicking on a line number or log entry in the display area of the **active tab** must select that line.
*   [ReqGoToLineInputBoxV2] **Go To Line Input (Active Tab):** The user must be able to enter an original line number into a dedicated text box (`Ctrl+G` to focus) in the status bar, relevant to the **active tab**.
*   [ReqGoToLineExecuteJumpV2] **GoTo Line Execution (Active Tab):** Pressing Enter must jump to (select and scroll to) the entered line number if it exists in the *filtered* view of the **active tab**.
*   [ReqGoToLineFeedbackNotFoundV2] **Go To Line Feedback:** Feedback must indicate if the entered line number is not found in the filtered view of the active tab or if the input is invalid.
*   **Auto Scroll:**
    *   [ReqAutoScrollOptionV3] **Auto Scroll Control:** An **anchor toggle button** in the status bar must control automatically scrolling to the end of the log view of the **active tab**.
    *   [ReqAutoScrollDisableOnManualV2] **Automatic Auto Scroll Disabling:** Auto Scroll for the active tab must be **automatically disabled** if the user manually scrolls its log view away from the end.
*   **Keyboard Shortcuts:** Common actions must be accessible via standard keyboard shortcuts:
    *   [ReqShortcutOpenFileV1] `Ctrl+O`: Open File
    *   [ReqShortcutPasteLogV2] `Ctrl+V`: Paste Log Content (creates new tab)
    *   [ReqShortcutFocusSearchV1] `Ctrl+F`: Focus Search Input
    *   [ReqShortcutFocusGoToLineV1] `Ctrl+G`: Focus Go To Line Input
    *   [ReqShortcutSearchSelectedV2] `Ctrl+F3`: Search for Selected Text
    *   [ReqShortcutFindNextV1] `F3`: Find Next Search Result
    *   [ReqShortcutFindPreviousV1] `Shift+F3`: Find Previous Search Result
    *   [ReqShortcutToggleSimulatorV2] `F12`: Toggle Simulator Configuration Panel visibility.
    *   [ReqShortcutUndoRedoV1] `Ctrl+Z` / `Ctrl+Y`: Undo/Redo filter tree modifications.

## 6. Configuration and Persistence

*   [ReqSettingsLoadSaveV1] **Load/Save Settings:** Application settings must be loaded at startup and saved automatically at shutdown.
*   **Saved Settings:** Saved settings must include:
    *   [ReqPersistSettingFilterProfilesV2] All defined filter profiles.
    *   [ReqPersistSettingLastProfileV1] The name of the last globally active filter profile.
    *   [ReqPersistSettingContextLinesV1] The context line setting.
    *   [ReqPersistSettingShowLineNumsV1] Display options (show line numbers).
    *   [ReqPersistSettingHighlightTimeV1] Display options (highlight timestamps).
    *   [ReqPersistSettingSearchCaseV1] Search options (case sensitivity).
    *   [ReqPersistSettingAutoScrollV1] Auto scroll toggle state.
    *   [ReqPersistSettingFontV2] Font family and size.
    *   [ReqPersistSettingThemeV1] Selected theme.
    *   [ReqPersistSettingSimulatorV1] Simulator configuration (rate, error frequency, burst size).
    *   [ReqPersistSettingOpenTabsV1] The state of open tabs as defined in [ReqPersistTabsV1].
    *   [ReqPersistSettingWindowGeometryV1] The size, position, and state of the main window and its internal panels.
