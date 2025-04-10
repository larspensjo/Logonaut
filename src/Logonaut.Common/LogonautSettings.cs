using Logonaut.Filters;
using Newtonsoft.Json; // Required for attributes if using explicit serialization

namespace Logonaut.Common
{
    /// <summary>
    /// Represents the application settings that are saved and loaded.
    /// </summary>
    public class LogonautSettings
    {
        /// <summary>
        /// The root of the filter tree configuration.
        /// Uses TypeNameHandling.All during serialization to handle the IFilter interface.
        /// </summary>
        public IFilter? RootFilter { get; set; }

        /// <summary>
        /// The number of context lines to display around filter matches.
        /// </summary>
        public int ContextLines { get; set; } = 0; // Default value

        /// <summary>
        /// Whether to show the original line numbers margin.
        /// </summary>
        public bool ShowLineNumbers { get; set; } = true; // Default value

        /// <summary>
        /// Whether to apply timestamp highlighting rules.
        /// </summary>
        public bool HighlightTimestamps { get; set; } = true; // Default value

        /// <summary>
        /// Whether to use case-sensitive search.
        /// </summary>
        public bool IsCaseSensitiveSearch { get; set; } = false; // Default value

        // Consider adding other settings here in the future, e.g.:
        // public string LastTheme { get; set; } = "Light";
        // public double MainWindowWidth { get; set; } = 1000;
        // public double MainWindowHeight { get; set; } = 600;
        // public string LastLogFile { get; set; }

        /// <summary>
        /// Parameterless constructor for deserialization and default creation.
        /// </summary>
        public LogonautSettings() { }
    }
}