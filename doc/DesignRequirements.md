# Design requirements

*   I want logic separated into independent modules, with clearly defined APIs. That will make it easy to replace a module without changing code all over the place.
*   Every module shall have unit tests, to verify the functionality.
*   Use dependency injection and interface types instead of hard-coded references to make unit testing easier.
*   I am going to use Microsoft Visual Code for this project, organized in a git repository.
*   The project shall use .NET 8 and a modern version of C#.
*   The design of the highlighting shall be programmatically controlled, not a hard coded set of rules. I expect to use a wide variation of dynamically controlled rules.
*   The highlighting system should support:
    *   User-configurable patterns for timestamps and other log elements
    *   Custom colors, font weights, and styles for different log elements
    *   The ability to save and load highlighting configurations
    *   Real-time updates to highlighting as configurations change
*   The filter system shall allow the user to define, save, load, and select between multiple named filter configurations (filter trees).
*   Using MSTest for unit tests
*   For error management, I prefer:
    *   Use exceptions for unepxected programming errors, but not for user errors.
    *   Avoid using a pop-up dialog with error messages when the user is doing something wrong. Instead, use techniques to disable controls that shouldn't be used, depending on state.
* For C#, use file-scoped namespace declarations.
* The sourcode code shall, where possible, contain a comment with a reference to the requirement ID. Do not remove these IDs when source code is updated.
* Every unit test shall also, if possible, have a comment reference to the requirement ID.
* Big functions should have a short and concise summary as a multi-line comment. But don't include too many details, just the main idea.

## Code Commenting Standard

### Interface/Class Purpose Comments

* Every class and namespace shall have a short and concise comment that explains the gist of it. Limit it to 2 paragraphs. Use block comment format for these.
* Significant and key functions in a class shall also have a two paragraph block comment.
