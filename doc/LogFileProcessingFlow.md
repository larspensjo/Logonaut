# Logonaut: Log File Processing Flow

## Complete Data Flow
1.  **User selects a log file** → `OpenLogFile` command → `LogTailerManager.ChangeFile`.
2.  **LogTailer monitors the file** → Detects changes → Reads new lines → Publishes via `IObservable<string>`.
3.  **New lines flow through** → `LogTailerManager.Instance.LogLines` → `LogFilterProcessor`.
4.  **`LogFilterProcessor`** → Appends line to `LogDocument` → Processes line via incremental Rx pipeline → Applies current filter to buffered new lines.
5.  **Filter Settings Changed** → `MainViewModel` calls `LogFilterProcessor.UpdateFilterSettings()` → `LogFilterProcessor` triggers (debounced) full re-filter using `FilterEngine.ApplyFilters` on `LogDocument` snapshot.
6.  **`LogFilterProcessor`** → Generates `FilteredUpdate` (Replace or Append) → Pushes update to `MainViewModel` via `FilteredUpdates` observable (on UI thread).
7.  **`MainViewModel` subscribes** → Receives `FilteredUpdate` → Updates `FilteredLogLines` collection (`ObservableCollection<FilteredLogLine>`).
8.  **UI Updates** → `FilteredLogLines` change notifies custom line number margin → `MainViewModel` schedules `LogText` update → `LogText` property updates AvalonEdit content → Display.

This architecture provides a responsive, modular system for monitoring and filtering log files in real-time, with a clean separation of concerns between file monitoring, data storage, reactive processing/filtering, and UI presentation.

## Log File Loading and Updating

### 1. Log File Selection
- Users select a log file through the UI using the `OpenLogFile` command in `MainViewModel`.
- The file path is passed to `LogTailerManager.Instance.ChangeFile(selectedFile)`.
- The `LogTailerManager` is a singleton managing the current `LogTailer`.
- Before changing the file, `MainViewModel` calls `LogFilterProcessor.Reset()` to clear internal state and UI collections.

### 2. Log File Monitoring (`LogTailer`)
- `LogTailer` monitors a specific log file for changes using `FileSystemWatcher`.
- It reads new content asynchronously from the last known position.
- New log lines are exposed as an `IObservable<string>` stream using Reactive Extensions (Rx.NET).

### 3. Log Data Flow
- New lines are published via `LogTailer.LogLines`.
- `LogTailerManager` subscribes and republishes these lines via its own `LogLines` observable.
- `LogFilterProcessor` subscribes to `LogTailerManager.Instance.LogLines`.

### 4. Log Storage (`LogDocument`)
- `LogDocument` is a thread-safe container holding *all* original log lines.
- `LogFilterProcessor` appends incoming lines to this document (`AppendLine`).
- `LogFilterProcessor` reads snapshots (`ToList()`) from `LogDocument` when performing full re-filters.
- Thread safety is ensured through locking within `LogDocument`.

## Log Filtering System

### 1. Filter Types & Hierarchy
- Filter types (Substring, Regex, And, Or, Nor, True) remain the same.
- Filters are organized in a hierarchical tree managed by `MainViewModel` and `FilterViewModel`.
- `MainViewModel` holds the root `FilterProfiles` (usually one tree).

### 2. Filter Processing (`LogFilterProcessor` & `FilterEngine`)
- **Reactive Orchestration (`LogFilterProcessor` in `Logonaut.Core`):**
    - Manages the Rx pipelines for filtering.
    - Handles incoming lines incrementally (buffering, applying current filter to new lines).
    - Handles filter changes (debouncing, triggering full re-filters).
    - Performs work on background threads.
    - Marshals results (`FilteredUpdate`) back to the UI thread.
- **Core Filtering Logic (`FilterEngine` in `Logonaut.Core`):**
    - Contains the static `ApplyFilters` method.
    - Takes a `LogDocument` (snapshot), an `IFilter` tree, and `contextLines`.
    - Iterates through the lines, applies the filter logic, includes context lines, ensures uniqueness, and returns `IReadOnlyList<FilteredLogLine>`.
    - This method is called by `LogFilterProcessor` during a full re-filter.

### 3. Filtering Execution Flow
- **Incremental:** New lines arrive → `LogFilterProcessor` buffers them → Applies *current* filter to the buffer → Emits an `Append` type `FilteredUpdate` with matching lines.
- **Full Re-filter:** User changes filter UI → `FilterViewModel` callback → `MainViewModel.TriggerFilterUpdate()` → Calls `LogFilterProcessor.UpdateFilterSettings()` → Processor updates internal filter state → Debounces → Calls `FilterEngine.ApplyFilters` on `LogDocument` snapshot → Emits a `Replace` type `FilteredUpdate` with the complete new result set.

### 4. UI Updates
- `MainViewModel` receives `FilteredUpdate` objects on the UI thread.
- It updates the `FilteredLogLines` `ObservableCollection` accordingly (clearing + adding for `Replace`, just adding for `Append`).
- The change to `FilteredLogLines` automatically updates the custom `OriginalLineNumberMargin` via data binding.
- `MainViewModel` then calls `ScheduleLogTextUpdate`, which posts `UpdateLogTextInternal` to the UI thread's queue.
- `UpdateLogTextInternal` safely reads the `FilteredLogLines`, extracts the text, joins it, and updates the `LogText` property bound to AvalonEdit.

### 5. Filter Highlighting (`MainViewModel` & `AvalonEditHelper`)
- When filters change (`TriggerFilterUpdate` is called), `MainViewModel` also executes `UpdateFilterSubstringsCommand`.
- This command traverses the *current* filter tree in `FilterProfiles` (`MainViewModel`) to collect all active substring/regex patterns.
- It updates the `FilterSubstrings` property (an `ObservableCollection<string>`).
- Data binding triggers `AvalonEditHelper.OnFilterSubstringsChanged`.
- `AvalonEditHelper` updates the `CustomHighlightingDefinition` with the new patterns.
- AvalonEdit redraws, applying the highlighting rules.