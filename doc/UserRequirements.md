# Logonaut User Requirements

This document outlines the functional requirements for the Logonaut application from a user's perspective.

## 1. Core Log Viewing

*   **File Monitoring:** The application must allow the user to select a log file via a menu, and then continuously monitor that file for changes, updating the display in real-time as new lines are added.
*   **Paste Input:** The user must be able to paste log content directly from the clipboard (e.g., using Ctrl+V) to view it without saving it to a file first.
*   **Large File Handling:** The application must remain responsive and usable even when viewing very large log files (hundreds of megabytes or more).
*   **Scrolling:** Standard vertical scrolling through the displayed log content must be supported (e.g., via mouse wheel, scrollbar interaction, PgUp/PgDn).
*   **File Reset Handling:** If the monitored log file is reset (e.g., truncated), the application should ideally detect this, potentially clear the display, and continue monitoring from the beginning of the reset file. *(Current implementation might require reopening)*.
*   **File Unavailability:** If the log file becomes unavailable (e.g., deleted, network drive disconnects), the application should handle this gracefully (e.g., stop tailing, display a message) without crashing.

## 2. Display and Appearance

*   **Real-Time Updates:** The log display area must update automatically as new relevant log entries arrive or when filtering/settings change.
*   **Original Line Numbers:** Line numbers displayed next to log entries must correspond to their original line number in the source file, regardless of filtering. This margin should be toggleable.
*   **Timestamp Highlighting:** The application should provide an option to automatically detect and visually distinguish common timestamp patterns at the beginning of lines for improved readability.
*   **Readability Customization:** The user should be able to adjust the font size for the log display. Color schemes should be adjustable via selectable themes (e.g., Light/Dark).
*   **Theming:** The application must provide at least a Light and a Dark theme option.
*   **Window Management:** The application window must be resizable.
*   **Status Bar:** The application must display a status bar showing:
    *   The total number of lines read from the source log.
    *   The number of lines currently visible after filtering.
    *   Both values must update dynamically as the log is processed and filters change.

## 3. Filtering System

*   **Filter Profiles:**
    *   The user must be able to create multiple, distinct filter configurations, referred to as "Filter Profiles".
    *   Each profile must have a user-defined name.
    *   The user must be able to select which profile is currently **active** using a dropdown list.
    *   The application must provide controls to **Create**, **Rename**, and **Delete** filter profiles.
*   **Filter Rules (within a profile):**
    *   Within the active profile, the user must be able to define filter rules based on:
        *   Exact substring matching.
        *   Regular expression pattern matching.
    *   The user must be able to combine these rules using logical **AND**, **OR**, and **NOR** operators.
    *   These rules must be manageable in a **hierarchical tree structure** to allow for complex conditions (e.g., `(A or (B and not C))`).
*   **Filtering Behavior:**
    *   The log display must show only the lines matching the rules of the **active** filter profile.
    *   The application must provide a setting (global, per profile) to include a specified number of **context lines** before and after each matching line.
    *   Text segments that cause a line to match a filter rule (substrings or regex matches) should be visually highlighted in the output (e.g., background color).
    *   Individual filter rules (nodes) within the active profile's tree must be toggleable (enabled/disabled) without removing them.
*   **Dynamic Updates:** The filtered log view must update automatically and efficiently whenever the active filter profile is changed or the rules/settings within the active profile are modified.
*   **Persistence:** All created filter profiles (names and structures) must be saved when the application closes and reloaded on startup. The application must remember and automatically select the last active profile upon restarting.

## 4. Search Functionality

*   **Text Search:** The user must be able to enter text to search for within the currently displayed (filtered) log content.
*   **Highlighting:** All occurrences of the search term must be visually highlighted in the log display, distinct from filter highlighting.
*   **Navigation:** Buttons must be provided to navigate to the **Previous** and **Next** search result occurrence.
*   **Status:** A status indicator should display the number of matches found and the current match index (e.g., "Match 3 of 15").
*   **Case Sensitivity:** An option must be provided to toggle case sensitivity for the search.

## 5. User Interaction

*   **Filter Controls:** A dedicated panel must provide intuitive controls for:
    *   Selecting the active filter profile.
    *   Managing profiles (Create, Rename, Delete).
    *   Viewing and interacting with the active profile's filter tree (expand/collapse).
    *   Adding, removing, and editing filter nodes within the active tree.
    *   Toggling the enabled state of filter nodes.
*   **Keyboard Shortcuts:** Common actions should be accessible via standard keyboard shortcuts (e.g., `Ctrl+F` for search focus, potentially others for navigation).
*   **Responsiveness:** The UI must remain responsive during filtering and log updates, especially on large files. A visual indicator should be shown if a filtering operation takes significant time.

## 6. Configuration and Persistence

*   **Load/Save:** Application settings must be loaded at startup and saved at shutdown.
*   **Saved Settings:** Saved settings must include:
    *   All defined filter profiles (names and filter trees).
    *   The name/identifier of the last active filter profile.
    *   The context line setting.
    *   Display options (show line numbers, highlight timestamps).
    *   Search options (case sensitivity).
    *   Selected theme.
    *   *(Future: Last viewed file/position, window size/location)*.