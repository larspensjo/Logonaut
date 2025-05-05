using Microsoft.Win32;

namespace Logonaut.UI.Services
{
    public class FileDialogService : IFileDialogService
    {
        public string? OpenFile(string title, string filter, string? initialDirectory = null)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                RestoreDirectory = true
            };

            // Set the initial directory if provided and valid
            if (!string.IsNullOrEmpty(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            bool? result = dialog.ShowDialog();
            return result == true ? dialog.FileName : null;
        }
    }
}
