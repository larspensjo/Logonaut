# Logonaut: Overview Ruler Data Flow and Control

## Introduction

Logonaut utilizes a custom `OverviewRulerMargin` control to replace the standard vertical scrollbar for the main log display (`TextEditor`). This custom ruler not only provides traditional scrolling capabilities (via thumb dragging) but also serves as a visual map of the entire (filtered) document, displaying markers for points of interest like search results.

This document outlines the data flow required to display information *on* the ruler and how user interaction *with* the ruler controls the scrolling of the `TextEditor`.

## Key Components

*   **`OverviewRulerMargin` (`Logonaut.UI.Helpers`):** The custom `Control` responsible for drawing the ruler track, thumb, and markers. It receives data via Dependency Properties and raises an event for scroll requests.
*   **`TextEditor` (`ICSharpCode.AvalonEdit`):** The main log display control. It exposes scroll-related properties (`VerticalOffset`, `ViewportHeight`, `ExtentHeight`) derived from its internal `ScrollViewer` and provides the `ScrollToVerticalOffset` method.
*   **`TextEditorWithOverviewRulerStyle` (`MainWindow.xaml`):** A custom `Style` applied to the `TextEditor`. Its `ControlTemplate` arranges the `TextEditor`'s internal `ScrollViewer` (with its default vertical scrollbar hidden) alongside the `OverviewRulerMargin`. Crucially, it contains the bindings connecting these components.
*   **`MainViewModel` (`Logonaut.UI.ViewModels`):** The primary ViewModel holding application state, including the collection of markers (`SearchMarkers`) to be displayed on the ruler.
*   **`MainWindow.xaml.cs`:** The code-behind for the main window, responsible for hooking up the event handler that connects the ruler's scroll request to the `TextEditor`'s scroll method.

## Data Flow TO the Ruler (Display)

The ruler needs specific information to draw itself correctly:

### 1. Scroll Information (Thumb Position & Size)

*   **Source:** The internal `ScrollViewer` (named `PART_ScrollViewer`) within the `TextEditor`'s template calculates the current scroll state based on the `TextArea` content.
*   **Path:**
    1.  The `ScrollViewer` updates its `VerticalOffset`, `ViewportHeight`, and `ExtentHeight` properties.
    2.  These values are accessed for binding within the `ControlTemplate`.
*   **Binding:** Inside the `TextEditorWithOverviewRulerStyle`'s `ControlTemplate`, the `OverviewRulerMargin` binds its corresponding Dependency Properties (`VerticalOffset`, `ViewportHeight`, `ExtentHeight`) to the `ScrollViewer` using `ElementName=PART_ScrollViewer` binding.
    ```xml
    <helpers:OverviewRulerMargin ...
        VerticalOffset="{Binding Path=VerticalOffset, ElementName=PART_ScrollViewer, Mode=OneWay}"
        ViewportHeight="{Binding Path=ViewportHeight, ElementName=PART_ScrollViewer, Mode=OneWay}"
        ExtentHeight="{Binding Path=ExtentHeight, ElementName=PART_ScrollViewer, Mode=OneWay}"
        ... />
    ```
*   **Rendering:** The `OverviewRulerMargin.OnRender` method uses these bound values:
    *   It calculates the thumb's height proportionally: `thumbHeight = (ViewportHeight / ExtentHeight) * ActualHeight` (with checks for minimum size and capping at `ActualHeight`).
    *   It calculates the thumb's top position proportionally: `thumbTop = (VerticalOffset / ExtentHeight) * ActualHeight`.
    *   It draws the thumb rectangle using these calculations and the `ThumbBrush`.

### 2. Markers (Search Results)

*   **Source:** The `MainViewModel.UpdateSearchMatches` method logic.
*   **Path:**
    1.  The logic searches the current `LogText`.
    2.  For each match, a `SearchResult` record (containing `Offset` and `Length`) is created.
    3.  The `SearchResult` is added to the `MainViewModel.SearchMarkers` (an `ObservableCollection<SearchResult>`).
*   **Binding:** Inside the `TextEditorWithOverviewRulerStyle`'s `ControlTemplate`, the `OverviewRulerMargin.SearchMarkers` Dependency Property binds to the ViewModel's collection.
    ```xml
     <helpers:OverviewRulerMargin ...
         SearchMarkers="{Binding DataContext.SearchMarkers, RelativeSource={RelativeSource AncestorType=Window}, Mode=OneWay}"
         ... />
    ```
