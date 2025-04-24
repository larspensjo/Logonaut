# Logonaut: Reactive Filtering with LogFilterProcessor (Simplified)

## Overview

Logonaut uses a dedicated `LogFilterProcessor` service ([`LogFilterProcessor.cs`](../src/Logonaut.Core/LogFilterProcessor.cs)) to manage log filtering reactively using Rx.NET.

## Core Logic

1.  **Full Re-filtering:** Instead of incremental updates, the processor **always re-filters the entire log document** snapshot ([`LogDocument`](../src/Logonaut.Common/LogDocument.cs)).
2.  **Triggers:** A full re-filter is triggered whenever:
    *   The active filter settings are changed (`UpdateFilterSettings` called by [`MainViewModel`](../src/Logonaut.UI/ViewModels/MainViewModel.cs)).
    *   A new log line arrives from the file tailer ([`ILogTailerService`](../src/Logonaut.Core/ILogTailerService.cs)).
3.  **Debouncing:** Uses Rx.NET's `Throttle` operator to prevent excessive re-filtering during rapid triggers (like fast log updates or quick UI changes).
4.  **Background Processing:** Filtering executes on a background thread to keep the UI responsive.
5.  **Results:** After filtering, the processor sends the *complete, updated list* of filtered lines ([`FilteredUpdate`](../src/Logonaut.Core/FilteredUpdate.cs)) back to the `MainViewModel` on the UI thread.
6.  **UI Update:** The `MainViewModel` receives this complete list and updates the display accordingly.