using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Logonaut.UI
{
    public partial class MainWindow : Window
    {
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public MainWindow()
        {
            InitializeComponent();
            this.SourceInitialized += MainWindow_SourceInitialized;
            DataContext = new ViewModels.MainViewModel();
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int useImmersiveDarkMode = 1; // 1 to enable dark mode
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
        }
        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is ViewModels.MainViewModel viewModel)
            {
                viewModel.SelectedFilter = e.NewValue as ViewModels.FilterViewModel;
            }
        }
    }
}
