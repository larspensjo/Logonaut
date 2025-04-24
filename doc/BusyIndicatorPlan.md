# Plan: Refactoring AnimatedSpinner to BusyIndicator

This document outlines the incremental steps to refactor the existing `AnimatedSpinner` into a more flexible `BusyIndicator` capable of displaying distinct animations for multiple concurrent busy states. Each step results in a functional application.

## Step 1: Refactor State Management (Internal Change, Same Visual)

*   **Goal:** Replace the single `bool IsSpinning` property with a mechanism to track multiple active states using a collection, while keeping the visual output (the original spinner arc) unchanged for now. The spinner will display if *any* state is active.
*   **Why:** Decouple the *indication* of busyness from the *specific type* of busyness. This foundational change prepares the control for handling multiple states and visuals later.
*   **Tasks:**
    *   Rename the control class and file from `AnimatedSpinner` to `BusyIndicator`. Update XAML references accordingly.
    *   Define identifiers for different busy states (e.g., static `object` tokens or an `enum`).
    *   Remove the `IsSpinning` dependency property.
    *   Add a new dependency property `ActiveStates` of type `ObservableCollection<object>` to hold the currently active busy state identifiers. Implement change handling for this property.
    *   Implement a `CollectionChanged` handler for the `ActiveStates` collection.
    *   Create a method (`UpdateRenderingBasedOnState`) that is called when `ActiveStates` or its contents change. This method determines if *any* state is active.
    *   Modify the existing animation subscription logic (`UpdateRenderingSubscription`) to accept a boolean parameter indicating whether animation should be active, based on the result from `UpdateRenderingBasedOnState`.
    *   Ensure `OnRender` only draws the original arc if the animation subscription is active.
    *   Update the `MainViewModel` to remove properties solely used for the old spinner state.
    *   Add an `ObservableCollection<object> CurrentBusyStates` property to the `MainViewModel`.
    *   Modify `MainViewModel` logic (e.g., starting/ending file loading, filtering) to add and remove the appropriate state tokens to/from the `CurrentBusyStates` collection.
    *   Update the XAML binding in `MainWindow.xaml` to bind the `BusyIndicator`'s `ActiveStates` property to the `MainViewModel`'s `CurrentBusyStates` collection.
*   **Result:** The application visually functions identically to before, showing the spinner when any busy operation is active. The internal state management is now collection-based, ready for the next steps.

## Step 2: Introduce State-Specific Visuals (No Concurrency Merging Yet)

*   **Goal:** Display a *different, unique* visual representation (animation/geometry) depending on which busy state is currently active. If multiple states are active concurrently, only the visual for the *first* state in the collection will be shown for now.
*   **Why:** Introduce the mapping between specific state types and distinct visual outputs.
*   **Tasks:**
    *   Design and implement the drawing logic for a second, distinct visual state (e.g., for "Filtering"). This could involve simple shape drawing or basic parameter changes to the existing arc logic for now. Create a separate drawing helper function for this new visual.
    *   Modify the `OnRender` method:
        *   Check if the `ActiveStates` collection is populated.
        *   Retrieve the *first* state identifier from the collection.
        *   Use conditional logic (like `if/else if` or `switch`) based on the first state identifier to call the appropriate drawing function (either the original arc function or the new function created above).
    *   Ensure the `MainViewModel` adds distinct state tokens for different operations (e.g., a "Filtering" token, a "Loading" token).
*   **Result:** The busy indicator now shows different visuals depending on the primary active state (e.g., blue spinner for loading, orange spinner for filtering). Concurrency defaults to showing the visual for whichever state was added first.

## Step 3: Basic Visual Merging (Parameter Blending)

*   **Goal:** When multiple states are active concurrently, combine their visual indication by modifying a parameter (like color or opacity) of a *single base animation* (e.g., the arc spinner).
*   **Why:** Implement a simple strategy for visually representing concurrency without complex geometry merging.
*   **Tasks:**
    *   Modify the `OnRender` method:
        *   Determine which specific states are present in the `ActiveStates` collection (e.g., check if both "Loading" and "Filtering" tokens are present).
        *   Based on the combination of active states, dynamically calculate the visual parameter to use (e.g., determine the appropriate `Brush`).
        *   Always call the *base* drawing function (e.g., the arc spinner), but ensure it uses the dynamically calculated parameter (e.g., the calculated `Brush`).
    *   Ensure the `MainViewModel` correctly adds and removes state tokens for potentially overlapping operations.
*   **Result:** The indicator always shows the base spinner shape, but its appearance (e.g., color) changes to reflect the combination of active states (e.g., Blue for Loading, Orange for Filtering, Purple for Both).

## Step 4: Introduce `PathGeometry` for One State

*   **Goal:** Replace the visual representation for one specific state (e.g., "Filtering") with a custom animation defined using `StreamGeometry` or `PathGeometry`. Implement a simple merging strategy (like layering) when this state is concurrent with others.
*   **Why:** Integrate the use of advanced vector graphics for specific state animations.
*   **Tasks:**
    *   Design the custom geometry and animation logic for the chosen state (e.g., "Filtering").
    *   Implement a new drawing function that uses `StreamGeometry` or `PathGeometry` to render this custom visual. This function will likely need to use animation timing values to update the geometry or its transformations frame-by-frame.
    *   Modify the `OnRender` method's merging logic:
        *   Check which states are active.
        *   If *only* the custom state is active, call its new drawing function.
        *   If *only* a basic state (like "Loading") is active, call its drawing function (e.g., the arc).
        *   If *both* (or multiple) states are active, implement a layering strategy: draw the base visual (e.g., the arc, potentially semi-transparent) and then draw the custom path visual on top (potentially also semi-transparent).
*   **Result:** The indicator displays the standard arc for one state, the custom path animation for another, and layers them when both are active.

## Step 5 (Optional/Future): Advanced Merging & Abstraction

*   **Goal:** Implement more sophisticated visual merging techniques or refactor the design for easier extensibility with new state types and visuals.
*   **Why:** Enhance the visual feedback for concurrency or improve the maintainability and scalability of the indicator.
*   **Tasks:**
    *   **Advanced Merging:** Explore modifying animation parameters (speed, oscillation), combining geometries using `CombinedGeometry`, or creating segmented indicators where different parts represent different active states.
    *   **Abstraction:** Consider defining an `IBusyStateVisualizer` interface. Create concrete visualizer classes for each state type. The `BusyIndicator` would manage a list of active visualizer instances based on the `ActiveStates` collection and call their `Update` and `Draw` methods in `OnRender`. This decouples the main control from the specific drawing logic of each state.
*   **Result:** A highly flexible and extensible busy indicator system capable of sophisticated visual feedback for concurrent operations.
