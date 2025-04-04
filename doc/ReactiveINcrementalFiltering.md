# Logonaut: Reactive Filtering with LogFilterProcessor

## Overview

To enhance performance, UI responsiveness, and architectural clarity, Logonaut moved its log filtering logic from the `MainViewModel` into a dedicated service, `LogFilterProcessor`, located in the `Logonaut.Core` project. This service utilizes System.Reactive (Rx.NET) to handle incoming log lines incrementally and re-filter the entire document efficiently when filter settings change. It incorporates debouncing to prevent excessive recalculations during rapid configuration changes.

## Core Component: `LogFilterProcessor`

The `LogFilterProcessor` is the heart of the reactive filtering system. Its primary responsibilities include:

1.  **Encapsulating Reactive Pipelines:** Managing the Rx.NET observables and operators for both incremental and full re-filtering.
2.  **Managing Log Data:** Interacting with the central `LogDocument` to append new lines and retrieve snapshots for full filtering.
3.  **Handling Threading:** Performing expensive filtering operations on background threads (`TaskPoolScheduler`) and marshalling results safely back to the UI thread using a provided `SynchronizationContext`.
4.  **Debouncing:** Preventing excessive full re-filters when filter settings change rapidly (e.g., user typing).
5.  **State Management:** Keeping track of the current filter (`IFilter`), context lines setting, and the original line number counter (`_currentLineIndex`).

### Inputs & Outputs

*   **Inputs:**
    *   `IObservable<string> rawLogLines`: The stream of raw log lines from `LogTailerManager`.
    *   `LogDocument logDocument`: A reference to the shared `LogDocument` instance where all original lines are stored.
    *   `SynchronizationContext uiContext`: Used to marshal results back to the UI thread.
    *   `(Optional) IScheduler backgroundScheduler`: Defaults to `TaskPoolScheduler.Default`.
*   **Output:**
    *   `IObservable<FilteredUpdate> FilteredUpdates`: Emits updates containing filtered log lines destined for the UI.
*   **Control Methods:**
    *   `UpdateFilterSettings(IFilter newFilter, int contextLines)`: Signals that the filter or context setting has changed, triggering a (debounced) full re-filter.
    *   `Reset()`: Clears the internal state (`LogDocument`, line counter) and emits an empty update, used when loading a new file.

## `FilteredUpdate` Record

Communication between the `LogFilterProcessor` and the `MainViewModel` happens via the `FilteredUpdate` record:
```csharp
public record FilteredUpdate(UpdateType Type, IReadOnlyList<FilteredLogLine> Lines);

public enum UpdateType { Replace, Append }
```
*   `Type`: Indicates whether the `Lines` should *replace* the entire current view (`Replace`) or be *appended* to the end (`Append`).
*   `Lines`: The list of `FilteredLogLine` objects resulting from the filtering operation.

## Reactive Pipelines within `LogFilterProcessor`

The service manages two primary reactive pipelines internally:

### 1. Incremental Filtering Pipeline (New Log Lines)

This pipeline processes new log lines as they arrive without re-scanning the entire document each time.

1.  **Source:** The `_rawLogLines` observable stream provides new lines.
2.  **Enrichment & Storage:** As each line arrives (`Select` operator), its original line number is captured using `Interlocked.Increment`, and the line is **appended to the shared `_logDocument`**. This happens *before* buffering.
3.  **Buffering:** New lines (with their captured original numbers) are buffered (`Buffer` operator) for a short duration (`_lineBufferTimeSpan`) or until a certain number (`LineBufferSize`) accumulate. Buffering runs on the `_backgroundScheduler`.
4.  **Filtering the Buffer:** When a buffer is released (`Select` operator), the *currently cached* filter (`_currentFilter`) is applied *only* to the lines within that buffer using the `ApplyIncrementalFilter` method on the background thread.
    *   *Note:* This incremental step primarily checks if new lines match the filter. It does **not** currently add context lines from the past; context is handled comprehensively by the full re-filter. Matching lines are converted into `FilteredLogLine` objects.
5.  **Output Generation:** The list of matching `FilteredLogLine` objects is packaged into a `FilteredUpdate(UpdateType.Append, matchedLines)`.
6.  **UI Marshalling:** The `FilteredUpdate` is sent to the UI thread (`ObserveOn(_uiContext)`).
7.  **Emission:** The update is pushed onto the `_filteredUpdatesSubject`.

**Result:** New matching lines appear at the end of the view relatively quickly without recalculating the entire filtered set.

### 2. Full Re-Filtering Pipeline (Filter Changes)

This pipeline recalculates the entire filtered view when triggered by filter or context setting changes.

