# Fancy Busy Indicator Animation Ideas

This document explores creative and visually interesting animation ideas for the `BusyIndicator` control, moving beyond a simple spinning arc, **and** includes ideas for a dedicated loading overlay. These leverage WPF's 2D drawing capabilities, particularly `PathGeometry` and transforms, to provide unique feedback for different application states.

## I. Persistent Indicators (for External `BusyIndicator`)

These animations loop or show continuous progress until the associated task completes (e.g., Filtering, Indexing). **Loading state is handled separately by the overlay.**

1.  **"Data Stream" Flow:**
    *   **Visual:** Small dashes or arrowheads travel along a circular path, simulating data flow.
    *   **Variations:** Different colors, directions, speeds, or shapes (dashes vs. arrows) for different tasks (e.g., orange counter-clockwise for Filtering). Concurrent tasks could show streams on different radii or blended colors.
    *   **Fancy:** Add fading trails behind the moving elements.

2.  **Geometric Assembly/Deconstruction:**
    *   **Visual:** Pieces (lines, curves) animate flying in from the edges to form a task-specific shape (e.g., gear for Filtering) or fly apart when the task finishes.
    *   **Variations:** Use task-specific target shapes. Blend piece colors during concurrent tasks.
    *   **Fancy:** Use curved flight paths (Bezier), rotate pieces as they move, add a "snap" effect (scale pulse) upon assembly.

3.  **"Bio-Cell" Pulsation:**
    *   **Visual:** An organic, blob-like shape defined with Bezier curves subtly warps and pulses by animating the curve control points over time.
    *   **Variations:** Different colors and pulsation rates/rhythms for different tasks. Blend colors and average pulsation rates for concurrency.
    *   **Fancy:** Overlay a faint static grid or circuit pattern behind the cell. Add small drifting "organelle" dots inside.

4.  **Interlocking Rings/Arcs:**
    *   **Visual:** Multiple rings or arcs animate their rotation and also their position/scale, appearing to orbit or pass through each other like gyroscopic rings.
    *   **Variations:** Different styles (solid, dashed), colors, and rotation directions/speeds per task. Show all active rings simultaneously for concurrency.
    *   **Fancy:** Apply rotating gradients to the rings. Use `SkewTransform` for a pseudo-3D perspective effect.

5.  **"Circuit Board" Activity:**
    *   **Visual:** A static background pattern resembling a circuit board. Small, bright pulses of "electricity" travel along the defined paths.
    *   **Variations:** Different colors and paths for different tasks (e.g., orange on "processing" traces for Filtering). Show pulses for all active tasks concurrently. Vary pulse density/speed.
    *   **Fancy:** Make static "pads" on the circuit board glow briefly as pulses pass over them.

## II. Dedicated Loading Overlay Animation (for AvalonEdit Overlay)

This animation is shown semi-transparently *over* the text editor area *only* during the initial file loading phase (`LoadingToken` active). Subtlety is key to avoid obscuring content.

1.  **Soft Vertical Scanlines:**
    *   **Visual:** Very faint, wide, semi-transparent vertical bands of color (or slightly lighter/darker than the overlay background).
    *   **Motion:** Bands slowly sweep horizontally across the overlay area. They might originate from one side and fade before reaching the other, or appear across the whole width and scroll horizontally, wrapping around.
    *   **Styling:** Use colors very close to the theme's editor background, differing subtly in brightness or hue (e.g., a slightly lighter grey on a dark theme). Soft gradient edges enhance subtlety.
    *   **Subtlety:** Very high transparency (e.g., Opacity 0.1-0.2). Slow, smooth, continuous horizontal motion. Soft edges via gradients. Aims for minimal distraction while indicating background activity.

2.  *(Alternative Loading Idea)* **Fading Dashes/Particles (Top Down):**
    *   **Visual:** Thin, short horizontal dashes or small particles appear near the top edge.
    *   **Motion:** Drift downwards slowly, potentially fading out over the top 1/3 of the overlay.
    *   **Styling:** Semi-transparent, dimmed theme color. Low density.
    *   **Subtlety:** Sparsity, small size, fading effect.

3.  *(Alternative Loading Idea)* **Top/Bottom Edge Pulse/Glow:**
    *   **Visual:** Soft, thin glow or filled rectangle along the top and/or bottom edge.
    *   **Motion:** Gentle pulsing in intensity/opacity, or a slow horizontal wave along the edge.
    *   **Styling:** Highly transparent, blurred theme accent color for glow, or semi-transparent neutral color for rectangle.
    *   **Subtlety:** Confined to edges, slow pulse/wave, high transparency.

## III. Transient Indicators (for External `BusyIndicator`)

These are short, attention-grabbing animations in the *external* indicator, triggered by specific, momentary actions (e.g., Search Next/Prev).

1.  **Directional Energy Pulse:**
    *   **Visual:** A bright shape (like a chevron) quickly scales up from the center and shoots off downwards (for Next) or upwards (for Previous), fading out rapidly.
    *   **How:** Trigger a short, frame-by-frame animation of scale, position, and opacity within `BusyIndicator.OnRender`.

2.  **Color Flash/Invert:**
    *   **Visual:** The entire external indicator briefly flashes a specific color (e.g., search highlight color) or inverts its colors.
    *   **How:** Temporarily override the indicator's brush/colors for a few frames in `BusyIndicator.OnRender` when triggered.

3.  **"Focus Target" Shrink:**
    *   **Visual:** A crosshair or target reticle shape appears, fills the external indicator, then rapidly shrinks to the center and vanishes.
    *   **How:** Trigger a short scale/opacity animation sequence for the target geometry within `BusyIndicator.OnRender`.

4.  **Rotational "Tick":**
    *   **Visual:** If a persistent rotational animation is active in the external indicator, it performs a quick, sharp rotation offset (e.g., +30° for Next, -30° for Previous) before smoothly resuming its normal course.
    *   **How:** Temporarily add an offset to the rotation angle calculation in `BusyIndicator.OnRender` that decays over a few frames.

## Merging Ideas (for External `BusyIndicator`)

*   **Layering:** Draw persistent non-loading indicators normally and layer transient flashes/pulses on top. Use transparency effectively.
*   **Interruption:** Briefly pause or dim the persistent non-loading animation while a transient one plays.
*   **Modulation:** Have a transient event briefly alter a parameter (color, speed, angle) of the ongoing persistent non-loading animation.

## Implementation Notes

*   **State Machine:** The `MainViewModel` manages the active state tokens (`LoadingToken`, `FilteringToken`, etc.) in `CurrentBusyStates`.
*   **Loading Overlay:** A dedicated control (`LoadingScanlineOverlay`) inside a `Border`/`Canvas` in `MainWindow.xaml`, visibility bound to `CurrentBusyStates` containing `LoadingToken`. Uses `CompositionTarget.Rendering` triggered by its own `IsVisibleChanged`.
*   **External `BusyIndicator`:** Bound to `CurrentBusyStates`. Its internal logic (`UpdateRenderingBasedOnState`, `OnRender`) filters out `LoadingToken` and decides which animation(s) to display based on the remaining tokens.
*   **Animation Timing:** Use `TimeSpan elapsed` in `OnRender` or animation callbacks for smooth, frame-rate independent motion.
*   **Geometry:** Prefer `StreamGeometry` for performance within `OnRender`, especially for frequently changing shapes. Use `PathGeometry` for more complex static definitions.
*   **Transforms:** Leverage `RotateTransform`, `ScaleTransform`, `TranslateTransform`, etc., within `DrawingContext.PushTransform` for animating static geometries efficiently.
