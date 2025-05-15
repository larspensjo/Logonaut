# Logonaut: Architecture and Design

This document provides a high-level overview of the Logonaut application's architecture, emphasizing how the design meets its core requirements. For general design principles, see `GeneralDesignPrinciples.md`.

## Core Principles & Patterns

Logonaut's design prioritizes **modularity**, **testability**, **responsiveness**, and **maintainability** through several key patterns:

*   **Modular Design:** The application is divided into distinct projects (`.csproj`), each handling specific concerns (UI, Core Logic, Filtering, Data Handling, Theming). This separation allows for independent development, testing, and replacement of components.
*   **MVVM (Model-View-ViewModel):** The `Logonaut.UI` layer utilizes MVVM to separate UI presentation (Views) from application logic and state (ViewModels), enhancing testability and maintainability. `CommunityToolkit.Mvvm` facilitates this pattern.
*   **Dependency Inversion Principle:** Key services (Settings, File Dialogs, Log Source, Filtering Logic) are accessed through interfaces (`ISettingsService`, `IFileDialogService`, `ILogSource`, `ILogFilterProcessor`), allowing for flexible implementation and enabling mocking for unit tests.
*   **Reactive Programming (Rx.NET):** The `LogFilterProcessor` leverages Rx.NET to handle asynchronous log updates and filter changes reactively. This includes debouncing UI triggers and orchestrating background processing to ensure UI responsiveness, meeting the requirement for handling large files and dynamic updates smoothly.
*   **Asynchronous Operations:** File I/O (`Logonaut.LogTailing`) and computationally intensive filtering (`LogFilterProcessor`, `FilterEngine`) are performed on background threads, preventing UI freezes. `SynchronizationContext` is used to safely marshal results back to the UI thread.

## Module Responsibilities (High-Level)

*   **Logonaut.UI:** Implements the WPF user interface, including ViewModels that manage UI state and orchestrate interactions. It extends AvalonEdit with custom controls and attached properties to meet specific display requirements.
*   **Logonaut.Core:** Houses the core business logic, including the reactive filtering pipeline (`LogFilterProcessor`), the synchronous filter application logic (`FilterEngine`), log source abstraction (`ILogSource`), and settings service interfaces/implementations.
*   **Logonaut.Filters:** Defines the structure and logic for various filter types and their composition.
*   **Logonaut.LogTailing:** Provides concrete implementations for `ILogSource` (real-time file monitoring and simulation).
*   **Logonaut.Common:** Contains shared data structures (`LogDocument`, `FilterProfile`, etc.), ensuring consistency across modules.
*   **Logonaut.Theming:** Manages switchable application themes (Light/Dark).
*   **Tests:** Provides unit tests for validating the functionality of each module.

## Key Mechanisms & Design Choices

*   **Log Processing & Tailing:** Log input is abstracted via `ILogSource`. `FileLogSource` handles file reading and real-time tailing asynchronously. The central `LogDocument` provides thread-safe storage for original log lines. This design supports both file monitoring and pasted content while keeping I/O off the UI thread.
*   **Dynamic Filtering:**
    *   Named **Filter Profiles** (`FilterProfile` within `LogonautSettings`) allow users to save and switch between complex filter configurations. Each `IFilter` within a profile can now also store a `HighlightColorKey` to specify its desired highlight appearance.
    *   The `LogFilterProcessor` subscribes to log source updates and filter setting changes from the `MainViewModel`. It uses **debouncing** (`Throttle`) to manage rapid updates and performs **full re-filtering** on a background thread against the current `LogDocument` snapshot.
    *   Results are pushed back to the `MainViewModel` as `FilteredUpdate` objects for atomic UI updates. This ensures the UI remains responsive even during intensive filtering or rapid log input.
*   **Log Display & Highlighting:**
    *   The **AvalonEdit** control is extended significantly to meet specific requirements:
        *   **Custom Margins** (`OriginalLineNumberMargin`, `OverviewRulerMargin`) display original line numbers and provide a document overview with markers, decoupling this view logic from the core editor.
        *   **Custom Renderers** (`ChunkSeparatorRenderer`, `SelectedIndexHighlightTransformer`) handle visual cues for non-contiguous filtered data and selected line background highlighting.
        *   **Attached Properties** (`AvalonEditHelper`) bridge the gap between ViewModel state (search terms, filter patterns/models, selection requests) and AvalonEdit's API for highlighting and selection, encapsulating view-specific logic.
        *   A **programmatic highlighting definition** (`CustomHighlightingDefinition`) allows dynamic rule updates based on user settings (timestamps, search) and per-filter configurations. It now resolves filter highlight colors based on `HighlightColorKey` stored in each `IFilter` model, using theme-aware brushes defined in application resources. This allows users to assign different, theme-adaptive colors to individual filter rules.
*   **State Management & UI Interaction:**
    *   `MainViewModel` acts as the central orchestrator, managing application state (settings, profiles, current filtered data, search state, busy status). It now also manages the collection of `IFilter` models passed to `AvalonEditHelper` for highlighting, ensuring their `HighlightColorKey` is considered.
    *   **Busy states** are managed via an `ObservableCollection<object>` (`CurrentBusyStates`) bound to custom indicators (`BusyIndicator`, overlay), providing clear feedback during background tasks like loading and filtering.
    *   UI services like `IFileDialogService` abstract direct UI dependencies.
    *   User interactions (scrolling, keyboard shortcuts) are handled in `MainWindow.xaml.cs` or via Commands to manage UI state like **disabling Auto-Scroll**.
    *   The `FilterViewModel` now exposes the `HighlightColorKey` of its underlying `IFilter` model and a list of available color choices for UI binding (e.g., a `ComboBox`).
*   **Persistence:** The `ISettingsService` (`FileSystemSettingsService`) handles serialization of `LogonautSettings` (including filter profiles with their `IFilter` models and their `HighlightColorKey` properties using `TypeNameHandling.All` for polymorphism) to JSON, ensuring user configurations are preserved across sessions.

## Graphical Identity

The "Flowing Neon Lines" concept provides a consistent visual identity across the application icon, busy indicators, loading overlay, and potentially future elements, reinforcing the application's focus on dynamic log data. Theming ensures adaptability to user preferences (Light/Dark).