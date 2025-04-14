using Logonaut.Common;
using Logonaut.Filters;
using Newtonsoft.Json;
using System.IO; // Required for Path, File, Directory
using System; // Required for Environment, Exception
using System.Linq; // Required for Linq methods

namespace Logonaut.Core
{
    /// <summary>
    /// Manages loading and saving of application settings.
    /// </summary>
    public static class SettingsManager
    {
        private const string AppFolderName = "Logonaut";
        private const string SettingsFileName = "settings.json";

        private static string GetSettingsFilePath()
        {
            string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolderPath = Path.Combine(localAppDataPath, AppFolderName);

            // Ensure the application's settings directory exists
            Directory.CreateDirectory(appFolderPath);
            return Path.Combine(appFolderPath, SettingsFileName);
        }

        /// <summary>
        /// Saves the provided settings object to the settings file.
        /// </summary>
        /// <param name="settings">The settings object to save.</param>
        public static void SaveSettings(LogonautSettings settings)
        {
            try
            {
                string filePath = GetSettingsFilePath();
                // TypeNameHandling.All is crucial for serializing IFilter within FilterProfile
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All,
                    NullValueHandling = NullValueHandling.Ignore // Don't write null properties
                });
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save settings: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads the settings object from the settings file.
        /// If the file doesn't exist or an error occurs, returns a new default LogonautSettings object.
        /// </summary>
        /// <returns>The loaded LogonautSettings object or a default one.</returns>
        public static LogonautSettings LoadSettings()
        {
            string filePath = GetSettingsFilePath();
            LogonautSettings? loadedSettings = null;

            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    // Deserialize using TypeNameHandling.All
                    loadedSettings = JsonConvert.DeserializeObject<LogonautSettings>(json, new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.All,
                        // Optional: Error handling during deserialization
                        // Error = (sender, args) => {
                        //     System.Diagnostics.Debug.WriteLine($"Deserialization error: {args.ErrorContext.Error.Message}");
                        //     args.ErrorContext.Handled = true; // Attempt to continue if possible
                        // }
                    });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load settings: {ex.Message}", ex);
            }

            // If loading failed or file didn't exist, return new settings
            if (loadedSettings == null)
            {
                loadedSettings = new LogonautSettings();
            }

            // Ensure there is at least one profile
            if (loadedSettings.FilterProfiles == null || !loadedSettings.FilterProfiles.Any())
            {
                loadedSettings.FilterProfiles = new List<FilterProfile>
                {
                    new FilterProfile("Default", null) // Create a default profile
                };
                // Ensure LastActiveProfileName points to this new default if it was null/invalid
                if (string.IsNullOrEmpty(loadedSettings.LastActiveProfileName) ||
                    !loadedSettings.FilterProfiles.Any(p => p.Name == loadedSettings.LastActiveProfileName))
                {
                    loadedSettings.LastActiveProfileName = loadedSettings.FilterProfiles[0].Name;
                }
            }
            // Validate LastActiveProfileName exists, otherwise set to the first profile's name
            else if (string.IsNullOrEmpty(loadedSettings.LastActiveProfileName) ||
                     !loadedSettings.FilterProfiles.Any(p => p.Name == loadedSettings.LastActiveProfileName))
            {
                loadedSettings.LastActiveProfileName = loadedSettings.FilterProfiles.First().Name;
            }


            return loadedSettings;
        }
    }
}