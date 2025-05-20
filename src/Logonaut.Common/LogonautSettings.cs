using Logonaut.Filters;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Windows; // Required for WindowState

namespace Logonaut.Common;

/*
 * Represents the application settings that are saved and loaded.
 * This class holds various user-configurable options, including filter profiles,
 * display preferences, simulator configurations, and editor font settings.
 */
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

    /// <summary>
    /// Stores the path of the folder from which the last log file was successfully opened.
    /// </summary>
    public string? LastOpenedFolderPath { get; set; }

    // Display/Search Settings
    public int ContextLines { get; set; } = 0;
    public bool ShowLineNumbers { get; set; } = true;
    public bool HighlightTimestamps { get; set; } = true;
    public bool IsCaseSensitiveSearch { get; set; } = false;
    public bool AutoScrollToTail { get; set; } = true;

    // Simulator Settings ---
    public double SimulatorLPS { get; set; } = 10.0; // Match VM property type and default
    public double SimulatorErrorFrequency { get; set; } = 100.0; // Match VM property type and default
    public double SimulatorBurstSize { get; set; } = 1000.0; // Match VM property type and default

    // Font Settings
    public string EditorFontFamilyName { get; set; } = "Consolas"; // Default font
    public double EditorFontSize { get; set; } = 12.0;          // Default font size

    // Window Geometry Settings
    public double WindowTop { get; set; } = 100; // Default position
    public double WindowLeft { get; set; } = 100; // Default position
    public double WindowHeight { get; set; } = 700; // Default size
    public double WindowWidth { get; set; } = 1000; // Default size

    // GridSplitter Settings (Column 0 width for Filter Panel)
    // Using GridLength.Value directly. GridLength itself is not easily serializable.
    public double FilterPanelWidth { get; set; } = 250; // Default width for the filter panel

    public LogonautSettings() { }
}
