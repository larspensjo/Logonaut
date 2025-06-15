// ===== File: C:\Users\larsp\src\Logonaut\src\Logonaut.Core\FileSystemSettingsService.cs =====
using Logonaut.Common;
using Logonaut.Filters;
using Newtonsoft.Json;
using System.IO;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Logonaut.Core;

/*
 * Provides services for loading and saving application settings to the file system.
 * This service handles the persistence of LogonautSettings, including default
 * creation and validation of loaded settings.
 */
public class FileSystemSettingsService : ISettingsService
{
    private const string AppFolderName = "Logonaut";
    private const string SettingsFileName = "settings.json";

    private string GetSettingsFilePath()
    {
        string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolderPath = Path.Combine(localAppDataPath, AppFolderName);
        Directory.CreateDirectory(appFolderPath);
        return Path.Combine(appFolderPath, SettingsFileName);
    }

    public LogonautSettings LoadSettings()
    {
        string filePath = GetSettingsFilePath();
        LogonautSettings? loadedSettings = null;

        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                loadedSettings = JsonConvert.DeserializeObject<LogonautSettings>(json, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All,
                    // Optional error handling
                });
            }
        }
        catch (Exception ex)
        {
            // Log the error appropriately
            System.Diagnostics.Debug.WriteLine($"WARN: Failed to load settings from {filePath}. Error: {ex.Message}");
            throw new Exception($"Failed to load settings: {ex.Message}", ex);
        }

        // If loading failed or file didn't exist, create default settings
        if (loadedSettings == null)
        {
            loadedSettings = CreateDefaultSettings();
        }
        else
        {
            // Validate loaded settings (ensure at least one profile, valid LastActiveProfileName, etc.)
            EnsureValidSettings(loadedSettings);
        }

        return loadedSettings;
    }

    public void SaveSettings(LogonautSettings settings)
    {
        try
        {
            string filePath = GetSettingsFilePath();
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                NullValueHandling = NullValueHandling.Ignore
            });
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            // Log the error appropriately
            System.Diagnostics.Debug.WriteLine($"ERROR: Failed to save settings to {GetSettingsFilePath()}. Error: {ex.Message}");
            throw new Exception($"Failed to save settings: {ex.Message}", ex);
        }
    }

    /*
     * Creates a LogonautSettings object with default values for all settings.
     * This is used when no settings file is found or if loading fails.
     */
    private LogonautSettings CreateDefaultSettings()
    {
        var settings = new LogonautSettings
        {
            FilterProfiles = new List<FilterProfile>
            {
                new FilterProfile("Default", null)
            },
            LastActiveProfileName = "Default",
            ContextLines = 0,
            ShowLineNumbers = true,
            HighlightTimestamps = true,
            IsCaseSensitiveSearch = false,
            AutoScrollToTail = true,
            LastOpenedFolderPath = null,
            SimulatorLPS = 10.0,
            SimulatorErrorFrequency = 100.0,
            SimulatorBurstSize = 1000.0,
            EditorFontFamilyName = "Consolas",
            EditorFontSize = 12.0,

            // Persisted window state
            WindowState = AppWindowState.Normal,

            // --- Default Window Geometry Settings ---
            WindowTop = 100,        // Sensible default top
            WindowLeft = 100,       // Sensible default left
            WindowHeight = 700,     // Sensible default height
            WindowWidth = 1000,     // Sensible default width
            FilterPanelWidth = 250  // Sensible default filter panel width
        };
        return settings;
    }

    private void EnsureValidSettings(LogonautSettings settings)
    {
        if (settings.FilterProfiles == null || !settings.FilterProfiles.Any())
        {
            settings.FilterProfiles = new List<FilterProfile>
            {
                new FilterProfile("Default", null) // Null filter is used when there are no filters
            };
            settings.LastActiveProfileName = "Default";
        }
        else if (string.IsNullOrEmpty(settings.LastActiveProfileName) ||
                !settings.FilterProfiles.Any(p => p.Name == settings.LastActiveProfileName))
        {
            // Ensure LastActiveProfileName is valid, default to first if not
            settings.LastActiveProfileName = settings.FilterProfiles.First().Name;
        }

        // Simulator settings validation
        if (settings.SimulatorLPS < 0) settings.SimulatorLPS = 10.0;
        if (settings.SimulatorErrorFrequency < 1) settings.SimulatorErrorFrequency = 100.0; // Min freq is 1
        if (settings.SimulatorBurstSize < 1) settings.SimulatorBurstSize = 1000.0; // Min burst is 1

        // Font settings validation
        var availableFonts = new List<string> { "Consolas", "Courier New", "Cascadia Mono", "Lucida Console" };
        if (string.IsNullOrEmpty(settings.EditorFontFamilyName) || !availableFonts.Contains(settings.EditorFontFamilyName))
        {
            settings.EditorFontFamilyName = "Consolas"; // Default if invalid or not in our curated list
        }
        // Ensure font size is within a reasonable range
        if (settings.EditorFontSize < 6.0 || settings.EditorFontSize > 72.0)
        {
            settings.EditorFontSize = 12.0; // Default if out of range
        }

        // --- Window Geometry Validation ---
        // Ensure window dimensions are positive and within reasonable screen bounds (optional, but good practice)
        // For Top/Left, we could check against SystemParameters.VirtualScreenWidth/Height,
        // but simpler is to ensure they are not excessively negative.
        if (settings.WindowHeight <= 0) settings.WindowHeight = 700;
        if (settings.WindowWidth <= 0) settings.WindowWidth = 1000;
        if (settings.WindowTop < -10000) settings.WindowTop = 100; // Avoid far off-screen positions
        if (settings.WindowLeft < -10000) settings.WindowLeft = 100;

        // --- Grid Splitter Validation ---
        if (settings.FilterPanelWidth <= 0) settings.FilterPanelWidth = 250;
        // Could add a max width check if necessary, e.g., not wider than half the default window width.
        if (settings.FilterPanelWidth > settings.WindowWidth * 0.8) settings.FilterPanelWidth = 250;
    }
}
