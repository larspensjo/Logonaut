# Plan: Refactoring AnimatedSpinner to BusyIndicator (with Overlay)

This document outlines the incremental steps to refactor the existing `AnimatedSpinner` into a more flexible `BusyIndicator` capable of displaying distinct animations for multiple concurrent busy states, **and** adds a separate overlay animation for initial file loading over the text editor. Each step results in a functional application.

## Step 1: Refactor State Management (Internal Change, Same Visual)

This step should now be complete.

*   **Goal:** Replace the single `bool IsSpinning` property with a mechanism to track multiple active states using a collection (`ActiveStates`), while keeping the visual output (the original spinner arc in the *external* `BusyIndicator`) unchanged for now. The spinner will display if *any* state is active.
*   **Why:** Decouple the *indication* of busyness from the *specific type* of busyness. This foundational change prepares for handling multiple states and visuals later.
*   **Tasks:**
    *   Rename the control class and file from `AnimatedSpinner` to `BusyIndicator`. Update XAML references accordingly (`ControlsStyles.xaml`, `MainWindow.xaml`).
    *   Define identifiers for different busy states in `MainViewModel` (e.g., `public static readonly object LoadingToken = new();`, `public static readonly object FilteringToken = new();`).
    *   Change `BusyIndicator.ActiveStates` to be a standard read/write `DependencyProperty` of type `ObservableCollection<object>` (using `DependencyProperty.Register`). Ensure the `OnActiveStatesChanged` callback correctly subscribes/unsubscribes to `CollectionChanged` on the *newly bound* collection.
    *   Implement the `CollectionChanged` handler (`ActiveStates_CollectionChanged`) within `BusyIndicator`.
    *   Create a method (`UpdateRenderingBasedOnState`) in `BusyIndicator` that is called when `ActiveStates` property changes *or* its contents change (via `CollectionChanged`). This method determines if `ActiveStates` collection `Any()` (after checking for null).
    *   Modify the `BusyIndicator` animation subscription logic (`UpdateRenderingSubscription`) to accept a boolean parameter indicating whether animation should be active, based on the result from `UpdateRenderingBasedOnState`.
    *   Ensure `BusyIndicator.OnRender` only draws the original arc if the animation subscription is active.
    *   Remove old boolean busy flags (`IsBusyFiltering`, `IsPerformingInitialLoad`) from `MainViewModel`.
    *   Add an `ObservableCollection<object> CurrentBusyStates { get; } = new();` property to the `MainViewModel`.
    *   Modify `MainViewModel` logic (e.g., starting/ending file loading, filtering) to add and remove the appropriate state tokens (`LoadingToken`, `FilteringToken`) to/from the `CurrentBusyStates` collection. Use `_uiContext.Post` for thread safety if modifying from background operations.
    *   Update the XAML binding in `MainWindow.xaml` to bind the `BusyIndicator`'s `ActiveStates` property to the `MainViewModel`'s `CurrentBusyStates` collection.
    *   Update relevant unit tests in `MainViewModelTests` to assert against the `CurrentBusyStates` collection instead of the removed boolean flags.
*   **Result:** The application visually functions identically to before, showing the external spinner when any busy operation is active. The internal state management is now collection-based, ready for the next steps.

## Step 2: Implement AvalonEdit Loading Overlay

This step should now be complete.

