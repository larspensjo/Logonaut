# TODO
* Refactor AnimatedSpinner into a flexible BusyIndicator (see [BusyIndicatorPlan.md](BusyIndicatorPlan.md)).
* Use MainViewModel.JumpStatusMessage for error messsages instead of MessageBox.
* Support multiple log windows, as tabs.
* Second time loading a new log file, the "Processing..." isn't shown.
* When loading a big file, the application freezes.
* Tool tips don't look good in dark mode.
* Disable the tool tip for the main log window.
* CTRL+O for quick open file.
* BUG: Monitoring a log file that grows will add filtered lines, but not taking context lines into account.
* BUG: Monitor a growing log file. Toggle filters on/off. Nothing will be shown.
* Any filter change or context chage while Auto Scroll is enabled shall automatically scroll to the new end.
* Animations for UI changes:
    * When Auto Scroll is automatically disabled, use an animation on top of the checkbox.
    * When the user initiates a search using CTR+F, focus is automatically changed to the search box. Use an animation on top of the search box to help the user notice the transition.
    * The same for CTRL+G, the go to line text box.