# TODO

* Refactor AnimatedSpinner into a flexible BusyIndicator (see [BusyIndicatorPlan.md](BusyIndicatorPlan.md)).
* Use MainViewModel.JumpStatusMessage for error messsages instead of MessageBox.
* Support multiple log windows, as tabs. Every log window shall have its own selected filter profile.
* CTRL+O for quick open file.
* Any filter change or context change while Auto Scroll is enabled shall automatically scroll to the new end.
* Animations for UI changes:
    * When Auto Scroll is automatically disabled, use an animation on top of the checkbox.
    * When the user initiates a search using CTR+F, focus is automatically changed to the search box. Use an animation on top of the search box to help the user notice the transition.
    * The same for CTRL+G, the go to line text box.
* Implement deployment using Inno
* Opening a new log file shall honor the Auto Scroll.
* When the simluator is enabled, rather than a file log, I want a special busy token.
    * When a simulator token is active, use a special animation.
* It seems as if settings are saved frequently, also during startup.
* The highlighted time stamps should be adjusted for themes.
* It should be possible to select what colors are used for every substring. Default should be red, but it is enough to supply a few alternatives (green, yellow and blue)

* **SimulatorLogSource UI overlay**
*   6. Sliders or percentage inputs for INFO, WARN, ERROR, DEBUG, TRACE (could enforce sum=100%).
*   7. TextBox for keywords (comma-separated?), Checkbox "Inject Keywords Randomly". Slider for injection probability. Randomly inserts specified keywords into generated messages. Useful for testing filters.

* Enhanced dynamic substring
    * A quick, subtle animation (e.g., a brief pulse or a quick color fade-in) when it enables could make the transition more engaging.
    * Ellipsis Placement: Middle ellipsis (FirstPart...LastPart) is often better for recognizable strings than just trailing ellipsis if the beginning and end are important.
    * Tooltip Disabled: "Select single-line text in the log to create a filter from it."
    * Tooltip Enabled: "Drag to create a Substring filter: '[full selected text]'" (tooltip can show the full text even if the palette item shows a shortened one).
