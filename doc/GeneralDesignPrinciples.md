# General Design Principles

This document outlines the general software design principles followed in this project, intended to be potentially reusable across different projects.

*   **Modularity:** Logic should be separated into independent modules with clearly defined APIs (interfaces or stable public classes). This facilitates maintainability, testability, and allows replacing or upgrading modules with minimal impact on the rest of the system.
*   **Testability:** Every module should have comprehensive unit tests to verify its functionality and prevent regressions. Using dependency injection and interfaces aids in creating testable code. MSTest is the chosen framework for this project.
*   **Clear APIs:** Interfaces and public members should be clearly documented and represent stable contracts between modules. Internal implementation details should be hidden where possible.
*   **Version Control:** The project is maintained in a Git repository for tracking changes, collaboration, and branching.
*   **Development Environment:** Visual Studio (or VS Code) is the primary IDE.
*   **Technology Stack:** The project utilizes .NET 8 and modern C# features. WPF is used for the UI layer where applicable.
*   **Dependency Management:** Dependencies (like NuGet packages) should be managed carefully and kept up-to-date where feasible.
*   Avoid using MessageBox for user interactions. Instead, find ways to interact inside the UI. For example, no need for error messages if buttons are disabled when unusable. And no need for a pop-up dialog asking for names when a regular input field directly in the UI can be used.