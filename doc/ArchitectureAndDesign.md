# Logonaut: Architecture and Design

This document describes the specific architectural choices and high-level design decisions for the Logonaut application. For general design principles, see `GeneralDesignPrinciples.md`.

## Module Overview

Logonaut is organized into several independent modules:

*   **Logonaut.UI:** Contains the WPF user interface (Views, ViewModels using CommunityToolkit.Mvvm), custom controls, attached properties for AvalonEdit integration, and UI services (like file dialogs). Manages the presentation layer and user interaction logic.
*   **Logonaut.Core:** Provides core business logic, including the `FilterEngine` for applying filter rules and the reactive `LogFilterProcessor` for orchestrating log processing and filtering. Also includes settings management.
*   **Logonaut.Filters:** Implements the filtering system, defining various filter types (`SubstringFilter`, `RegexFilter`, `AndFilter`, `OrFilter`, `NorFilter`, `TrueFilter`) and serialization logic for filter trees.
*   **Logonaut.LogTailing:** Handles asynchronous log file reading and tailing using `LogTailer` and `LogTailerManager`, exposing new log lines as an `IObservable<string>` via Reactive Extensions (Rx.NET).
*   **Logonaut.Theming:** Manages application theming (Light/Dark modes) and associated styles.
*   **Logonaut.Common:** Contains common data structures (`LogDocument`, `FilteredLogLine`, `LogonautSettings`, `FilterProfile`) shared across modules.
*   **Tests:** Unit tests for each module using MSTest.

## Key Design Decisions

### Filtering System

*   **Named Filter Profiles:** Users can create, save, name, and switch between multiple distinct filter configurations (profiles). This allows tailoring filters for different log types or analysis tasks.
*   **Profile Management UI:** A `ComboBox` located in the filter panel is used for selecting the active profile. Associated buttons allow creating, renaming, and deleting profiles. (This follows Alternative A from the design discussion).
*   **Reactive Processing:** Filtering is handled reactively using `System.Reactive` within the `LogFilterProcessor`. This service manages background processing, incremental updates for new lines, debounced full re-filters on configuration changes, and safe marshalling of results to the UI thread. See `ReactiveIncrementalFiltering.md` for details.
*   **Filter Engine:** The `FilterEngine` provides the core synchronous logic for applying a given filter tree and context lines to a snapshot of the log document.

### Highlighting System

*   **Programmatic Control:** Highlighting rules (timestamps, levels, filter matches, search terms) are managed programmatically via a custom `IHighlightingDefinition` (`CustomHighlightingDefinition`). This allows dynamic updates based on user configuration and application state, rather than relying solely on static XSHD files.
*   **Dynamic Updates:** Highlighting updates in real-time as filter configurations change or search terms are entered.
*   **Extensibility:** The system is designed to allow adding new types of highlighting rules easily.

### Log Display

*   **AvalonEdit:** The powerful AvalonEdit control is used for displaying log content, providing features like virtualization and syntax highlighting support.
*   **Custom Margins:**
    *   `OriginalLineNumberMargin`: Displays the original line number from the source file, driven by `FilteredLogLine` data. See `LineNumberManagement.md`.
    *   `OverviewRulerMargin`: Replaces the standard vertical scrollbar, providing a visual map of the filtered document with markers for search results (and potentially other points of interest). It controls scrolling via events. See `OverviewRulerFlow.md`.
    *   `ChunkSeparatorRenderer`: Draws visual lines between non-contiguous log chunks resulting from filtering with context.

### Persistence

*   **JSON Settings:** Application settings, including all filter profiles and the last active profile name, are serialized to a JSON file (`settings.json`) in the user's local application data folder (`%LocalAppData%\Logonaut`). Newtonsoft.Json is used for serialization, handling the `IFilter` polymorphism via `TypeNameHandling.All`.

### Data Flow

*   Detailed data flow for log processing and filtering is described in `LogFileProcessingFlow.md`.