*   **Goal:** *   **Goal:** Add a separate, semi-transparent overlay animation directly on top of the `TextEditor` (`LogOutputEditor`) that is visible *only* when the `LoadingToken` is present in `MainViewModel.CurrentBusyStates`. Use a "Soft **Horizontal** Scanlines" animation **moving upwards**.
*   **Why:** Provide contextual visual feedback during potentially long initial file loads, directly over the content area being populated.
*   **Tasks:**
    *   **XAML Layout (`MainWindow.xaml`):**
        *   In the `Grid` containing the `LogOutputEditor` (in the right panel, `Grid.Row="1"`), add a new element *after* the editor, ensuring it overlays it (e.g., a `Border` or `Canvas` named `LoadingOverlay`). It should occupy the same `Grid.Row`.
        *   Set `LoadingOverlay.IsHitTestVisible="False"` so it doesn't interfere with mouse interactions on the editor below.
        *   Set a semi-transparent background initially for testing visibility (`Background="#80000000"`, maybe remove later if the animation element fills it).
        *   Bind `LoadingOverlay.Width` to `LogOutputEditor.ActualWidth` using `ElementName` binding.
        *   Bind `LoadingOverlay.Height` to `LogOutputEditor.ActualHeight` using `ElementName` binding.
    *   **Visibility Binding:**
        *   Create an `IValueConverter` (`CollectionContainsToVisibilityConverter`) that takes a collection and a `ConverterParameter` (the token to check for). It returns `Visibility.Visible` if the collection contains the parameter, otherwise `Visibility.Collapsed`.
        *   Add an instance of this converter to resources.
        *   Make the `LoadingToken` (and potentially others) `public static readonly` in `MainViewModel` so it can be referenced in XAML binding.
        *   Bind `LoadingOverlay.Visibility` to `MainViewModel.CurrentBusyStates` using this converter, passing `{x:Static vm:TabViewModel.LoadingToken}` as the `ConverterParameter` (requires adding `vm:` namespace mapping to `ViewModels`).
    *   **Animation Implementation (Recommended - Custom Element):**
        *   Create a new `FrameworkElement`-derived control (e.g., `LoadingScanlineOverlay`) responsible for drawing the "Soft Vertical Scanlines" animation.
        *   Place this custom control *inside* the `LoadingOverlay` Border/Canvas.
        *   Inside `LoadingScanlineOverlay`:
            *   Use `CompositionTarget.Rendering` for animation updates.
            *   Subscribe/unsubscribe in response to its own `IsVisibleChanged` event (controlled by the `LoadingOverlay`'s visibility binding).
            *   Implement `OnRender` to draw faint, thin, semi-transparent **horizontal** bands.
            *   Animate the **vertical position offset** of these bands over time to create a slow **upwards sweeping** effect.
            *   Use theme-aware colors (e.g., derived from background/foreground) with very high transparency (e.g., Opacity 0.1-0.2).
            *   Ensure soft edges (e.g., using linear gradients for the bands' brushes).
    *   **Design:** Finalize the visual appearance (color, width, speed, transparency) of the scanlines to be subtle yet noticeable.
*   **Result:** When `MainViewModel` adds `LoadingToken` to `CurrentBusyStates`, the **horizontal scanline animation moving upwards** appears semi-transparently over the `TextEditor`. When the token is removed, the overlay disappears. The external `BusyIndicator` still spins based on *any* token present (including `LoadingToken` at this stage).

## Step 3: State-Specific Visuals for *BusyIndicator* (Ignoring Loading)

*   **Goal:** Modify the *external* `BusyIndicator` to display a different visual depending on the *active non-loading* state (e.g., `FilteringToken`). It should now *ignore* the `LoadingToken` and display nothing if *only* `LoadingToken` is present.
*   **Why:** Separate the visual feedback: overlay for loading, external indicator for other tasks like filtering.
*   **Tasks:**
    *   Modify `BusyIndicator.UpdateRenderingBasedOnState`: Change the condition to check if `ActiveStates` contains *any token other than* `LoadingToken`.
        *   `bool shouldBeSpinning = ActiveStates?.Any(s => s != TabViewModel.LoadingToken) == true;` (Ensure `TabViewModel.LoadingToken` is accessible or pass it).
    *   Design and implement drawing logic for the "Filtering" state visual in `BusyIndicator` (e.g., orange arc, different shape). Create a separate drawing function.
    *   Modify `BusyIndicator.OnRender`:
        *   Check if the rendering subscription is active (meaning a non-loading state is present).
        *   Iterate through `ActiveStates` to find the *first* token that is *not* `LoadingToken`.
        *   Use conditional logic based on this first non-loading token (e.g., `FilteringToken`) to call the appropriate drawing function (original arc, new filtering visual, etc.).
        *   If only `LoadingToken` was present, `OnRender` shouldn't be called anyway due to the change in `UpdateRenderingBasedOnState`/`UpdateRenderingSubscription`.
*   **Result:** The external `BusyIndicator` now shows different visuals depending on the primary *non-loading* active state (e.g., blue spinner for filtering). It shows nothing when only the file loading is happening (as the overlay handles that).

## Step 4: Basic Visual Merging for *BusyIndicator* (Ignoring Loading)

*   **Goal:** If multiple *non-loading* states become active concurrently in the future (e.g., Filtering + hypothetical "Indexing"), combine their visual indication in the *external* `BusyIndicator` by modifying parameters (color, opacity) of a base animation.
*   **Why:** Implement a simple strategy for representing concurrency *for non-loading tasks*.
*   **Tasks:**
    *   Modify `BusyIndicator.OnRender`:
        *   Filter `ActiveStates` to exclude `LoadingToken`.
        *   Determine which specific *non-loading* states are present.
        *   Based on the combination, dynamically calculate visual parameters (e.g., `Brush`).
        *   Call the appropriate drawing function using the calculated parameters.
*   **Result:** The external indicator shows visuals reflecting the combination of *active non-loading* states.

## Step 5: Introduce `PathGeometry` for One *Non-Loading* State in *BusyIndicator*

*   **Goal:** Replace the visual representation for one specific *non-loading* state (e.g., "Filtering") in the *external* `BusyIndicator` with a custom animation using `StreamGeometry`/`PathGeometry`. Implement merging (e.g., layering) if concurrent *non-loading* states occur.
*   **Why:** Integrate advanced vector graphics for specific non-loading state animations.
*   **Tasks:**
    *   Design the custom geometry/animation for the chosen state (e.g., "Filtering").
    *   Implement a new drawing function in `BusyIndicator` using `StreamGeometry`/`PathGeometry`.
    *   Modify `BusyIndicator.OnRender`'s merging logic for *non-loading* states:
        *   If only the custom state (e.g., Filtering) is active (excluding Loading), call its drawing function.
        *   If only another basic non-loading state is active, call its function.
        *   If multiple *non-loading* states are active, implement layering or another merging strategy for them.
*   **Result:** The external `BusyIndicator` displays standard visuals for some non-loading states, custom path animations for others, and merges them appropriately when concurrent (excluding the Loading state). The AvalonEdit overlay continues to handle the Loading state independently.

## Step 6: Advanced Merging & Abstraction for *BusyIndicator*

*   **Goal:** Implement more sophisticated visual merging or abstraction for the *external* `BusyIndicator` for *non-loading* states.
*   **Why:** Enhance feedback for concurrency or improve extensibility for *non-loading* tasks.
*   **Tasks:** (Focus on non-loading states)
    *   Explore advanced merging (parameter modulation, combined geometry, segmentation).
    *   Consider `IBusyStateVisualizer` abstraction for *non-loading* states managed by `BusyIndicator`.
*   **Result:** A highly flexible external busy indicator system for concurrent *non-loading* operations.
