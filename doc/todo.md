# TODO

This document has a list of ideas. and problems. Some might be out-of-date.

* Theming for Adorners: EmptyDropTargetAdorner creates brushes in code-behind. Consider moving these to theme resource dictionaries for better maintainability, similar to other controls.
* Bulk Color Assignment: For composite filters, changing the color should affect all children.
* Color-Based Quick Filters/Toggles: Above the log view or in the filter panel, show a row of small colored swatches representing all currently used highlight colors in the active filter profile.
* Refactor AnimatedSpinner into a flexible BusyIndicator (see [Plan.BusyIndicator.md](Busy Indicator Plan)).
* Use MainViewModel.JumpStatusMessage for error messsages instead of MessageBox.
* Support multiple log windows, as tabs. Every log window shall have its own selected filter profile.
    * Investigate concerns whether undo/redo is properly supported by the TABS system.
    * When changing filters in a profile, it will not have any effect on other TABs until you switch TAB.
    * See separate plan at [Plan.TabbedInterface.md](Tabbed interface plan)
    * Some UI bindings may need to be updated for the TabbedControl?
    * MainViewModel.FilterTreeInteraction.cs: Review commands like AddFilter, RemoveFilterNode. They modify ActiveFilterProfile.RootFilterViewModel. After Execute(action) in these commands, you'll need to ensure the ActiveTabViewModel (if it's using this ActiveFilterProfile) re-evaluates its display. The current TriggerFilterUpdate() in MainViewModel.Execute() should be adapted to call ActiveTabViewModel?.ApplyCurrentFilters(...).
    * Global UI Settings (ContextLines, HighlightTimestamps): When these global settings in MainViewModel change, the ActiveTabViewModel needs to be informed so it can update its display/processing. OnContextLinesChanged already calls ActiveTabViewModel?.ApplyCurrentFilters. A similar notification mechanism will be needed if, for instance, HighlightTimestamps is a per-tab setting visually configured through the active tab's UI. For now, if it's a global setting, the TabViewModel's AvalonEdit setup would read this global value on activation.
* Any filter change or context change while Auto Scroll is enabled shall automatically scroll to the new end.
* Implement deployment using Inno
* Opening a new log file shall honor the Auto Scroll.
* When the simluator is enabled, rather than a file log, I want a special busy token.
    * When a simulator token is active, use a special animation.
* It seems as if settings are saved frequently, also during startup.
* The treeview of filters should have some visual indicator for what patterns are currently used for matching.
* The Anchor button is now automatically when new lines are added to the log window.
* When dragging the separator between the filter view and the log view, I want the windows to update as I drag.
* Implement drag and drop of filters to other composite nodes.
* Switching themes did not update the highlights used by filters.
* Splitting xaml into smaller controls:
*   Toolbar UserControl: Extract the toolbar above the AvalonEdit control into its own LogDisplayToolBarView.xaml.
*   Search Panel UserControl: Extract the search panel below AvalonEdit into SearchPanelView.xaml.
*   Stats Bar UserControl: Extract the status bar at the bottom into StatusBarView.xaml.
* ViewModel Specialization: As MainViewModel grows, consider if parts of its logic could be extracted into more specialized ViewModels that these new UserControls might bind to, rather than everything binding directly to the main MainViewModel. This would be a larger refactoring. For instance, a FilterPanelViewModel could be created and exposed as a property on MainViewModel.
* Use CTR+ mouse wheel to increase and decrease font size.
* Implement ideas [IdeasUsingTimeStamps.md](how to use time stamps).

## Default highlights
    * Extend the list of default highlights. For example, the date/time is automatically highlighted.
    * Highlight file paths.
    * Highlight for http/https links.
    * Enable timestamp highlighting of: '2025-06-18 08:59:14.892'
    * Create an option dialog where you can control what highlights to enable.

## Jump-to-line
    * Doesn't work.
    * If I select a line and then disable the filter, I want the log window to keep the selected line visible.
    * The Jump-to-line should have a button beside it, which you can click to activate the jump-to-line.
    * Error Handling for ScrollToSelectedLine: The current ScrollToSelectedLine has try-catch blocks but mainly logs to Debug. More user-visible feedback or robust recovery could be considered if scrolling errors become common.

## Animations for UI changes:
    * When Auto Scroll is automatically disabled, use an animation on top of the checkbox.
    * When the user initiates a search using CTR+F, focus is automatically changed to the search box. Use an animation on top of the search box to help the user notice the transition.
    * The same for CTRL+G, the go to line text box.

## Searching
    * Ctrl+F3 Search from Current Position: LogOutputEditor_PreviewKeyDown for Ctrl+F3 should search from the current selection/caret position rather than always starting from the beginning.
    * A cross that clears the current search.
    * The markings on the scrollbar should update live (with a debounce), not wait until the user clicks "next".

## Performance
    * Using scrollbar for big files is really slow, especially with search active

## SimulatorLogSource UI overlay
    *   6. Sliders or percentage inputs for INFO, WARN, ERROR, DEBUG, TRACE (could enforce sum=100%).
    *   7. TextBox for keywords (comma-separated?), Checkbox "Inject Keywords Randomly". Slider for injection probability. Randomly inserts specified keywords into generated messages. Useful for testing filters.

* Enhanced dynamic substring
    * A quick, subtle animation (e.g., a brief pulse or a quick color fade-in) when it enables could make the transition more engaging.
    * Ellipsis Placement: Middle ellipsis (FirstPart...LastPart) is often better for recognizable strings than just trailing ellipsis if the beginning and end are important.
    * Tooltip Disabled: "Select single-line text in the log to create a filter from it."
    * Tooltip Enabled: "Drag to create a Substring filter: '[full selected text]'" (tooltip can show the full text even if the palette item shows a shortened one).
