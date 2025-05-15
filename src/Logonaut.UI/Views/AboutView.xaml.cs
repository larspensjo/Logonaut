// ===== File: C:\Users\larsp\src\Logonaut\src\Logonaut.UI\Views\AboutView.xaml.cs =====

using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace Logonaut.UI.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
    }

    // Event handler for Hyperlink clicks within this UserControl
    private void Hyperlink_RequestNavigate_Handler(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (System.Exception ex)
        {
            Debug.WriteLine($"Error opening hyperlink: {ex.Message}");
        }
        e.Handled = true;
    }
}

