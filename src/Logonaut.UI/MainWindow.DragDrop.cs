using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input; // Required for RoutedUICommand
using System.Windows.Navigation;
using Logonaut.UI.ViewModels;
using Logonaut.Filters;
using System.Diagnostics;

namespace Logonaut.UI;

// All the event handlers related to Drag and Drop functionality for the Filter Palette and FilterTreeView.
// Drag-and-drop logic is a distinct feature set with its own event handlers and visual feedback mechanisms.
// THIS FILE IS NOW MOSTLY EMPTY as the logic has moved to FilterPanelView.xaml.cs
public partial class MainWindow : Window, IDisposable
{
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) // Catch potential errors opening the link
        {
            Debug.WriteLine($"Error opening hyperlink: {ex.Message}");
        }
        e.Handled = true;
    }
}
