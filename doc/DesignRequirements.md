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
* Use the new format where you define the namespace as "namespace xxxx;", and then the rest of the file belongs to that namespace (rather than the old where the content of the namespace was inside a block).
* The sourcode code shall, where possible, contain a comment with a reference to the requirement ID. Do not remove these IDs when source code is updated.
* Every unit test shall also, if possible, have a comment reference to the requirement ID.
* Big functions should have a short and concise summary as a multi-line comment. But don't include too many details, just the main idea.

## Code Commenting Standard

### Interface/Class Purpose Comments

**Goal:** To provide clear, concise explanations for the *purpose* and architectural *role* of key interfaces and classes. These comments should focus on the "why" rather than the detailed "how".

**Scope:** Apply this standard primarily to:
    *   Public interface definitions.
    *   Major public class implementations that represent core components.
    *   Use judgment for internal or simpler classes.

**Format:**
    *   Use standard C# block comments: `/* ... */`.
    *   Place the comment directly above the interface or class definition.
    *   **Do not** use XML Documentation Comments (`/// <summary>...`) for *this specific* high-level purpose comment. XML comments are still valuable for public API members (methods, properties) for IntelliSense and documentation generation.

**Content Structure:**
    Structure the comment using the following sections (use clear headings or separators within the comment):

    1.  **`Defines the contract for...` / `Implements the component responsible for...`**:
        *   A brief (1-2 sentence) high-level statement describing the fundamental responsibility or concept defined/implemented.

    2.  **`Purpose:`**:
        *   Explain *why* this interface/class exists. What problem does it solve in the context of the application?
        *   Describe its main function or goal.

    3.  **`Role:` / `Context:` / `Lifecycle:`** (Combine or use as appropriate):
        *   Explain where this component fits within the overall application architecture.
        *   Mention key interfaces/classes it interacts with or collaborates with (e.g., "decouples X from Y", "used by Z").
        *   If applicable, briefly describe its expected usage pattern or lifecycle (e.g., the Prepare/Start/Stop sequence for `ILogSource`).

    4.  **`Responsibilities:` (Optional but Recommended)**:
        *   A *brief* bulleted list of the primary tasks or capabilities this component is expected to handle. Keep this high-level.

    5.  **`Benefits:`**:
        *   Outline the advantages gained by having this abstraction or component (e.g., Decoupling, Testability, Flexibility, Reusability, Separation of Concerns).

    6.  **`Implementation Notes:` (Optional)**:
        *   Mention any crucial considerations for implementers or users, such as threading requirements (async), resource management (`IDisposable`), or specific patterns employed.

**Tone and Style:**
    *   Be clear and concise.
    *   Focus on the architectural intent and purpose.
    *   Avoid deep implementation details.
    *   Keep comments up-to-date as the code evolves.
    *   Keep the total size limited to 1000 characters or less, and not more than 25 lines.
