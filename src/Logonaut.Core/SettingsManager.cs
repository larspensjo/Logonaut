using Logonaut.Common; // For LogonautSettings
using Newtonsoft.Json;

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
                // Use Newtonsoft.Json consistent with FilterSerializer
                // TypeNameHandling.All is crucial for serializing the IFilter interface correctly
                string json = JsonConvert.SerializeObject(settings, Formatting.Indented, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All
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
            try
            {
                if (!File.Exists(filePath))
                    return new LogonautSettings();

                string json = File.ReadAllText(filePath);
                // Deserialize using TypeNameHandling.All to reconstruct the IFilter tree
                var loadedSettings = JsonConvert.DeserializeObject<LogonautSettings>(json, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.All
                });

                if (loadedSettings != null)
                {
                    return loadedSettings;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load settings: {ex.Message}", ex);
            }

            // Return default settings if file doesn't exist or loading failed
            return new LogonautSettings();
        }
    }
}