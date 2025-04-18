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
    *   Never use exceptions for user errors, only when unepxected things happens.
    *   Avoid using a pop-up dialog with error messages when the user is doing something wrong. Instead, use techniques to disable controls that shouldn't be used, depending on state.
