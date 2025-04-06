using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Logonaut.UI.ViewModels; // Ensure this using is present
using System; // For EventArgs

namespace Logonaut.UI
{
    public partial class MainWindow : Window
    {
        // --- Dark Title Bar Support ---
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private static bool IsWindows10OrGreater(int build = -1)
        {
            try {
                 return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= build;
            } catch { return false; } // Avoid potential exceptions
        }

        private void SetDarkTitleBar(IntPtr hwnd)
        {
            if (IsWindows10OrGreater(17763)) // Check for supported Windows 10 versions
            {
                int attribute = DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;
                if (IsWindows10OrGreater(18985)) // Build 18985+ uses attribute 20
                {
                    attribute = DWMWA_USE_IMMERSIVE_DARK_MODE;
                }

                int useImmersiveDarkMode = 1; // 1 = True
                try
                {
                    int result = DwmSetWindowAttribute(hwnd, attribute, ref useImmersiveDarkMode, sizeof(int));
                    if (result != 0) // S_OK = 0
                    {
                        // TODO better error handling
                        System.Diagnostics.Debug.WriteLine($"DwmSetWindowAttribute failed with result: {result}");
                    }
                }
                catch (Exception ex)
                {
                    // TODO: Better error handling
                    System.Diagnostics.Debug.WriteLine($"Failed to set dark title bar attribute: {ex.Message}");
                }
            }
        }
        // --- End Dark Title Bar Support ---


        public MainWindow()
        {
            // InitializeComponent() is the method generated from the XAML. When it runs, it parses the XAML, creates the UI elements, and wires them up.
            InitializeComponent();
            DataContext = new ViewModels.MainViewModel(); // Assuming you set DC here or earlier
            this.SourceInitialized += MainWindow_SourceInitialized;

            // Create and add the custom margin
            var margin = new Logonaut.UI.Helpers.OriginalLineNumberMargin();
            LogOutputEditor.TextArea.LeftMargins.Add(margin); // LogOutputEditor is generated automatically from XAML

            // Set up the necessary bindings in code
            var filteredLinesBinding = new System.Windows.Data.Binding("FilteredLogLines") {
                Source = this.DataContext, // Or find the ViewModel appropriately
                Mode = System.Windows.Data.BindingMode.OneWay
            };
            margin.SetBinding(Logonaut.UI.Helpers.OriginalLineNumberMargin.FilteredLinesSourceProperty, filteredLinesBinding);

            var visibilityBinding = new System.Windows.Data.Binding("IsCustomLineNumberMarginVisible") {
                Source = this.DataContext,
                Mode = System.Windows.Data.BindingMode.OneWay
                // You might need a converter if IsCustomLineNumberMarginVisible is not a Visibility type
            };

            margin.SetBinding(UIElement.VisibilityProperty, visibilityBinding);

            var lineSeparatorMargin = new Logonaut.UI.Helpers.VerticalLineMargin();
            // Bind its visibility to the same property as the number margin
            lineSeparatorMargin.SetBinding(UIElement.VisibilityProperty, visibilityBinding); // Re-use the same binding object
            // Add it *after* the number margin
            LogOutputEditor.TextArea.LeftMargins.Add(lineSeparatorMargin);
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                SetDarkTitleBar(hwnd); // Apply dark title bar if applicable
            }
            else {
                // TODO: Better error handling
                System.Diagnostics.Debug.WriteLine("Could not get window handle in SourceInitialized.");
            }
        }

        // Update SelectedFilter in ViewModel when TreeView selection changes
        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                // The TreeView's SelectedItem is the data item (FilterViewModel)
                viewModel.SelectedFilter = e.NewValue as FilterViewModel;
            }
        }

        // Call ViewModel cleanup method when the window is closing
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.Cleanup();
            }
        }

        // Simple Exit menu item handler
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Close(); // Trigger the Window_Closing event
        }
    }
}