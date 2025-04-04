# Logonaut

This is a WIP, not ready for use.

**Logonaut** is a modern, modular log viewer application for Windows built in C# using WPF and the MVVM pattern. It continuously tails log files, supports advanced hierarchical filtering, provides customizable highlighting, and offers a clean, responsive user interface. The application also saves user settings and filter profiles, and includes a convenient menu for selecting the log file to monitor.

## Features

- **Real-Time Log Tailing:**
  Continuously reads from a log file and updates the display asynchronously using Reactive Extensions and FileSystemWatcher.
- **Advanced Filtering:**
  Supports substring matching, regex patterns, and composite logical (AND/OR/NOR) filter conditions. Filters are organized in a hierarchical tree for complex expressions like `(A or (B and not C))`. Includes context line display.
- **Customizable Syntax Highlighting:**
  Define rules for highlighting timestamps, log levels (Error, Warn, Info), and other patterns. Highlighting configurations can be saved and loaded. Filter matches are also highlighted.
- **Original Line Numbers:**
  Displays the original line number from the source file next to each entry, even when filters are applied.
- **User-Friendly UI:**
  Built with WPF and AvalonEdit for a modern look and feel. Provides a dedicated panel for managing filters, a log display area, free-text search, and theme support (Light/Dark).
- **Configuration Persistence:**
  Loads user settings and saved filter profiles (each with a unique name) at startup and saves them at shutdown.

## Architecture

Logonaut is organized into several independent modules:

- **Logonaut.UI:**
  Contains the WPF user interface, including XAML views and ViewModels (using CommunityToolkit.Mvvm). Uses attached properties and custom controls (like the line number margin) to integrate with AvalonEdit.
- **Logonaut.Core:**
  Provides core business logic, including the `FilterEngine`.
- **Logonaut.Filters:**
  Implements the filtering system with various filter types (`SubstringFilter`, `RegexFilter`, `AndFilter`, `OrFilter`, `NorFilter`) and serialization logic.
- **Logonaut.LogTailing:**
  Handles asynchronous log file reading and tailing using `LogTailer` and `LogTailerManager`, exposing new log lines as an observable sequence via Reactive Extensions (Rx.NET).
- **Logonaut.Theming:**
  Manages application theming (e.g., dark/light mode) and styles.
- **Logonaut.Common:**
  Contains common data structures like `LogDocument` and `FilteredLogLine` used across the project.
- **Tests:**
  Unit tests for each module using MSTest, ensuring that filters, log tailing, UI logic, and services behave as expected.

## Documentation

For more detailed information on specific aspects of Logonaut, please refer to the following documents in the `doc/` directory:

*   [Application Requirements](doc/Requirements.md): Detailed functional and non-functional requirements.
*   [Design Requirements](doc/DesignRequirements.md): High-level design goals and constraints.
*   [Log File Processing Flow](doc/LogFileProcessingFlow.md): How log data is read, processed, and displayed.
*   [Reactive and Incremental Filtering](doc/ReactiveINcrementalFiltering.md): Details on the reactive filtering implementation.
*   [Original Line Number Management](doc/LineNumberManagement.md): Explanation of how original line numbers are preserved and displayed.
*   [Gemini Analysis (Code Review)](doc/GeminAnalysis.md): An automated analysis of potential improvements and issues (as of a specific point in time).

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio Code (or another preferred IDE)

### Building the Project

1.  **Clone the repository:**
    ```bash
    git clone https://github.com/larspensjo/Logonaut
    cd Logonaut
    ```

2.  **Build the solution:**
    ```bash
    dotnet build Logonaut.sln
    ```

### Running the Application

From the command line:
```bash
dotnet run --project src/Logonaut.UI/Logonaut.UI.csproj
```

Using Visual Studio Code:

Use the provided `tasks.json` and `launch.json` configurations to build and debug the application.

### Running Tests
Run all tests with the following command:
```bash
dotnet test
```

## Contributing
Contributions are welcome! If you encounter issues or have suggestions for improvements, please open an issue or submit a pull request.

## License
This project is licensed under the MIT License. See the LICENSE file for details.

## Acknowledgements
* WPF for providing a powerful desktop UI framework.
* MVVM Light Toolkit for simplifying MVVM-based development.
* Reactive Extensions (Rx.NET) for handling asynchronous and event-based programming.
* AvalonEdit for advanced text editing and log display capabilities.
