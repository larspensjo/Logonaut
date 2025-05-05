using Logonaut.Common;
using Logonaut.Filters; // For TrueFilter default
using Newtonsoft.Json;
using System.IO;
using System;
using System.Linq;
using System.Collections.Generic; // For List

namespace Logonaut.Core;

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
            // Validate loaded settings (ensure at least one profile, valid LastActiveProfileName)
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
            // --- Default Simulator Settings ---
            SimulatorLPS = 10.0,
            SimulatorErrorFrequency = 100.0,
            SimulatorBurstSize = 1000.0
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
        if (settings.SimulatorLPS < 0) settings.SimulatorLPS = 10.0;
        if (settings.SimulatorErrorFrequency < 1) settings.SimulatorErrorFrequency = 100.0; // Min freq is 1
        if (settings.SimulatorBurstSize < 1) settings.SimulatorBurstSize = 1000.0; // Min burst is 1
    }
}
