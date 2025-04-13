# Logonaut Theme Specification: Clinical Neon (Light)

This document outlines the visual style, color palette, and specific UI element requirements for the "Clinical Neon" light theme for the Logonaut application.

## Overall Aesthetic

*   **Core:** Modern, clean, flat design principles baseline, enhanced with very subtle depth via shadows or borders.
*   **Feel:** Professional, clinical, high clarity, but with a distinct, vibrant "neon" accent for interactivity. Focus remains on usability for log analysis.
*   **Emphasis:** Use of the neon accent color primarily for borders and fills to indicate focus/selection. Depth effects are minimal and rely on subtle shadows or thin lines. Avoid strong glows.

## Color Palette

| Element                           | Description           | Hex Code    | Resource Key (`<Color>`)          | Brush Key (`<SolidColorBrush>`)             |
| :-------------------------------- | :-------------------- | :---------- | :------------------------------ | :------------------------------------------ |
| **Primary Accent**                | Electric Cyan/Blue    | `#FF00BFFF` | `NeonAccentColor`               | `AccentBrush`                               |
| Base Background                   | White                 | `#FFFFFFFF` | `BaseBackgroundColor`           | `WindowBackgroundBrush`                     |
| Panel Background                  | Off-White             | `#FFF9F9F9` | `PanelBackgroundColor`          | `PanelBackgroundBrush`                      |
| Control/Input Background          | White                 | `#FFFFFFFF` | `ControlBackgroundColor`        | `ControlBackgroundBrush`                    |
| Primary Text                      | Near Black            | `#FF1C1C1E` | `PrimaryForegroundColor`        | `TextForegroundBrush`                       |
| Secondary Text / Icons            | Medium Grey           | `#FF6D6D72` | `SecondaryForegroundColor`      | `SecondaryTextBrush`                        |
| Borders / Dividers                | Light Grey            | `#FFD1D1D6` | `BorderColor`                   | `BorderBrush`, `DividerBrush`               |
| **Status: Error**                 | Vibrant Red           | `#FFFF3B30` | `ErrorColor`                    | `ErrorBrush`                                |
| **Status: Warning**               | Vibrant Orange        | `#FFFF9500` | `WarningColor`                  | `WarningBrush`                              |
| **Status: Info**                  | Neutral Medium Grey   | `#FF6D6D72` | `InfoColor`                     | `InfoBrush`                                 |
| **Highlight: Filter Match BG**    | Pale Yellow           | `#FFFFFFAC` | `HighlightFilterBackgroundColor`| `HighlightFilterBackgroundBrush`            |
| **Highlight: Search Match BG**    | Pale Turquoise        | `#FFAFEEEE` | `HighlightSearchBackgroundColor`| `HighlightSearchBackgroundBrush`            |
| Highlighting: Filter/Search FG    | Near Black            | `#FF1C1C1E` | *(Uses PrimaryForegroundColor)* | *(Uses TextForegroundBrush)*                |
| Highlighting: Timestamp FG        | Dark Blue             | `#FF00008B` | `HighlightTimestampColor`       | *(Used directly or via named color)*        |
| Button Hover Background           | Very Light Grey       | `#FFEFEFF4` | `ButtonHoverBackgroundColor`    | *(Used directly in Style)*                  |
| Button Pressed Background         | Light Grey            | `#FFE0E0E0` | `ButtonPressedBackgroundColor`  | *(Used directly in Style)*                  |
| Button Pressed Inner Shadow       | Medium Grey           | `#FFBEBEBE` | `ButtonPressedInnerShadowColor` | *(Used directly in Style)*                  |
| Button Pressed Inner Highlight    | White                 | `#FFFFFFFF` | `ButtonPressedInnerHighlightColor`| *(Used directly in Style)*                  |
| ComboBox Background               | White                 | `#FFFFFFFF` | `ComboBoxBackgroundColor`       | `ComboBoxBackgroundBrush`                   |
| ComboBox Border                   | Light Grey            | `#FFD1D1D6` | `ComboBoxBorderColor`           | `ComboBoxBorderBrush`                       |
| ComboBox Arrow                    | Medium Grey           | `#FF6D6D72` | `ComboBoxArrowColor`            | `ComboBoxArrowBrush`                        |
| ComboBox Dropdown Background      | White                 | `#FFFFFFFF` | `ComboBoxDropdownBackgroundColor`| `ComboBoxDropdownBackgroundBrush`          |
| ComboBox Dropdown Border          | Light Grey            | `#FFD1D1D6` | `ComboBoxDropdownBorderColor`   | `ComboBoxDropdownBorderBrush`               |
| ComboBox Item Hover Background    | Very Light Grey       | `#FFEFEFF4` | `ComboBoxItemHoverBackgroundColor`| `ComboBoxItemHoverBackgroundBrush`         |
| ComboBox Item Selected Background | Accent Color          | `#FF00BFFF` | `ComboBoxItemSelectedBackgroundColor`| `ComboBoxItemSelectedBackgroundBrush`   |
| ComboBox Item Selected Foreground | White                 | `#FFFFFFFF` | `ComboBoxItemSelectedForegroundColor`| `ComboBoxItemSelectedForegroundBrush`   |

