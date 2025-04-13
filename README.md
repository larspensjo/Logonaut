# Logonaut

**Logonaut** is a modern, modular log viewer application for Windows built with C# and WPF. It provides real-time log tailing, advanced filtering capabilities, customizable syntax highlighting, and a clean, responsive user interface with theme support.

## Key Features

*   **Live Log Tailing:** Continuously monitors and displays updates from log files.
*   **Flexible Input:** Open log files or paste content directly from the clipboard.
*   **Advanced Filtering:** Create complex filter rules using substrings, regex, and logical operators (AND/OR/NOR) organized in manageable, named profiles.
*   **Custom Highlighting:** Define syntax highlighting rules for timestamps, log levels, and custom patterns. Filter matches are also highlighted.
*   **Original Line Numbers:** Tracks and displays the original line number for each log entry, even when filtered.
*   **Theming:** Supports Light and Dark themes ("Clinical Neon" and "Neon Night").
*   **Persistence:** Saves user settings, including all defined filter profiles and the last active one.

## Getting Started

### Prerequisites

*   .NET 8 SDK
*   Visual Studio or another compatible IDE/editor

### Building & Running

1.  Clone the repository.
2.  Build the solution: `dotnet build Logonaut.sln`
3.  Run the UI project: `dotnet run --project src/Logonaut.UI/Logonaut.UI.csproj`

### More Information

*   For detailed user features and requirements, see [UserRequirements.md](doc/UserRequirements.md).
*   For technical design and architecture details, see [ArchitectureAndDesign.md](doc/ArchitectureAndDesign.md).
*   For general software design principles used, see [GeneralDesignPrinciples.md](doc/GeneralDesignPrinciples.md).

## Contributing

Contributions, issues, and feature requests are welcome.

## License

This project is licensed under the MIT License.