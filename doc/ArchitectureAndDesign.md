# Logonaut: Architecture and Design

This document describes the specific architectural choices and high-level design decisions for the Logonaut application. For general design principles, see `GeneralDesignPrinciples.md`.

## Module Overview

Logonaut is organized into several independent modules:

*   **Logonaut.UI:** Contains the WPF user interface (Views, ViewModels using CommunityToolkit.Mvvm), custom controls, attached properties for AvalonEdit integration, and UI services (like file dialogs). Manages the presentation layer and user interaction logic. Key helpers include:
    *   `AvalonEditHelper`: Attached properties for binding text, selection, and dynamic highlighting rules.
    *   `OriginalLineNumberMargin`: Custom margin displaying original log line numbers.
    *   `OverviewRulerMargin`: Custom scrollbar replacement showing document overview and markers.
    *   `ChunkSeparatorRenderer`: Draws visual lines between non-contiguous log chunks.
    *   `SelectedIndexHighlightTransformer`: `DocumentColorizingTransformer` for highlighting a specific selected line.
    *   `BusyIndicator`: Custom control displaying animations for background activity. Activated by adding state tokens to its bound `ActiveStates` collection (replaces `AnimatedSpinner`). Visuals currently default to a spinning arc, planned for evolution.
*   **Logonaut.Core:** Provides core business logic, including the `FilterEngine` for applying filter rules and the reactive `LogFilterProcessor` for orchestrating log processing and filtering. Also includes settings management.
*   **Logonaut.Filters:** Implements the filtering system, defining various filter types (`SubstringFilter`, `RegexFilter`, `AndFilter`, `OrFilter`, `NorFilter`, `TrueFilter`) and serialization logic for filter trees.
*   **Logonaut.LogTailing:** Handles asynchronous log file reading and tailing using `LogTailer` and `LogTailerService`, exposing new log lines as an `IObservable<string>` via Reactive Extensions (Rx.NET).
*   **Logonaut.Theming:** Manages application theming (Light/Dark modes) and associated styles.
*   **Logonaut.Common:** Contains common data structures (`LogDocument`, `FilteredLogLine`, `LogonautSettings`, `FilterProfile`) shared across modules.
*   **Tests:** Unit tests for each module using MSTest.

## Graphical Identity

### Application Icon Concept: Flowing Neon Lines

*   **Core Idea:** The primary application icon (.ico file) will feature a stylized representation of log lines rendered in the application's primary neon accent color (e.g., cyan/blue).
*   **Visual:** These lines should appear dynamic, perhaps slightly curved, wavy, or like a data stream, suggesting live tailing and analysis.
*   **Context:** This could be integrated with a simple, modern document shape or a terminal prompt symbol (>_) to provide context.
*   **Implementation:** A multi-resolution `.ico` file containing standard sizes (16x16, 32x32, 48x48, 256x256) will be created to ensure clarity across different Windows UI contexts (Taskbar, Explorer, Title Bar, etc.).

### Extended Graphical Identity

The "Flowing Neon Lines" motif and the core neon accent color will be consistently applied in other areas to reinforce the application's visual identity:

*   **Splash Screen:** If implemented, it will feature the main logo prominently against a theme-appropriate background (dark or light).
*   **About Dialog:** The dialog will display the application icon alongside version and copyright information.
*   **Busy Indicator:** The custom `BusyIndicator` control will eventually incorporate the "Flowing Neon Lines" concept or other state-specific animations. Different busy states (e.g., file loading vs. filtering) will be represented by distinct visual feedback (overlay or external indicator). See [BusyIndicatorPlan.md](BusyIndicatorPlan.md) for evolution details.
*   **Installer:** Visual assets used in the installer will use the application logo and neon branding.
*   **(Future) File Associations:** Icons for associated file types could incorporate the flowing lines motif with a document symbol.

## Key Design Decisions

### Filtering System

*   **Named Filter Profiles:** Users can create, save, name, and switch between multiple distinct filter configurations (profiles). This allows tailoring filters for different log types or analysis tasks.
*   **Profile Management UI:** A `ComboBox` located in the filter panel is used for selecting the active profile. Associated buttons allow creating, renaming, and deleting profiles.
*   **Reactive Processing:** Filtering is handled reactively using `System.Reactive` within the `LogFilterProcessor`. This service manages background processing, full re-filters on configuration changes or new data arrival, debounced triggers, and safe marshalling of results to the UI thread. See [ReactiveIncrementalFiltering.md](ReactiveIncrementalFiltering.md) for details.
*   **Filter Engine:** The `FilterEngine` provides the core synchronous logic for applying a given filter tree and context lines to a snapshot of the log document.

### Highlighting System

*   **Programmatic Control:** Highlighting rules (timestamps, levels, filter matches, search terms) are managed programmatically via a custom `IHighlightingDefinition` (`CustomHighlightingDefinition`).
*   **Selected Line Highlighting:** The background highlighting for the user-selected line is implemented using a custom `DocumentColorizingTransformer` (`SelectedIndexHighlightTransformer`), integrating directly with AvalonEdit's rendering pipeline.
*   **Dynamic Updates:** Highlighting updates in real-time as filter configurations change or search terms are entered.
*   **Extensibility:** The system is designed to allow adding new types of highlighting rules easily.

### Log Display

*   **AvalonEdit:** The powerful AvalonEdit control is used for displaying log content, providing features like virtualization and syntax highlighting support.
*   **Custom Margins:**
    *   `OriginalLineNumberMargin`: Displays the original line number from the source file, driven by `FilteredLogLine` data. See [LineNumberManagement.md](LineNumberManagement.md).
    *   `OverviewRulerMargin`: Replaces the standard vertical scrollbar, providing a visual map of the filtered document with markers for search results (and potentially other points of interest). It controls scrolling via events. See [OverviewRulerFlow.md](OverviewRulerFlow.md).
    *   `ChunkSeparatorRenderer`: Draws visual lines between non-contiguous log chunks resulting from filtering with context.

### Persistence

*   **JSON Settings:** Application settings, including all filter profiles and the last active profile name, are serialized to a JSON file (`settings.json`) in the user's local application data folder (`%LocalAppData%\Logonaut`). Newtonsoft.Json is used for serialization, handling the `IFilter` polymorphism via `TypeNameHandling.All`.

### Data Flow

*   Detailed data flow for log processing and filtering is described in [LogFileProcessingFlow.md](LogFileProcessingFlow.md).
