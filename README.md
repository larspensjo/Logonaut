# Logonaut

This is a WIP, not ready for use.

**Logonaut** is a modern, modular log viewer application for Windows built in C# using WPF and the MVVM Light pattern. It continuously tails log files, supports advanced hierarchical filtering, and provides a clean, responsive user interface. The application also saves user settings and filter profiles, and includes a convenient menu for selecting the log file to monitor.

## Features

- **Real-Time Log Tailing:**  
  Continuously reads from a log file and updates the display asynchronously using Reactive Extensions and FileSystemWatcher.

- **Advanced Filtering:**  
  Supports substring matching, negation, and composite logical (AND/OR) filter conditions. Filters are organized in a hierarchical tree for complex expressions like `(A or (B and not C))`.

- **User-Friendly UI:**  
  Built with WPF and AvalonEdit for a modern look and feel. Provides a dedicated panel for managing filters, a log display area with syntax highlighting, and free-text search with navigation.

- **Configuration Persistence:**  
  Loads user settings and saved filter profiles (each with a unique name) at startup and saves them at shutdown.

- **Log File Selection:**  
  A menu option allows you to choose the log file to monitor, using an abstracted file dialog service to keep the UI logic testable.

## Architecture

Logonaut is organized into several independent modules:

- **Logonaut.UI:**  
  Contains the WPF user interface, including XAML views and ViewModels (built with MVVM Light). Uses attached properties to enable data binding to controls like AvalonEdit.

- **Logonaut.Core:**  
  Provides core business logic and shared utilities.

- **Logonaut.Filters:**  
  Implements the filtering engine with support for simple and composite filters (AND/OR/NOT).

- **Logonaut.LogTailing:**  
  Handles asynchronous log file reading and tailing, exposing new log lines as an observable sequence.

- **Logonaut.Theming:**  
  Manages application theming (e.g., dark/light mode) and styles.

- **Logonaut.Common:**  
  Contains common utilities and helper classes used across the project.

- **Tests:**  
  Unit tests for each module using MSTest, ensuring that filters, log tailing, UI logic, and services behave as expected.

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio Code (or another preferred IDE)

### Building the Project

1. **Clone the repository:**
```bash
git clone https://github.com/larspensjo/Logonaut
cd Logonaut
```

2. **Build the solution:**
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

## Configuration
User settings and filter profiles are loaded at startup and saved at shutdown. The configuration includes:
* Window layout and theme preferences.
* Saved filter profiles (each with a unique name).
* The last selected log file to monitor.
These settings are stored in a configuration file (e.g., JSON or XML) in the application data directory.

## Contributing
Contributions are welcome! If you encounter issues or have suggestions for improvements, please open an issue or submit a pull request.

## License
This project is licensed under the MIT License. See the LICENSE file for details.

## Acknowledgements
* WPF for providing a powerful desktop UI framework.
* MVVM Light Toolkit for simplifying MVVM-based development.
* Reactive Extensions (Rx.NET) for handling asynchronous and event-based programming.
* AvalonEdit for advanced text editing and log display capabilities.