*Note: Foreground colors for Filter/Search highlighting should use `TextForegroundBrush` for readability.*

## Typography

*   Use a clean, highly readable sans-serif font suitable for code/log viewing (e.g., Consolas, Fira Code, Inter, Roboto Mono, Segoe UI).
*   Maintain consistent font usage throughout the application.

## Depth & 3D Effects ("Subtle Shadow & Inset")

*   **Layering:** Panels (Filters, Stats Bar, Toolbars, Search Bar) use **very subtle light grey drop shadows** (`PanelShadowEffect`: Color `#FFAAAAAA`, ShadowDepth 1-3, BlurRadius 5-8, Opacity 0.1-0.2) *or* clean, thin (1px) `BorderBrush` lines for separation. Choose one approach for consistency.
*   **Interactivity/Focus:** Use the `NeonAccentColor` primarily for **solid borders (1-2px)** or **background fills** on focused/active elements:
    *   Text Inputs (Filters, Search) when focused: Show a 2px `AccentBrush` border.
    *   Active/Selected Buttons (Filter type, AND/OR/NOR): Use `AccentBrush` background fill or border.
    *   Active `ToggleButton` / `ToggleSwitch`: Use `AccentBrush` background fill for the active state.
    *   Selected items in the Filter `TreeViewItem`: Use `AccentBrush` background fill.
    *   Focused `ComboBox`: Show 2px `AccentBrush` border.
    *   *Avoid strong glows.* If a glow is used, it must be extremely subtle (e.g., BlurRadius 2-3, Opacity 0.2).
*   **Buttons/Controls (North-West Light Simulation):**
    *   *Default:* Flat white/light grey (`ControlBackgroundBrush`).
    *   *Hover:* Slightly darker background (`ButtonHoverBackgroundColor`) and potentially `AccentBrush` border.
    *   *Pressed:* Apply a subtle **inset effect** using inner shadows/highlights: 1px `ButtonPressedInnerShadowColor` on bottom/right, 1px `ButtonPressedInnerHighlightColor` on top/left within the button's template.

## Specific UI Element Styling

