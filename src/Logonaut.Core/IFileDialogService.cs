
namespace Logonaut.Core;

/// <summary>
/// Provides an abstraction for displaying an open file dialog and returning the selected file path.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Opens a file selection dialog and returns the chosen file path.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="filter">Filter string for file types.</param>
    /// <param name="initialDirectory">Optional directory to start in.</param>
    /// <returns>The selected file path, or null if the dialog was cancelled.</returns>
    string? OpenFile(string title, string filter, string? initialDirectory = null);
}