*   **Rendering:** The `OverviewRulerMargin.OnRender` method uses the bound `SearchMarkers` collection and `DocumentLength`:
    1.  It iterates through each `SearchResult` in the `SearchMarkers` collection.
    2.  For each marker, it calculates the relative position within the document: `relativeDocPos = (double)marker.Offset / DocumentLength`.
    3.  It scales this relative position to the ruler's height: `yPos = relativeDocPos * ActualHeight`.
    4.  It draws a small marker rectangle at `yPos` using the `SearchMarkerBrush`.

*(TODO: A similar flow could be implemented for filter match markers by adding another collection to the ViewModel and a corresponding `FilterMarkers` property/binding/drawing logic to the ruler).*

## Interaction Flow FROM the Ruler (Scrolling Control)

When the user interacts with the ruler to scroll:

1.  **User Input:** The user clicks or drags the mouse on the `OverviewRulerMargin` control.
2.  **Ruler Calculation:**
    *   The `OverviewRulerMargin`'s mouse event handlers (`OnMouseLeftButtonDown`, `OnMouseMove`) capture the mouse position (`y`).
    *   It calculates the desired *target* `VerticalOffset` for the `TextEditor` based on the click/drag position relative to the ruler's height and the `ExtentHeight`. It typically tries to center the view around the clicked point: `desiredOffset = (y / ActualHeight) * ExtentHeight - (ViewportHeight / 2);` (with clamping).
3.  **Event Invocation:** The `OverviewRulerMargin` raises the `RequestScrollOffset` event, passing the calculated `desiredOffset` as an argument.
    ```csharp
    // Inside OverviewRulerMargin.ScrollToPosition(double y)
    RequestScrollOffset?.Invoke(this, desiredOffset);
    ```
4.  **Event Handling (`MainWindow.xaml.cs`):**
    *   The `MainWindow` code-behind, in its `LogOutputEditor_Loaded` handler, finds the instance of `OverviewRulerMargin` within the `TextEditor`'s template using `FindVisualChild`.
    *   It subscribes an event handler (`OverviewRuler_RequestScrollOffset`) to the ruler's `RequestScrollOffset` event.
5.  **Executing the Scroll:**
    *   The `OverviewRuler_RequestScrollOffset` event handler in `MainWindow.xaml.cs` receives the event and the `requestedOffset`.
    *   It calls the `TextEditor`'s method to perform the scroll: `LogOutputEditor.ScrollToVerticalOffset(requestedOffset);`.
    ```csharp
    // Inside MainWindow.xaml.cs
    private void OverviewRuler_RequestScrollOffset(object? sender, double requestedOffset)
    {
        LogOutputEditor.ScrollToVerticalOffset(requestedOffset);
    }
    ```
6.  **Feedback Loop:** The `TextEditor` scrolls its content. This causes its internal `ScrollViewer` to update its `VerticalOffset`. This change triggers the data flow described in "Scroll Information" above, causing the `OverviewRulerMargin` to redraw its thumb at the new position.

## Highlighting within the Ruler

The term "highlighting" in the context of the `OverviewRulerMargin` refers to the **drawing of markers** (small colored rectangles) to indicate points of interest.

*   **Search Markers:** These are drawn using the `SearchMarkerBrush` property of the `OverviewRulerMargin`. The data comes from the `SearchMarkers` collection bound from the `MainViewModel`.
*   **Customization:** By changing the `SearchMarkerBrush` (e.g., via theme resources bound using `DynamicResource`), the appearance of these markers can be controlled.
*   **Extensibility:** Additional marker types (e.g., filter matches, error lines) can be added by:
    1.  Adding corresponding data collections to the `MainViewModel`.
    2.  Adding Dependency Properties (e.g., `FilterMarkers`, `FilterMarkerBrush`) to `OverviewRulerMargin`.
    3.  Binding these properties in the `ControlTemplate`.
    4.  Adding drawing logic within `OverviewRulerMargin.OnRender` to draw the new marker types using their specific data and brushes.

This architecture allows the `OverviewRulerMargin` to act as a visual summary, driven by data from the ViewModel and controlling the main editor through events and bindings defined in the template.