1.  **Window:** Standard light title bar. Background uses `WindowBackgroundBrush`.
2.  **Menu Bar:** Background uses `PanelBackgroundBrush`. Text uses `TextForegroundBrush`. Hovered `MenuItem` background uses `AccentBrush`, text uses `BaseBackgroundColor`.
3.  **Log Stats Bar:** Uses `CardPanelStyle` (Panel Background, subtle shadow/border). Text uses theme colors. Status numbers/icons tinted with `ErrorBrush`, `WarningBrush`, `InfoBrush`.
4.  **Filters Panel:** Uses `CardPanelStyle`.
    *   **Profile ComboBox:** Styled light (`ComboBoxBackgroundBrush`, `ComboBoxBorderBrush`). Dropdown uses `ComboBoxDropdownBackgroundBrush`. Selected item uses `ComboBoxItemSelectedBackgroundBrush` / `ComboBoxItemSelectedForegroundBrush`. Hover uses `ComboBoxItemHoverBackgroundBrush`. Gains `AccentBrush` border when focused.
    *   **Profile Management Buttons (New, Rename, Delete):** Styled light Buttons, placed near the ComboBox, using subtle inset effect on press.
    *   **Filter TreeView:** Background `PanelBackgroundBrush`. Items use `TextForegroundBrush`. Selected items have `AccentBrush` background fill. Disabled items have reduced opacity (e.g., 0.5). Use simple, distinct icons (filled with `SecondaryTextBrush` or `TextForegroundBrush`) for each filter type. Optionally, apply very subtle background differences for composite vs. value filters.
    *   **Filter Input (Edit Mode):** TextBox uses `ControlBackgroundBrush`, `TextForegroundBrush`. Gains 2px `AccentBrush` border when active.
    *   **Add/Edit/Remove Node Buttons:** Styled light Buttons, using subtle inset effect on press.
5.  **Display Options Toolbar:** Uses `CardPanelStyle`.
    *   **Toggle Switches (`Line Numbers`, `Highlight Timestamps`):** Use `ToggleSwitchStyle`. Active state uses `AccentBrush` background fill. Thumb uses `WindowBackgroundBrush`.
    *   **Context Lines Input:** `TextBox` styled light with accent border on focus. Up/down `Button`s styled light and flat with inset on press.
    *   **Busy Indicator:** Uses `BusySpinnerStyle` (animated rotating arc) with `AccentBrush` for the arc color, on a light background.
    *   **Search Status Text:** Uses `SecondaryTextBrush`.
6.  **Log View Area (AvalonEdit):**
    *   Background uses `EditorBackgroundBrush` (`BaseBackgroundColor`). Text uses `EditorForegroundBrush` (`TextForegroundBrush`).
    *   **Custom Highlighting:** Defined via `CustomHighlightingDefinition`, using theme brushes:
        *   Timestamp: `Highlighting.Timestamp` brush (e.g., DarkBlue).
        *   Error/Warn/Info: Text color set to `ErrorBrush`, `WarningBrush`, `InfoBrush`. Consider `FontWeight="Bold"` for Error.
        *   Filter Match: Background set to `Highlighting.FilterMatch.Background` brush (Pale Yellow). Foreground `TextForegroundBrush`.
        *   Search Match: Background set to `Highlighting.SearchMatch.Background` brush (Pale Turquoise). Foreground `TextForegroundBrush`.
    *   **Margins (`OriginalLineNumberMargin`, `VerticalLineMargin`, `ChunkSeparatorRenderer`):** Background matches editor. Text/Lines use `SecondaryTextBrush` or `DividerBrush`.
    *   **`OverviewRulerMargin`:** Track uses `OverviewRuler.Background`. Thumb uses `OverviewRuler.ThumbBrush` (darker grey). Markers use `OverviewRuler.SearchMarkerBrush` (Accent), `OverviewRuler.FilterMarkerBrush` (Warning), `OverviewRuler.ErrorMarkerBrush` (Error). Prepare for distinct shapes/colors per marker type.
    *   **Scrollbars:** Styled light (light track, darker grey `SecondaryTextBrush` thumb).
7.  **Search Bar:** Uses `CardPanelStyle`.
    *   **Previous/Next Buttons:** Styled light Buttons with inset effect on press. Use icons (`<`, `>`), potentially filled with `TextForegroundBrush` or `AccentBrush`.
    *   **Search Input TextBox:** Styled light with accent border on focus.
    *   **Case Sensitive CheckBox:** Styled light, uses `AccentBrush` for checkmark/indicator.