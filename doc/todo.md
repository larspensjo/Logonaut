# TODO

* Refactor AnimatedSpinner into a flexible BusyIndicator (see [BusyIndicatorPlan.md](BusyIndicatorPlan.md)).
* Use MainViewModel.JumpStatusMessage for error messsages instead of MessageBox.
* Support multiple log windows, as tabs.
* Second time loading a new log file, the "Processing..." isn't shown.
* Tool tips don't look good in dark mode.
* Disable the tool tip for the main log window.
* CTRL+O for quick open file.
* Any filter change or context chage while Auto Scroll is enabled shall automatically scroll to the new end.
* Animations for UI changes:
    * When Auto Scroll is automatically disabled, use an animation on top of the checkbox.
    * When the user initiates a search using CTR+F, focus is automatically changed to the search box. Use an animation on top of the search box to help the user notice the transition.
    * The same for CTRL+G, the go to line text box.
* When adding a Substring, it shall by default use the selected string from the logwindow, if any.
* The Auto Scroll option could be better visualized with a picture of an anchor. But it need to be placed at a proper place.
* Implement deployment using Inno
* When opening a new log, the dialog should remember last folder.

* **SimulatorLogSource UI overlay**
*   1. Implement the key combination trigger and the overlay UI placement over the filter tree. Secret key to toggle overlay: Ctrl+Alt+Shift+S. (DONE)
*   2. Add basic controls: Lines Per Second slider, Start/Stop/Restart buttons, Clear Log button.
*   3. Start/Stop/Restart buttons within the simulator UI. Controls the Timer in SimulatorLogSource. Needs to be independent of file opening.
*   4. Add an "Error Rate %" slider and an "Inject ERROR Now" button.
*   5. Refactor SimulatorLogSource to accept and use these basic parameters.
*   6. Sliders or percentage inputs for INFO, WARN, ERROR, DEBUG, TRACE (could enforce sum=100%).
*   7. TextBox for keywords (comma-separated?), Checkbox "Inject Keywords Randomly". Slider for injection probability. Randomly inserts specified keywords into generated messages. Useful for testing filters.
*   8. Burst mode. Numeric inputs for "Lines per Burst", "Burst Interval (ms)", "Pause Between Bursts (ms)". Button "Enable Burst Mode". Generate lines rapidly in bursts, followed by pauses.