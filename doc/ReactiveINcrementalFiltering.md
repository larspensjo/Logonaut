# Logonaut: Reactive and Incremental Filtering

## Overview

To improve performance and UI responsiveness, especially with large or rapidly updating log files, Logonaut moved from a polling-based background filtering mechanism to a reactive approach using System.Reactive (Rx.NET). This new approach handles incoming log lines incrementally and re-filters the entire document efficiently when filter settings change, incorporating debouncing to prevent excessive recalculations.

## Core Concepts

The filtering logic is primarily managed within `MainViewModel` and relies on two main reactive pipelines:

1.  **Incremental Filtering Pipeline:** Handles new lines arriving from the `LogTailerManager`.
2.  **Full Re-Filtering Pipeline:** Handles changes to the filter configuration.

Both pipelines leverage background threads for processing and ensure UI updates happen safely on the main UI thread.

## 1. Incremental Filtering Pipeline (New Log Lines)

This pipeline processes new log lines as they arrive without re-scanning the entire document each time.

1.  **Source:** The `LogTailerManager.Instance.LogLines` observable stream provides new lines as they are read from the file.
2.  **Enrichment:** As each line arrives (`Select` operator), its original line number is captured using `Interlocked.Increment`, and the line is added to the main `LogDocument`. This happens *before* buffering.
3.  **Buffering:** New lines (with their original numbers) are buffered (`Buffer` operator) for a short duration (`_lineBufferTimeSpan`) or until a certain number of lines (`LineBufferSize`) accumulate. Buffering runs on a background thread (`_backgroundScheduler`). This avoids processing every single line individually, improving efficiency.
4.  **Filtering the Buffer:** When a buffer is released (`Select` operator), the *current* filter configuration (`GetCurrentFilter()`) is applied only to the lines within that buffer on the background thread. Lines matching the filter are converted into `FilteredLogLine` objects.
5.  **UI Update:** The list of matching `FilteredLogLine` objects is sent to the UI thread (`ObserveOn(_uiContext)` using the captured `SynchronizationContext`). The `Subscribe` action then calls `AddFilteredLines`, which appends these new matching lines to the `FilteredLogLines` `ObservableCollection`.
6.  **Text View Update:** `AddFilteredLines` schedules an update (`ScheduleLogTextUpdate`) to rebuild the `LogText` property (containing only the text content) for the AvalonEdit control.

**Result:** New matching lines appear at the end of the view relatively quickly without recalculating the entire filtered set.

## 2. Full Re-Filtering Pipeline (Filter Changes)

This pipeline recalculates the entire filtered view when the user modifies the filter tree.

1.  **Trigger:** Changes in the filter UI (adding, removing, enabling/disabling, finishing an edit in `FilterViewModel`) invoke the `TriggerFullRefilter` method via a callback action passed down during `FilterViewModel` creation.
2.  **Signaling:** `TriggerFullRefilter` pushes a notification onto the `_filterChangedSubject` (a `Subject<Unit>`).
3.  **Debouncing:** The pipeline listens to `_filterChangedSubject` but uses the `Throttle` (or `Debounce`) operator on a background thread (`_backgroundScheduler`). This ensures that rapid changes (e.g., typing quickly in a filter box) don't trigger a full re-filter for every single change, but only after a brief pause (`_filterDebounceTime`).
4.  **Full Filter Application:** Once a debounced signal is received (`Select` operator), the *current* filter configuration (`GetCurrentFilter()`) is applied to a snapshot of the *entire* `LogDocument` using `FilterEngine.ApplyFilters`. This intensive work happens on a background thread.
5.  **UI Update:** The complete new list of `FilteredLogLine` objects is sent to the UI thread (`ObserveOn(_uiContext)`). The `Subscribe` action then calls `ReplaceFilteredLines`.
6.  **Collection Replacement:** `ReplaceFilteredLines` clears the existing `FilteredLogLines` `ObservableCollection` and adds all items from the new results.
7.  **Text View Update:** `ReplaceFilteredLines` schedules an update (`ScheduleLogTextUpdate`) to rebuild the `LogText` property for the AvalonEdit control based on the new collection content.

**Result:** The UI remains responsive during filter modifications, and the view updates efficiently only after the user pauses input.

## Updating AvalonEdit (`LogText`)

Both pipelines need to update the `LogText` property bound to AvalonEdit. Directly updating `LogText` immediately after modifying the `FilteredLogLines` `ObservableCollection` can lead to `"Collection was modified"` errors, because the collection might still be triggering internal updates or UI bindings while the `LogText` generation tries to enumerate it.

To prevent this:
*   The `AddFilteredLines` and `ReplaceFilteredLines` methods now call `ScheduleLogTextUpdate`.
*   `ScheduleLogTextUpdate` uses `_uiContext.Post` to queue the actual update (`UpdateLogTextInternal`) to run *after* the current UI operation completes.
*   A flag (`_logTextUpdateScheduled`) prevents queuing multiple updates if signals arrive rapidly.
*   `UpdateLogTextInternal` performs the `FilteredLogLines.Select(...).ToList()` and `string.Join(...)` logic to safely update the `LogText` property.

## Key Technologies Used

*   **System.Reactive (Rx.NET):** For managing asynchronous streams of data (log lines, filter changes).
*   **Observables/Subjects:** `IObservable<string>` from `LogTailerManager`, `Subject<Unit>` for filter changes.
*   **Operators:** `Select`, `Buffer`, `Where`, `Throttle`/`Debounce`, `ObserveOn`, `Subscribe`.
*   **Schedulers:** `TaskPoolScheduler` for background work.
*   **SynchronizationContext:** To marshal results back to the UI thread safely.
*   **ObservableCollection:** For the `FilteredLogLines` to automatically notify the UI (specifically the custom line number margin's binding) of changes.

This reactive architecture provides a more robust, performant, and responsive filtering experience compared to the previous polling mechanism.