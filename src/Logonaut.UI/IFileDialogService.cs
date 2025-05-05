using Logonaut.Core;

namespace Logonaut.UI.Services;

public interface IFileDialogService
{
    /// <summary>
    /// Opens a file dialog and returns the selected file path.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="filter">Filter string for file types.</param>
    /// <returns>The selected file path, or null if cancelled.</returns>
    string? OpenFile(string title, string filter, string? initialDirectory = null);
}
