using Microsoft.Win32;

namespace Logonaut.UI.Services
{
    public class FileDialogService : IFileDialogService
    {
        public string OpenFile(string title, string filter)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter
            };
            bool? result = dialog.ShowDialog();
            return result == true ? dialog.FileName : null;
        }
    }
}
