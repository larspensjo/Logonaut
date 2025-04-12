# Logonaut Theme Specification: Neon Night (Dark)

This document outlines the visual style, color palette, and specific UI element requirements for the "Neon Night" dark theme for the Logonaut application.

## Overall Aesthetic

-   **Core:** Modern, clean, flat design principles baseline, enhanced with subtle depth via layering and pronounced glows.
-   **Feel:** "Techy," "Developer Tool," or "Cyberpunk-lite." High contrast for readability, focusing on clarity for log analysis.
-   **Emphasis:** Pronounced neon glow effects for interactive elements and focus states. Panels appear to float above the background.

## Color Palette

| Element                           | Description           | Hex Code    | Resource Key (`<Color>`)          | Brush Key (`<SolidColorBrush>`)          |
| :-------------------------------- | :-------------------- | :---------- | :------------------------------ | :--------------------------------------- |
| **Primary Accent**                | Electric Cyan/Blue    | `#FF33CCFF` | `NeonAccentColor`               | `AccentBrush`                            |
| Base Background                   | Very Dark Charcoal    | `#FF1A1A1A` | `BaseBackgroundColor`           | `WindowBackgroundBrush`                  |
| Panel Background                  | Lighter Dark Grey     | `#FF2C2C2E` | `PanelBackgroundColor`          | `PanelBackgroundBrush`                   |
| Control/Input Background          | Dark (like Base)      | `#FF1A1A1A` | `ControlBackgroundColor`        | `ControlBackgroundBrush`                 |
| Primary Text                      | Light Grey / Off-white| `#FFEAEAEA` | `PrimaryForegroundColor`        | `TextForegroundBrush`                    |
| Secondary Text / Icons            | Medium Grey           | `#FF8A8A8E` | `SecondaryForegroundColor`      | `SecondaryTextBrush`                     |
| Borders / Dividers                | Very Dark Grey        | `#FF3A3A3C` | `BorderColor`                   | `BorderBrush`, `DividerBrush`            |
| **Status: Error**                 | Vibrant Red           | `#FFFF1111` | `ErrorColor`                    | `ErrorBrush`                             |
| **Status: Warning**               | Vibrant Orange        | `#FFFFA500` | `WarningColor`                  | `WarningBrush`                           |
| **Status: Info**                  | Neutral Medium Grey   | `#FF999999` | `InfoColor`                     | `InfoBrush`                              |
| **Glow Effect Color**             | Accent with Alpha     | `#CC33CCFF` | `GlowColor`                     | *(Used directly in DropShadowEffect)*    |
| **Highlight: Filter Match BG**    | Dark Yellow/Olive     | `#FF808000` | `HighlightFilterBackgroundColor`| `HighlightFilterBackgroundBrush`         |
| **Highlight: Search Match BG**    | Dark Cyan             | `#FF008B8B` | `HighlightSearchBackgroundColor`| `HighlightSearchBackgroundBrush`         |
| Highlighting: Filter/Search FG    | Light Grey            | `#FFEAEAEA` | *(Uses PrimaryForegroundColor)* | *(Uses TextForegroundBrush)*             |
| Highlighting: Timestamp FG        | Light Sky Blue        | `#FF87CEFA` | `HighlightTimestampColor`       | *(Used directly or via named color)*     |

*Note: Foreground colors for Filter/Search highlighting should use `TextForegroundBrush` for readability.*

## Typography

-   Use a clean, highly readable sans-serif font suitable for code/log viewing (e.g., Consolas, Fira Code, Inter, Roboto Mono, Segoe UI).
-   Maintain consistent font usage throughout the application.

## Depth & 3D Effects ("Glow & Layering")

-   **Layering:** Panels (Filters, Stats Bar, Toolbars, Search Bar) should "float" slightly above the `BaseBackgroundColor`. Achieve this using subtle, diffused **dark drop shadows** (`PanelShadowEffect`: Color `#FF000000`, ShadowDepth 2-4, BlurRadius 5-10, Opacity 0.3-0.5).
-   **Interactivity/Focus (Neon Effect):** Use the `NeonAccentColor` to create **pronounced but clean outer glows** (`FocusGlowEffect`: Color `#CC33CCFF`, ShadowDepth 0, BlurRadius 5-8, Opacity 0.7-0.9) or highlighted borders around focused/active elements:
    -   Text Inputs (Filters, Search) when focused.
    *   Active/Selected Buttons (Filter type, AND/OR/NOR).
    -   Active `ToggleButton` / `ToggleSwitch`.
    -   Selected items in the Filter `TreeViewItem`.
