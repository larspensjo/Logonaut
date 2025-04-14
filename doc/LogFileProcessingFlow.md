# Logonaut: Log File Processing Flow

This document outlines the high-level flow of data from log file monitoring through filtering to display within the Logonaut application.

## Key Components

*   **`LogTailerManager` (`Logonaut.LogTailing`):** Manages the active log file monitoring (`LogTailer`) and provides an observable stream of new log lines. ([`LogTailerManager.cs`](../src/Logonaut.LogTailing/LogTailerManager.cs))
*   **`LogDocument` (`Logonaut.Common`):** Thread-safe storage for the complete, original log file content. ([`LogDocument.cs`](../src/Logonaut.Common/LogDocument.cs))
*   **`ILogFilterProcessor` (`Logonaut.Core`):** Service responsible for orchestrating the reactive filtering pipeline. Subscribes to raw log lines and filter change triggers. ([`ILogFilterProcessor.cs`](../src/Logonaut.Core/ILogFilterProcessor.cs), [`LogFilterProcessor.cs`](../src/Logonaut.Core/LogFilterProcessor.cs))
*   **`FilterEngine` (`Logonaut.Core`):** Contains the synchronous logic to apply a filter tree to a log snapshot. ([`FilterEngine.cs`](../src/Logonaut.Core/FilterEngine.cs))
*   **`FilterProfile` (`Logonaut.Common`):** Holds the definition (name and filter tree) for a saved filter configuration. ([`FilterProfile.cs`](../src/Logonaut.Common/FilterProfile.cs))
*   **`MainViewModel` (`Logonaut.UI.ViewModels`):** Orchestrates UI state, handles user commands, manages filter profiles, receives processed updates, and prepares data for display. ([`MainViewModel.cs`](../src/Logonaut.UI/ViewModels/MainViewModel.cs))
*   **`FilteredLogLine` (`Logonaut.Common`):** Represents a line that passed filtering, holding its text and original line number. ([`FilteredLogLine.cs`](../src/Logonaut.Common/FilteredLogLine.cs))
*   **AvalonEdit `TextEditor` & Custom Margins (`Logonaut.UI`):** Displays the filtered log text and custom line numbers/markers. ([`MainWindow.xaml`](../src/Logonaut.UI/MainWindow.xaml), [`OriginalLineNumberMargin.cs`](../src/Logonaut.UI/Helper/OriginalLineNumberMargin.cs))

## High-Level Data Flow

1.  **File Monitoring:** `LogTailerManager` monitors the selected log file. When changes occur, it reads new lines and publishes them via an `IObservable<string>`.
2.  **Input Subscription:** `LogFilterProcessor` subscribes to the `LogTailerManager`'s observable stream.
3.  **Line Processing & Storage:** As new lines arrive, `LogFilterProcessor`:
    *   Assigns an original line number.
    *   Appends the line to the central `LogDocument`.
    *   Buffers incoming lines.
4.  **Incremental Filtering (Rx Pipeline):** The buffered *new* lines are processed (on a background thread) against the *currently active filter profile's rules* (cached within the processor). Matching lines generate an `Append` type `FilteredUpdate`.
5.  **Full Re-Filtering (Rx Pipeline):** When the active `FilterProfile` or its settings change (signaled by `MainViewModel` calling `UpdateFilterSettings`), `LogFilterProcessor`:
    *   Debounces the trigger to avoid excessive work.
    *   Retrieves a snapshot of the *entire* `LogDocument`.
    *   Calls `FilterEngine.ApplyFilters` (on a background thread) using the active profile's filter tree and context setting.
    *   Generates a `Replace` type `FilteredUpdate` containing the complete new set of filtered lines.
6.  **UI Update Notification:** `LogFilterProcessor` pushes the `FilteredUpdate` (Append or Replace) onto its `FilteredUpdates` observable, ensuring delivery on the UI thread via the `SynchronizationContext`.
7.  **ViewModel Update:** `MainViewModel` subscribes to `FilteredUpdates` and updates its `FilteredLogLines` `ObservableCollection` accordingly (clearing and adding for `Replace`, just adding for `Append`).
8.  **Display Rendering:**
    *   Changes to `FilteredLogLines` notify the custom `OriginalLineNumberMargin` via data binding.
    *   `MainViewModel` schedules (`_uiContext.Post`) an update to its `LogText` property.
    *   `UpdateLogTextInternal` generates the joined string from `FilteredLogLines`.
    *   The change to `LogText` updates the AvalonEdit `TextEditor` content via data binding.
    *   AvalonEdit and the custom margins render the filtered text and corresponding original line numbers.

## Key Transitions

*   **Application Start:** Loads settings, including all saved `FilterProfile`s and the last active profile name. `MainViewModel` initializes, selects the active profile, and triggers the initial filter application.
*   **Opening New File:** `MainViewModel` calls `LogFilterProcessor.Reset()` (clearing its state and `LogDocument`), updates the `LogTailerManager`, and then triggers a filter application using the *currently selected profile* on the (now empty or soon-to-be-filled) document.
*   **Changing Active Profile:** `MainViewModel` updates its `ActiveFilterProfile`, which triggers `UpdateActiveTreeRootNodes` (for the TreeView) and `TriggerFilterUpdate` (passing the *new* profile's filter to `LogFilterProcessor`).
*   **Modifying Active Filter Tree:** User interactions modify the `FilterViewModel` tree. Callbacks from `FilterViewModel` invoke `MainViewModel.TriggerFilterUpdate`, passing the *modified filter tree* of the *still active* profile to `LogFilterProcessor`.

This flow ensures that file I/O and filtering occur off the UI thread, results are marshalled back safely, and the UI remains responsive. See [ReactiveIncrementalFiltering.md](ReactiveIncrementalFiltering.md) for more detail on the reactive pipeline specifics.