1.  **Trigger:** Calls to `LogFilterProcessor.UpdateFilterSettings(newFilter, newContext)` push the new settings onto the `_filterSettingsSubject`.
2.  **State Update:** The processor immediately updates its internal `_currentFilter` and `_currentContextLines` cache (`Do` operator).
3.  **Debouncing:** The pipeline listens to `_filterSettingsSubject` but uses the `Throttle` operator on the `_backgroundScheduler`. This ensures that rapid calls to `UpdateFilterSettings` don't trigger a full re-filter for every single change, but only after a brief pause (`_filterDebounceTime`).
4.  **Full Filter Application:** Once a debounced signal is received (`Select` operator), the `ApplyFullFilter` method is called. This method retrieves a snapshot of the *entire* `_logDocument` and applies the *latest* filter settings (`_currentFilter`, `_currentContextLines`) using `FilterEngine.ApplyFilters`. This intensive work happens on the background thread.
5.  **Output Generation:** The complete new list of `FilteredLogLine` objects is packaged into a `FilteredUpdate(UpdateType.Replace, allFilteredLines)`.
6.  **UI Marshalling:** The `FilteredUpdate` is sent to the UI thread (`ObserveOn(_uiContext)`).
7.  **Emission:** The update is pushed onto the `_filteredUpdatesSubject`.

**Result:** The UI remains responsive during filter modifications, and the view updates efficiently only after the user pauses input or changes settings.

## ViewModel Integration (`MainViewModel`)

The `MainViewModel` is now significantly simpler regarding filtering logic:

1.  **Instantiation:** It creates an instance of `LogFilterProcessor`, passing the required dependencies (`LogTailerManager.Instance.LogLines`, its own `LogDoc` instance, `SynchronizationContext.Current`).
2.  **Subscription:** It subscribes to the `_logFilterProcessor.FilteredUpdates` observable.
3.  **Applying Updates:** The `Subscribe` action calls `ApplyFilteredUpdate(update)`, which checks the `update.Type`:
    *   If `UpdateType.Replace`, it calls `ReplaceFilteredLines(update.Lines)` to clear and repopulate the `FilteredLogLines` `ObservableCollection`.
    *   If `UpdateType.Append`, it calls `AddFilteredLines(update.Lines)` to append to the `FilteredLogLines` `ObservableCollection`.
4.  **Triggering Re-filters:** When the filter configuration changes in the UI (e.g., adding/removing/editing filters via `FilterViewModel` which uses a callback, or changing the `ContextLines` property), `MainViewModel` calls `_logFilterProcessor.UpdateFilterSettings(GetCurrentFilter(), ContextLines)`.
5.  **Resetting:** When a new file is opened (`OpenLogFile` command), `MainViewModel` calls `_logFilterProcessor.Reset()` before changing the file in `LogTailerManager`.

## Updating AvalonEdit (`LogText`)

The mechanism for updating the `LogText` property (bound to AvalonEdit's content) remains within `MainViewModel` because it directly depends on the state of the `FilteredLogLines` collection *after* it has been updated on the UI thread.

*   The `AddFilteredLines` and `ReplaceFilteredLines` methods (called via the processor's subscription) still use `ScheduleLogTextUpdate`.
*   `ScheduleLogTextUpdate` uses `_uiContext.Post` to queue the actual update (`UpdateLogTextInternal`) to run *after* the collection modification and related UI notifications have settled.
*   This prevents potential "Collection was modified" errors during the `string.Join` operation.

## Key Technologies Used

*   **System.Reactive (Rx.NET):** For managing asynchronous streams and orchestrating filtering logic within `LogFilterProcessor`.
*   **`LogFilterProcessor`:** The dedicated service encapsulating filtering logic.
*   **`FilteredUpdate` Record:** Data structure for communicating updates.
*   **Observables/Subjects:** `IObservable<string>` (input), `Subject<(IFilter, int)>` (filter changes), `BehaviorSubject<FilteredUpdate>` (output).
*   **Operators:** `Select`, `Buffer`, `Where`, `Throttle`, `ObserveOn`, `Subscribe`, `Do`.
*   **Schedulers:** `TaskPoolScheduler` (or specified background scheduler).
*   **SynchronizationContext:** To marshal results back to the UI thread safely.
*   **`ObservableCollection<FilteredLogLine>`:** In `MainViewModel`, for notifying the UI (custom margin, list controls) of changes.
*   **`LogDocument`:** Central, thread-safe storage for all original log lines.
*   **`FilterEngine.ApplyFilters`:** Core, synchronous filtering logic used by `LogFilterProcessor`.

## Benefits

This architecture provides:

*   **Improved Separation of Concerns:** Complex reactive/threading logic is isolated in `Logonaut.Core`, distinct from UI state management in `Logonaut.UI`.
*   **Enhanced Testability:** `LogFilterProcessor` can be unit tested more easily by mocking its dependencies. `MainViewModel` becomes simpler to test.
*   **Better Performance/Responsiveness:** Heavy lifting occurs on background threads, and debouncing prevents unnecessary work, keeping the UI fluid.
*   **Increased Maintainability:** Code is better organized and easier to understand and modify.