# Logonaut Requirements

## 1. General Requirements

Logonaut is a Windows application written in C#. It functions as a log viewer, reading input from a log file and displaying it dynamically.

*   The application shall continue to read and asynchronously update the display as the log file is updated.
*   The log file is a continuous process, which may go on for hours.
*   The application shall use a configuration that is loaded at startup and saved at shutdown to remember user settings.
*   The log file can be very large (hundreds of megabytes), so text updating and filtering must be efficient.
*   If the log file becomes unavailable, the application shall handle it gracefully.

## 2. Core Functionality

### 2.1 Log File Handling

*   The application shall provide a menu where the user can select which log file to monitor.
*   The application shall support pasting log content directly from the clipboard using Ctrl+V.
*   It shall be possible to scroll up and down the filtered output.
*   If a log file is reset, the application shall notify the user and start a new tab.

### 2.2 Display Requirements

*   The log output shall be continuously updated in real-time.
*   There shall be some kind of indication showing how much of the log output is currently included in the display.
*   Many log files have a prefix in the format of a date or time. The application shall detect this prefix and visibly mark it for better readability.
*   Line numbers displayed next to filtered log entries shall correspond to the original line number in the complete log file.
*   The user shall be able to adjust the font size and color scheme for better readability.

### 2.3 User Interaction

*   The application shall support a free-text search function where the user enters a string, which is then highlighted in the output.
*   There shall be buttons to navigate up and down through the search results.
*   The user shall be able to jump to a specific line number within the log file.
*   The application shall support keyboard shortcuts for common actions (e.g., Ctrl+F for search, PgUp/PgDn for scrolling, Ctrl+G for jumping to a specific line).

### 2.4 Syntax Highlighting

*   The application shall support customizable syntax highlighting for log files.
*   Users shall be able to define patterns for timestamps, error messages, warnings, and other log elements.
*   Each pattern shall be associated with a specific color, font weight, and style.
*   The application shall provide a UI for users to add, edit, and remove highlighting rules (within the dynamic highlighting definition).
*   Highlighting configurations shall be saved with the application settings (as part of the overall settings file).
*   The application shall include predefined highlighting configurations for common log formats (applied by default or selectable).
*   Highlighting shall update in real-time as configurations change.

## 3. User Interface & Experience

### 3.1 Theming & Appearance

*   The application shall support an optional dark theme.
*   The display shall have a modern, visually appealing style.
*   The UI shall also be designed flexibly, allowing future style changes with minimal manual adjustments.

### 3.2 Window Management

*   The application shall support dynamic window resizing by the user.
*   The application shall support opening multiple log files in separate tabs or windows. *(Future consideration)*

## 4. Filtering System

### 4.1 Filter Functionality

*   The log viewer shall support multiple named **Filter Profiles**. Each profile contains a hierarchical filter tree.
*   The user shall be able to select the **active** filter profile from a list (e.g., using a ComboBox).
*   Applying filters or changing the active profile shall update the log display dynamically.
*   Filters within a profile tree shall support two types of text matching:
    *   Exact substring matching (testing against all positions in each line)
    *   Regular expression pattern matching for more complex filtering needs
*   Filters shall support AND, OR and NOR combinations (e.g., find all lines containing A and B, or all lines containing A or B).
*   Filters shall be structured hierarchically within each profile to allow complex conditions (e.g., (A or (B and not C))).
*   Users shall be able to set a global line number context (default: 0) to include a specified number of lines before and after each match in the *active* profile.
*   The text used for filtering (matching substrings/regex) shall be visibly marked in the output, such as with a colored background or changed font, based on the rules in the *active* profile.
*   Each filter node within the active profile's tree shall be individually enabled or disabled without needing to be removed.
*   Filtered output shall update dynamically. To avoid excessive updates, debouncing shall be used when filter configurations or the active profile change.
*   Whenever the active filter profile or its settings are changed, the filtered output shall update automatically.
*   The user shall be able to save *all* filter profiles with the application configuration. The application shall remember and restore the last active profile on startup.
*   The user shall be able to filter logs based on timestamps (e.g., show logs between 12:00-14:00 or last 10 minutes). *(Future consideration - Requires specific filter type)*

### 4.2 Filter Performance

*   Filtering must be efficient, given that log files can be very large.
*   If filtering is slow on large files, the application shall provide a progress indicator (e.g., a spinning indicator in the UI).

## 5. Filter Controls & UI

### 5.1 Filter Panel

*   The filtering controls shall be located in a separate panel (e.g., on the left side), with log output on the right.
*   The panel shall contain:
    *   A `ComboBox` to select the **active filter profile**.
    *   Buttons to **Create**, **Rename**, and **Delete** filter profiles.
    *   A `TreeView` displaying the filter hierarchy of the *currently selected* profile.
    *   Buttons to add, remove, and edit filter nodes *within* the active profile's tree.

### 5.2 Filter Profile Management

*   The `ComboBox` shall list all saved filter profiles by name.
*   Selecting a profile from the `ComboBox` shall make it the active profile, update the `TreeView`, and trigger a re-filter of the log view.
*   The **Create** button shall add a new, empty profile with a default name (e.g., "New Profile 1") to the list, select it, and allow the user to start building its tree.
*   The **Rename** button (acting on the selected profile in the `ComboBox`) shall prompt the user for a new name.
*   The **Delete** button (acting on the selected profile in the `ComboBox`) shall prompt for confirmation before removing the profile.

### 5.3 Filter Tree View (`TreeView` within the active profile)

*   Filters within the active profile shall be presented in a tree-like structure, making it easy for users to expand, collapse, and navigate.
*   Different filter node types (Substring, Regex, AND, OR, NOR) shall be visually distinguishable (e.g., using different icons).
*   Users shall be able to select a node within the `TreeView`.
*   Buttons shall be available for adding specific filter node types (`SubstringFilter`, `RegexFilter`, `AndFilter`, `OrFilter`, `NorFilter`) *to the selected node* if it's a composite type (AND, OR, NOR), or potentially as a sibling.
*   When a `SubstringFilter` or `RegexFilter` node is added or selected for edit, it shall automatically receive input focus for its value.
*   The UI shall provide visual cues to distinguish between different filter types.
*   A **Remove** button shall remove the selected node (and its children) from the active profile's tree.
*   An **Edit** button (or double-click) shall allow editing the value of the selected `SubstringFilter` or `RegexFilter`.

## 6. Advanced Search & Exporting

*   The user shall be able to save frequently used search queries. *(Future consideration)*
*   The application shall maintain a search history for quick re-use. *(Future consideration)*
*   The filtered log output shall be exportable to a new file in multiple formats (plain text, JSON, CSV). *(Future consideration)*

## 7. Performance & Optimization

*   The application shall use multi-threading (via Reactive Extensions and background tasks) for reading log files and applying filters to maintain UI responsiveness.
*   Search performance shall be optimized.
*   The application shall allow adjusting update frequency for performance tuning if necessary (though reactive debouncing is preferred).

## 9. Configuration & Persistence

*   The application shall load user settings from a configuration file (e.g., `settings.json` in `%LocalAppData%\Logonaut`) at startup.
*   Configuration shall include *all* saved filter profiles (names and trees).
*   Configuration shall include the name or identifier of the last active filter profile.
*   Other settings (context lines, theme, UI state, etc.) shall be included.
*   Settings shall be saved when the application shuts down.
*   The last viewed position in the log file shall be saved so that the user can resume from where they left off. *(Future consideration)*