using Logonaut.Filters;
using Newtonsoft.Json;
using System.Collections.Generic; // Required for List

namespace Logonaut.Common
{
    /// <summary>
    /// Represents the application settings that are saved and loaded.
    /// </summary>
    public class LogonautSettings
    {
        /// <summary>
        /// Stores all defined filter profiles.
        /// Serialized with TypeNameHandling.All for the IFilter within FilterProfile.
        /// </summary>
        public List<FilterProfile> FilterProfiles { get; set; } = new List<FilterProfile>();

        /// <summary>
        /// Stores the name of the filter profile that was last active.
        /// </summary>
        public string? LastActiveProfileName { get; set; }

        // --- Other Settings Remain ---
        public int ContextLines { get; set; } = 0;
        public bool ShowLineNumbers { get; set; } = true;
        public bool HighlightTimestamps { get; set; } = true;
        public bool IsCaseSensitiveSearch { get; set; } = false;

        public LogonautSettings() { }
    }
}