# TODO

*   **Refactor Filtering for Performance (Incremental Updates):**
    *   **Goal:** Address performance bottleneck where frequent log updates cause UI lag due to full re-filtering. Implement incremental filtering for new lines while retaining full re-filtering for settings changes.
    *   **Steps:**
        1.  Introduce distinct update types (e.g., `ReplaceFilteredUpdate`, `AppendFilteredUpdate`).
        2.  Adapt `MainViewModel.ApplyFilteredUpdate` to differentiate between `Replace` and `Append` logic.
        3.  Implement `FilterEngine.ApplyFilterToSubset` to filter only new lines (with context lookup).
        4.  Refactor `LogFilterProcessor` to have two pipelines:
            *   *New Lines:* Use `ApplyFilterToSubset` -> Emit `AppendFilteredUpdate`.
            *   *Settings Change:* Use existing full `ApplyFilters` -> Emit `ReplaceFilteredUpdate`.
        5.  Implement the append logic in `MainViewModel` (add to collection, append text to editor).
        6.  Update unit tests (`FilteredUpdates_RapidLineEmits...`) to verify the new behavior.
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
* Implement deployment
* When opening a new log, the dialog should remember last folder.