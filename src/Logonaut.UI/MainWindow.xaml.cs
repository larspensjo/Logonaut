using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using Logonaut.Common;
using System.Windows.Forms; // Required for Screen
using Logonaut.UI.ViewModels; // Ensure this is present
using System; // For EventArgs, etc.

namespace Logonaut.UI;

/*
 * Code-behind for the main application window.
 * This class is now primarily responsible for window-level concerns, such as:
 *  - Window lifecycle events (Loaded, Closing, etc.).
 *  - Loading and saving window geometry (position, size, state).
 *  - Handling top-level commands that are not specific to a tab's content.
 *  - Interop for custom OS features like the dark title bar.
 *
 * All logic related to the log content display has been moved into the TabView user control.
 */
public partial class MainWindow : Window, IDisposable
{
    public static readonly RoutedUICommand ToggleSimulatorConfigCommand = new RoutedUICommand(
        "Toggle Simulator Configuration Panel", "ToggleSimulatorConfigCommand", typeof(MainWindow)
    );

    public static readonly RoutedUICommand GoToLineBoxFocusCommand = new RoutedUICommand(
        "Focus Go To Line Box", "GoToLineBoxFocusCommand", typeof(MainWindow)
    );

    private readonly MainViewModel _viewModel;
    private bool _disposed;
    private bool _isProgrammaticallySettingGeometry = false;
    private bool _windowFullyLoaded = false;

    // --- Dark Title Bar Support ---
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _windowFullyLoaded = true;
        ApplySplitterPosition();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        EnableDarkTitleBar();
        LoadAndApplyWindowGeometry();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_windowFullyLoaded)
        {
            UpdateViewModelWithCurrentGeometryAndMarkDirty();
        }
        Dispose();
    }

    #region Window Geometry Management

    private void LoadAndApplyWindowGeometry()
    {
        _isProgrammaticallySettingGeometry = true;
        try
        {
            Rect savedBounds = new Rect(_viewModel.WindowLeft, _viewModel.WindowTop, _viewModel.WindowWidth, _viewModel.WindowHeight);
            bool onScreen = false;
            foreach (var screen in Screen.AllScreens)
            {
                var screenWorkingArea = new Rect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
                if (screenWorkingArea.IntersectsWith(savedBounds) || screenWorkingArea.Contains(savedBounds))
                {
                    if (savedBounds.Right > screenWorkingArea.Left + 50 &&
                        savedBounds.Left < screenWorkingArea.Right - 50 &&
                        savedBounds.Bottom > screenWorkingArea.Top + 30 &&
                        savedBounds.Top < screenWorkingArea.Bottom - 30)
                    {
                        onScreen = true;
                        break;
                    }
                }
            }

            this.WindowState = _viewModel.WindowState switch
            {
                AppWindowState.Maximized => WindowState.Maximized,
                AppWindowState.Minimized => WindowState.Minimized,
                _ => WindowState.Normal,
            };

            if (_viewModel.WindowState == AppWindowState.Normal)
            {
                if (onScreen && _viewModel.WindowWidth > 100 && _viewModel.WindowHeight > 100)
                {
                    this.Top = _viewModel.WindowTop;
                    this.Left = _viewModel.WindowLeft;
                    this.Width = _viewModel.WindowWidth;
                    this.Height = _viewModel.WindowHeight;
                }
                else
                {
                    this.Width = _viewModel.WindowWidth > 100 ? _viewModel.WindowWidth : 1000;
                    this.Height = _viewModel.WindowHeight > 100 ? _viewModel.WindowHeight : 700;
                    if (!onScreen) this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading/applying window geometry from ViewModel: {ex.Message}");
        }
        finally
        {
            _isProgrammaticallySettingGeometry = false;
        }
    }

    private void UpdateViewModelWithCurrentGeometryAndMarkDirty()
    {
        if (_isProgrammaticallySettingGeometry || !_windowFullyLoaded || _viewModel == null) return;

        if (this.WindowState == WindowState.Normal)
        {
            _viewModel.WindowTop = this.Top;
            _viewModel.WindowLeft = this.Left;
            _viewModel.WindowHeight = this.Height;
            _viewModel.WindowWidth = this.Width;
        }
        else if (this.RestoreBounds != Rect.Empty && this.RestoreBounds.Width > 0 && this.RestoreBounds.Height > 0)
        {
            _viewModel.WindowTop = this.RestoreBounds.Top;
            _viewModel.WindowLeft = this.RestoreBounds.Left;
            _viewModel.WindowHeight = this.RestoreBounds.Height;
            _viewModel.WindowWidth = this.RestoreBounds.Width;
        }

        _viewModel.WindowState = this.WindowState switch
        {
            WindowState.Maximized => AppWindowState.Maximized,
            WindowState.Minimized => AppWindowState.Minimized,
            _ => AppWindowState.Normal,
        };

        if (MainContentColumnsGrid != null && MainContentColumnsGrid.ColumnDefinitions.Count > 0)
        {
            double currentFilterPanelWidth = MainContentColumnsGrid.ColumnDefinitions[0].ActualWidth;
            if (currentFilterPanelWidth > 0 && Math.Abs(_viewModel.FilterPanelWidth - currentFilterPanelWidth) > 1e-6)
            {
                _viewModel.FilterPanelWidth = currentFilterPanelWidth;
            }
        }
        _viewModel.MarkSettingsAsDirty();
    }

    private void ApplySplitterPosition()
    {
        _isProgrammaticallySettingGeometry = true;
        try
        {
            if (MainContentColumnsGrid?.ColumnDefinitions.Count > 0 && _viewModel != null)
            {
                double filterPanelWidth = _viewModel.FilterPanelWidth;
                double maxAvailableWidth = this.ActualWidth - 150;
                filterPanelWidth = Math.Clamp(filterPanelWidth, 50, maxAvailableWidth > 50 ? maxAvailableWidth : 50);
                MainContentColumnsGrid.ColumnDefinitions[0].Width = new GridLength(filterPanelWidth, GridUnitType.Pixel);
            }
        }
        finally
        {
            _isProgrammaticallySettingGeometry = false;
        }
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (_windowFullyLoaded && WindowState == WindowState.Normal) UpdateViewModelWithCurrentGeometryAndMarkDirty();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_windowFullyLoaded) UpdateViewModelWithCurrentGeometryAndMarkDirty();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (_windowFullyLoaded) UpdateViewModelWithCurrentGeometryAndMarkDirty();
    }

    #endregion

    #region Commands and UI Events

    private void ToggleSimulatorConfig_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = DataContext is MainViewModel;
    private void ToggleSimulatorConfig_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsSimulatorConfigurationVisible = !viewModel.IsSimulatorConfigurationVisible;
        }
    }

    private void GoToLineBoxFocus_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;
    private void GoToLineBoxFocus_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        JumpToLineTextBox.Focus();
        JumpToLineTextBox.SelectAll();
    }

    private void Paste_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (_viewModel?.PasteCommand != null)
        {
            e.CanExecute = _viewModel.PasteCommand.CanExecute(null);
        }
        else
        {
            e.CanExecute = false;
        }
    }

    private void Paste_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel?.PasteCommand?.Execute(null);
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => this.Close();

    private void EnableDarkTitleBar()
    {
        var windowHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero) return;
        int useImmersiveDarkMode = 1;
        DwmSetWindowAttribute(windowHandle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
    }

    #endregion

    public void Dispose()
    {
        if (!_disposed)
        {
            _viewModel?.Cleanup();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
