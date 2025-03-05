# Requirements
I want to create a Windows application, written in C#.

It is a log viewer. It shall read input from a file and display it.
I have the following requirements:
* It shall continue to read and asynchronously update the display.
* The log is a continuous process, which may go on for hours.
* There shall also be a free text search function. The user enters a string, which is then highlighted in the output.
* There shall be buttons to move the display up and down for the free text search.
* There shall be some kind of indication showing how much of the log output is currently included in the display.
* The application shall support dynamic window resizing by the user.
* Many log files have a prefix in the format of a date or time. It shall be possible to detect this prefix and visibly mark it, making it easy for the user to see.
* The application shall optionally support both a dark theme.
* The application display shall be in a nice and pleasingly contemporary way. Make this flexible, to enable future style changes without having to manually edit every layout detail.
* The application shall use a configuration that is loaded at startup, and saved at shutdown. The configuration shall be used to remember user settings.
* The application shall have a menu where I can select what log file to monitor.
* It shall be possible to scroll up and down the filtered output.

## Filters
* The Log viewer shall support filters.
* It shall be possible to add and remove filters while the application is running.
* The filters shall be based on sub string text matching. Just regular text, not regexp. The text is tested against all positions in every line.
* The filters shall support negations. That is, lines not having a text.
* The filters shall support AND+OR combinations. That is, you shall be able to say I want all lines with A and B, or I want all lines with A or B.
* The filters shall be organized in a hierarchy, to make it possible to have conditions like (A or (B and not C)).
* It shall be possible to set a line number context (default 0). For example, five lines before and after every match. There is only one such number, common for all matches.
* The text used for filtering shall be marked visibly in the output. Maybe as a colored background or a changed font. Any common procedure is fine.
* Every filter shall be possible to enable and disable. That way, you don't need to remove filters you temporarily don't need.
* The filter output shall update in real-time. Maybe some proper poll period is good, no need to update too frequently.
* Whenever the filters are changed, the filtered output shall update dynamically.
* It shall be possible to also save one or more filter settings with the configuration. Each such filter tree shall have a name, making it easy for the user to browse.
* The log file can be very big, 100 of Megabytes. Updating text and applying filters must be effective.

### Filter controls
* The application shall organize the filters in a tree-like view, making it easy to expand and navigate for the user.
* The filter control would be in a panel of its own. Maybe to the left side of the application, while the right side shows the log output.
* When working with the filters, the application shall show the tree-like view of them and provide controls to easily manipulate the tree. To add and remove items, move items within the tree.
* You start with no filters. There can only be one filter at the top.
* When a filter is added, the user shall select what type of filter to use.
* If the current filter is empty, a new filter will become the current filter.
* If there is a filter shown in the tree view, the user needs to click on the filter that will be modified.
* When a filter is added and the current filter isn't empty, the new filter shall be added to the selected filter. For example, add another sub-filter to an AndFilter.
* When the user clicks on "Remove filter", the currently selected filter shall be removed. This can leave the top filter empty.
* I want several buttons to add filters. One button for each filter type: SubString, AndFilter, OrFilter, NegationFilter.
* When a SubstringFilter is added, it shall automatically receive input focus.