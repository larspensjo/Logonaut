using Logonaut.Common;

namespace Logonaut.Core
{
    public interface ISettingsService
    {
        /// <summary>
        /// Loads the application settings.
        /// </summary>
        /// <returns>The loaded settings, or default settings if loading fails or none exist.</returns>
        LogonautSettings LoadSettings();

        /// <summary>
        /// Saves the provided application settings.
        /// </summary>
        /// <param name="settings">The settings object to save.</param>
        void SaveSettings(LogonautSettings settings);
    }
}