-   **Buttons/Controls:** Primarily flat design.
    -   *Hover:* Slightly lighten background *or* apply `FocusGlowEffect`.
    -   *Pressed:* Apply `FocusGlowEffect` and potentially a subtle inset effect (slightly darker background, minimal inner shadow if desired, though glow is primary).

## Specific UI Element Styling

1.  **Window:** Dark title bar (using OS integration if possible). Background uses `WindowBackgroundBrush`.
2.  **Menu Bar:** Background uses `PanelBackgroundBrush`. Text uses `TextForegroundBrush`. Hovered `MenuItem` background uses `AccentBrush`, text uses `BaseBackgroundColor`.
3.  **Log Stats Bar:** Uses `CardPanelStyle` (Panel Background, dark shadow). Text uses theme colors. Status numbers/icons tinted with `ErrorBrush`, `WarningBrush`, `InfoBrush`.
4.  **Filters Panel:** Uses `CardPanelStyle`.
    *   **Filter TreeView:** Background transparent or `PanelBackgroundBrush`. Items use `TextForegroundBrush`. Selected items have `AccentBrush` background/border and/or `FocusGlowEffect`. Disabled items have reduced opacity (e.g., 0.5). Use simple, distinct icons (filled with `SecondaryTextBrush` or `TextForegroundBrush`) for each filter type (`SubstringType`, `RegexType`, `AndType`, etc.). Optionally, apply very subtle background differences for composite vs. value filters.
    *   **Filter Input (Edit Mode):** TextBox uses `ControlBackgroundBrush`, `TextForegroundBrush`. Gains `FocusGlowEffect` when active.
    *   **Add Buttons:** Styled as flat Buttons, using `FocusGlowEffect` on hover/active.
    *   **Edit/Remove Buttons:** Styled as flat Buttons.
5.  **Display Options Toolbar:** Uses `CardPanelStyle`.
    *   **Toggle Switches (`Line Numbers`, `Highlight Timestamps`):** Use `ToggleSwitchStyle`. Active state uses `AccentBrush` background and shows `FocusGlowEffect`. Thumb uses `SecondaryTextBrush` (inactive) and `BaseBackgroundColor` (active).
    *   **Context Lines Input:** `TextBox` styled dark with glow on focus. Up/down `Button`s styled dark and flat.
    *   **Busy Indicator:** Uses `BusySpinnerStyle` (animated rotating arc) with `AccentBrush` for the arc color.
    *   **Search Status Text:** Uses `SecondaryTextBrush`.
6.  **Log View Area (AvalonEdit):**
    *   Background uses `EditorBackgroundBrush` (`BaseBackgroundColor`). Text uses `EditorForegroundBrush` (`TextForegroundBrush`).
    *   **Custom Highlighting:** Defined via `CustomHighlightingDefinition`, using theme brushes:
        *   Timestamp: `Highlighting.Timestamp` brush (e.g., LightSkyBlue).
        *   Error/Warn/Info: Text color set to `ErrorBrush`, `WarningBrush`, `InfoBrush`. Consider `FontWeight="Bold"` for Error.
        *   Filter Match: Background set to `Highlighting.FilterMatch.Background` brush. Foreground `TextForegroundBrush`.
        *   Search Match: Background set to `Highlighting.SearchMatch.Background` brush. Foreground `TextForegroundBrush`.
    *   **Margins (`OriginalLineNumberMargin`, `VerticalLineMargin`, `ChunkSeparatorRenderer`):** Background matches editor. Text/Lines use `SecondaryTextBrush` or `DividerBrush`.
    *   **`OverviewRulerMargin`:** Track uses `OverviewRuler.Background`. Thumb uses `OverviewRuler.ThumbBrush`. Markers use `OverviewRuler.SearchMarkerBrush` (Accent), `OverviewRuler.FilterMarkerBrush` (Warning), `OverviewRuler.ErrorMarkerBrush` (Error). Prepare for distinct shapes/colors per marker type.
    *   **Scrollbars:** Styled dark (dark track, `SecondaryTextBrush` thumb, `AccentBrush` hover).
7.  **Search Bar:** Uses `CardPanelStyle`.
    *   **Previous/Next Buttons:** Styled as flat Buttons with `FocusGlowEffect` on hover. Use icons (`<`, `>`), potentially filled with `TextForegroundBrush` or `AccentBrush`.
    *   **Search Input TextBox:** Styled dark with `FocusGlowEffect` on focus.
    *   **Case Sensitive CheckBox:** Styled dark, uses `AccentBrush` for checkmark/indicator.