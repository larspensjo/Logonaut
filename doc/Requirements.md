# Logonaut Requirements

## 1. General Requirements
Logonaut is a Windows application written in C#. It functions as a log viewer, reading input from a log file and displaying it dynamically.

- The application shall continue to read and asynchronously update the display as the log file is updated.
- The log file is a continuous process, which may go on for hours.
- The application shall use a configuration that is loaded at startup and saved at shutdown to remember user settings.
- The log file can be very large (hundreds of megabytes), so text updating and filtering must be efficient.
- If the log file becomes unavailable, the application shall handle it gracefully.

## 2. Core Functionality
### 2.1 Log File Handling
- The application shall provide a menu where the user can select which log file to monitor.
- It shall be possible to scroll up and down the filtered output.
- If a log file is reset, the application shall notify the user and start a new tab.

### 2.2 Display Requirements
- The log output shall be continuously updated in real-time.
- There shall be some kind of indication showing how much of the log output is currently included in the display.
- Many log files have a prefix in the format of a date or time. The application shall detect this prefix and visibly mark it for better readability.
- The user shall be able to adjust the font size and color scheme for better readability.

### 2.3 User Interaction
- The application shall support a free-text search function where the user enters a string, which is then highlighted in the output.
- There shall be buttons to navigate up and down through the search results.
- The user shall be able to jump to a specific line number within the log file.
- The application shall support keyboard shortcuts for common actions (e.g., Ctrl+F for search, PgUp/PgDn for scrolling, Ctrl+G for jumping to a specific line).

### 2.4 Syntax Highlighting
- The application shall support customizable syntax highlighting for log files.
- Users shall be able to define patterns for timestamps, error messages, warnings, and other log elements.
- Each pattern shall be associated with a specific color, font weight, and style.
- The application shall provide a UI for users to add, edit, and remove highlighting rules.
- Highlighting configurations shall be saved with the application settings.
- The application shall include predefined highlighting configurations for common log formats.
- Highlighting shall update in real-time as configurations change.

## 3. User Interface & Experience
### 3.1 Theming & Appearance
- The application shall support an optional dark theme.
- The display shall have a modern, visually appealing style.
- The UI shall also be designed flexibly, allowing future style changes with minimal manual adjustments.

### 3.2 Window Management
- The application shall support dynamic window resizing by the user.
- The application shall support opening multiple log files in separate tabs or windows.

## 4. Filtering System
### 4.1 Filter Functionality
- The log viewer shall support filters that can be added or removed while the application is running.
- Filters shall support two types of text matching:
  - Exact substring matching (testing against all positions in each line)
  - Regular expression pattern matching for more complex filtering needs
- Filters shall support negation (excluding lines that contain certain text).
- Filters shall support AND and OR combinations (e.g., find all lines containing A and B, or all lines containing A or B).
- Filters shall be structured hierarchically to allow complex conditions (e.g., (A or (B and not C))).
- Users shall be able to set a global line number context (default: 0) to include a specified number of lines before and after each match.
- The text used for filtering shall be visibly marked in the output, such as with a colored background or changed font.
- Each filter shall be individually enabled or disabled without needing to be removed.
- Filtered output shall update dynamically. To avoid excessive updates, an adjustable polling period shall be used when necessary.
- Whenever filters are changed, the filtered output shall update automatically.
- The user shall be able to save filter settings with the configuration, storing them under named filter trees for easy retrieval.
- The user shall be able to filter logs based on timestamps (e.g., show logs between 12:00-14:00 or last 10 minutes).

### 4.2 Filter Performance
- Filtering must be efficient, given that log files can be very large.
- If filtering is slow on large files, the application shall provide a progress indicator. The progress indicator should be the mouse pointer itself, but rather some live updated spinning wheel in the application window.

## 5. Filter Controls & UI
### 5.1 Filter Tree View
- Filters shall be presented in a tree-like structure, making it easy for users to expand, collapse, and navigate.
- The filter control shall be located in a separate panel, possibly on the left side, with log output on the right.
- Different filter types shall be visually distinguishable (e.g., using different colors or icons).

### 5.2 Filter Operations
- The application shall provide controls to add, remove, and rearrange filters within the tree structure.
- The filter tree starts empty, and only one top-level filter is allowed.
- When adding a filter, the user selects its type.
- If no filters exist, the new filter becomes the top-level filter.
- If filters already exist, the user must select where the new filter should be placed in the hierarchy.
- If a filter is removed, its children are also removed unless rearranged.
- Several buttons shall be available for adding specific filter types: 
  - SubstringFilter (for exact text matching)
  - RegexFilter (for regular expression pattern matching)
  - AndFilter
  - OrFilter
  - NegationFilter
- When a SubstringFilter or RegexFilter is added, it shall automatically receive input focus.
- The UI shall provide visual cues to distinguish between substring filters and regex filters.

## 6. Advanced Search & Exporting
- The user shall be able to save frequently used search queries.
- The application shall maintain a search history for quick re-use.
- The filtered log output shall be exportable to a new file in multiple formats (plain text, JSON, CSV).

## 7. Performance & Optimization
- The application shall use multi-threading for reading log files and applying filters to maintain UI responsiveness.
- Search performance shall be optimized by indexing large log files in memory where feasible.
- The application shall allow adjusting update frequency for performance tuning.

## 9. Configuration & Persistence
- The application shall load user settings from a configuration file at startup.
- Configuration shall include saved filter trees with unique names.
- Settings shall be saved when the application shuts down.
- The last viewed position in the log file shall be saved so that the user can resume from where they left off.
