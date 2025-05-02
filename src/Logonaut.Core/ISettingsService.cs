using Logonaut.Common;

namespace Logonaut.Core;

/*
* Defines the contract for managing application settings persistence.
*
* Purpose:
* To abstract the mechanism used for loading and saving application settings
* (represented by the LogonautSettings object).
*
* Role & Benefits:
* - Decouples the rest of the application (e.g., MainViewModel) from the specifics
*   of how settings are stored (e.g., file system, registry).
* - Centralizes settings management logic.
* - Enables different storage implementations (e.g., FileSystemSettingsService).
* - Improves testability by allowing mock settings services.
*
* Implementations handle the actual reading/writing of the settings data.
*/
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
