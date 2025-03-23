# Logonaut: Log File Processing Flow

## Complete Data Flow
1. **User selects a log file** → `OpenLogFile` command → `LogTailerManager.ChangeFile`.
2. **LogTailer monitors the file** → Detects changes → Reads new lines.
3. **New lines flow through** → Observable stream → `MainViewModel` → `LogDocument.AppendLine`.
4. **Background filtering process** → Applies filter tree → Produces filtered lines.
5. **UI updates** → `VisibleLogLines` collection → `UpdateLogText` → Display.

This architecture provides a responsive, modular system for monitoring and filtering log files in real-time, with a clean separation of concerns between file monitoring, data storage, filtering, and UI presentation.

## Log File Loading and Updating

### 1. Log File Selection
- Users select a log file through the UI using the `OpenLogFile` command in `MainViewModel`.
- The file path is passed to `LogTailerManager.Instance.ChangeFile(selectedFile)`.
- The `LogTailerManager` is a singleton that manages the current log file being monitored.

### 2. Log File Monitoring (`LogTailer`)
- `LogTailer` is responsible for monitoring a specific log file for changes.
- It uses `FileSystemWatcher` to detect when the file is modified.

#### Key components:
- **Constructor**: Validates the file exists and is accessible.
- **Start()**: Initializes monitoring with `FileSystemWatcher`.
- **OnFileChanged**: Event handler triggers when file changes are detected.
- **ReadNewLinesAsync**: Reads new content from the last known position.

### 3. Log Data Flow
- New log lines are exposed as an `IObservable<string>` stream using Reactive Extensions.
- When new lines are read, they're published through a `Subject<string>`.
- The `LogTailerManager` subscribes to this stream and republishes it to application components.
- `MainViewModel` subscribes to the manager's stream and appends lines to `LogDocument`.

### 4. Log Storage (`LogDocument`)
- `LogDocument` is a thread-safe container for log lines.
- It provides methods for:
    - **AppendLine**: Adding individual lines.
    - **AddInitialLines**: Loading multiple lines at once.
    - **GetLines**: Retrieving subsets of lines.
    - **Indexed access**: Accessing specific lines.
- Thread safety is ensured through locking.

## Log Filtering System

### 1. Filter Types

#### Simple Filters:
- **SubstringFilter**: Matches lines containing a specific substring.
- **RegexFilter**: Matches lines using regular expressions.

#### Composite Filters:
- **AndFilter**: Matches when all child filters match (logical AND).
- **OrFilter**: Matches when any child filter matches (logical OR).
- **NorFilter**: Matches when no child filters match (logical NOR).

#### Default Filter:
- **TrueFilter**: Always returns true (used when no filters are defined).

### 2. Filter Hierarchy
- Filters are organized in a hierarchical tree structure.
- The UI allows users to add, remove, and edit filters.
- Composite filters can contain other filters (simple or composite).
- This enables complex expressions like `(A or (B and not C))`.

### 3. Filter Processing (`FilterEngine`)
- `FilterEngine.ApplyFilters` processes the entire log against the filter tree.
- For each line, it checks if the line matches the filter criteria.
- If a match is found, it includes the line and optional context lines.
- Context lines are lines before and after the matching line.
- Duplicate lines are avoided in the final output.

### 4. Background Filtering
- Filtering runs on a background thread to keep the UI responsive.
- `StartBackgroundFiltering` in `MainViewModel` creates a continuous filtering loop.
- Every 250ms, it:
    1. Gets the current filter tree (or uses `TrueFilter` if none exists).
    2. Applies the filter to the entire log document.
    3. Updates the UI with the filtered results.
- The filtered lines are stored in the `VisibleLogLines` collection.
- `UpdateLogText` converts these lines to a single string for display.

### 5. Filter Highlighting
- `UpdateFilterSubstrings` collects all substrings and regex patterns from filters.
- These are used to highlight matching text in the log display.
    - For substring filters, the text is escaped for regex.
    - For regex filters, the pattern is used directly.
