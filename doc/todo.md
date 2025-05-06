# TODO

* Refactor AnimatedSpinner into a flexible BusyIndicator (see [BusyIndicatorPlan.md](BusyIndicatorPlan.md)).
* Use MainViewModel.JumpStatusMessage for error messsages instead of MessageBox.
* Support multiple log windows, as tabs.
* CTRL+O for quick open file.
* Any filter change or context change while Auto Scroll is enabled shall automatically scroll to the new end.
* Animations for UI changes:
    * When Auto Scroll is automatically disabled, use an animation on top of the checkbox.
    * When the user initiates a search using CTR+F, focus is automatically changed to the search box. Use an animation on top of the search box to help the user notice the transition.
    * The same for CTRL+G, the go to line text box.
* When adding a Substring, it shall by default use the selected string from the logwindow, if any.
* The Auto Scroll option could be better visualized with a picture of an anchor. But it need to be placed at a proper place.
* Implement deployment using Inno
* When opening a new log, the dialog should remember last folder.
* Opening a new log file will not honor the Auto Scroll.
* When the simluator is enabled, rather than a file log, I want a special busy token.
    * When a simulator token is active, use a special animation.
* The animation used when opening a new file is broken.
* It seems as if settings are saved frequently, also during startup.
* The highlighted time stamps should be adjusted for themes.

* **SimulatorLogSource UI overlay**
*   6. Sliders or percentage inputs for INFO, WARN, ERROR, DEBUG, TRACE (could enforce sum=100%).
*   7. TextBox for keywords (comma-separated?), Checkbox "Inject Keywords Randomly". Slider for injection probability. Randomly inserts specified keywords into generated messages. Useful for testing filters.
