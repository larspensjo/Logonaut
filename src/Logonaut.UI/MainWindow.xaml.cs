using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls; // Required for TextBox
using System.Windows.Media;
using System.Windows.Input; 
using System.Windows.Documents; 
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.UI.Helpers;
using Logonaut.UI.ViewModels;
using System.Diagnostics; 
using Logonaut.Common;
using Logonaut.Core;
using System.Windows.Forms; // Required for Screen

namespace Logonaut.UI;

/*
 * Code-behind for the main application window.
 * Handles window lifecycle events, interop for custom title bar theming,
 * integration with AvalonEdit, and forwarding UI events/data to the MainViewModel.
 * It also manages loading and applying initial window geometry and ensuring settings are saved on exit.
 */
public partial class MainWindow : Window, IDisposable
{
    public static readonly RoutedUICommand ToggleSimulatorConfigCommand = new RoutedUICommand(
        "Toggle Simulator Configuration Panel", "ToggleSimulatorConfigCommand", typeof(MainWindow)
    );

    private void ToggleSimulatorConfig_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        // Can always execute if the ViewModel exists
        e.CanExecute = DataContext is MainViewModel;
    }

    private void ToggleSimulatorConfig_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsSimulatorConfigurationVisible = !viewModel.IsSimulatorConfigurationVisible;
        }
    }

    // Define a command for focusing the Go To Line box
    public static readonly RoutedUICommand GoToLineBoxFocusCommand = new RoutedUICommand(
        "Focus Go To Line Box", "GoToLineBoxFocusCommand", typeof(MainWindow)
    );

    // --- Command Handlers for Go To Line Focus ---
    private void GoToLineBoxFocus_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        // We can always try to focus the box if it exists
        e.CanExecute = true;
    }

    private void GoToLineBoxFocus_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        // Access the TextBox using its x:Name and set focus
        JumpToLineTextBox.Focus();
        JumpToLineTextBox.SelectAll(); // Select existing text for easy replacement
    }

    // Define a command for focusing the Search box
    public static readonly RoutedUICommand FocusSearchCommand = new RoutedUICommand(
        "Focus Search Box", "FocusSearchCommand", typeof(MainWindow)
    );

    // Command Handlers for Search Focus
    private void FocusSearch_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        // Can always execute if the search box exists (which it should)
        e.CanExecute = true;
    }

    private void FocusSearch_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll(); // Select existing text
    }

    // --- Dark Title Bar Support ---
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private static bool IsWindows10OrGreater(int build = -1)
    {
        try
        {
            return Environment.OSVersion.Version.Major >= 10 &&
                    (build == -1 || Environment.OSVersion.Version.Build >= build);
        }
        catch
        {
            return false; // Avoid potential exceptions
        }
    }

    private readonly MainViewModel _viewModel;
    private readonly TextEditor _logOutputEditor; // Just for convenience. Referencing LogOutputEditor triggers an Intellisense error.
    private bool _disposed;

    private bool _isProgrammaticallySettingGeometry = false;
    private bool _windowFullyLoaded = false;

    public MainWindow(MainViewModel viewModel, ISettingsService settingsService)
    {
        InitializeComponent();
        _logOutputEditor = LogOutputEditor;
        DataContext = viewModel;
        _viewModel = viewModel;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.RequestGlobalScrollToEnd += ViewModel_RequestScrollToEnd;
        _viewModel.RequestGlobalScrollToLineIndex += ViewModel_RequestScrollToLineIndex;

        _logOutputEditor.Loaded += (s, e) =>
        {
            _viewModel.SetLogEditorInstance(_logOutputEditor);
            ApplyInitialFontSettingsToEditor();
        };

        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        SetupCustomMargins();

        _logOutputEditor.TextArea.PreviewKeyDown += LogOutputEditor_PreviewKeyDown;
        _logOutputEditor.TextArea.PreviewMouseDown += LogOutputEditor_PreviewMouseDown;

        Closing += MainWindow_Closing; // Simplified closing event
    }

    /*
     * Loads the persisted window geometry (position and size) and applies it.
     * This method is called once when the window is first initialized.
     * It includes validation to ensure the window appears on a visible screen area.
     */
    private void LoadAndApplyWindowGeometry()
    {
        _isProgrammaticallySettingGeometry = true; // Prevent OnLocationChanged/OnRenderSizeChanged from marking dirty
        Debug.WriteLine("LoadAndApplyWindowGeometry: Setting _isProgrammaticallySettingGeometry=true.");
        try
        {
            // ViewModel now holds the persisted geometry values after LoadPersistedSettings.
            // We read from ViewModel instead of loading settings directly here.
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

            this.WindowState = WindowState.Normal; // Always start normal

            if (onScreen && _viewModel.WindowWidth > 100 && _viewModel.WindowHeight > 100)
            {
                this.Top = _viewModel.WindowTop;
                this.Left = _viewModel.WindowLeft;
                this.Width = _viewModel.WindowWidth;
                this.Height = _viewModel.WindowHeight;
                Debug.WriteLine($"MainWindow: Restored Window Geometry from ViewModel - L:{Left}, T:{Top}, W:{Width}, H:{Height}");
            }
            else
            {
                Debug.WriteLine("MainWindow: ViewModel's window position/size is off-screen or invalid. Using defaults or centering.");
                this.Width = _viewModel.WindowWidth > 100 ? _viewModel.WindowWidth : 1000;
                this.Height = _viewModel.WindowHeight > 100 ? _viewModel.WindowHeight : 700;
                if (!onScreen) this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading/applying window geometry from ViewModel: {ex.Message}");
        }
        finally
        {
            _isProgrammaticallySettingGeometry = false;
            Debug.WriteLine("LoadAndApplyWindowGeometry: Setting _isProgrammaticallySettingGeometry=false.");
        }
    }

    /*
     * Updates the MainViewModel with the current window geometry and marks settings as dirty.
     * This is called on window move, resize, or when the splitter position changes.
     * It ensures that the latest geometry is captured for persistence when the application eventually saves settings.
     */
    private void UpdateViewModelWithCurrentGeometryAndMarkDirty()
    {
        if (_isProgrammaticallySettingGeometry || !_windowFullyLoaded || _viewModel == null) return;

        // Update ViewModel properties with current geometry
        if (this.WindowState == WindowState.Normal)
        {
            _viewModel.WindowTop = this.Top;
            _viewModel.WindowLeft = this.Left;
            _viewModel.WindowHeight = this.Height;
            _viewModel.WindowWidth = this.Width;
        }
        else if (this.RestoreBounds != Rect.Empty && this.RestoreBounds.Width > 0 && this.RestoreBounds.Height > 0)
        {
            // If maximized or minimized, use RestoreBounds for the "normal" state geometry
            _viewModel.WindowTop = this.RestoreBounds.Top;
            _viewModel.WindowLeft = this.RestoreBounds.Left;
            _viewModel.WindowHeight = this.RestoreBounds.Height;
            _viewModel.WindowWidth = this.RestoreBounds.Width;
        }
        // If RestoreBounds is empty (e.g., started maximized and never normalized),
        // the ViewModel retains its previously loaded/set "normal" geometry values.


        if (MainContentColumnsGrid != null && MainContentColumnsGrid.ColumnDefinitions.Count > 0)
        {
            double currentFilterPanelWidth = MainContentColumnsGrid.ColumnDefinitions[0].ActualWidth;
            if (!double.IsNaN(currentFilterPanelWidth) &&
                !double.IsInfinity(currentFilterPanelWidth) &&
                 currentFilterPanelWidth > 0)
            {
                if (Math.Abs(_viewModel.FilterPanelWidth - currentFilterPanelWidth) > 1e-6) // Only update if changed
                {
                    _viewModel.FilterPanelWidth = currentFilterPanelWidth;
                    Debug.WriteLine($"MainWindow: Updated ViewModel.FilterPanelWidth to {currentFilterPanelWidth}");
                }
            }
            else
            {
                Debug.WriteLine($"MainWindow: Skipped updating ViewModel.FilterPanelWidth due to invalid ActualWidth ({currentFilterPanelWidth}).");
            }
        }
        _viewModel.MarkSettingsAsDirty(); // Inform ViewModel that settings (geometry part) have changed
    }

    // Event handlers for updating ViewModel with geometry
    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (_windowFullyLoaded && WindowState == WindowState.Normal)
        {
            UpdateViewModelWithCurrentGeometryAndMarkDirty();
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_windowFullyLoaded && WindowState == WindowState.Normal)
        {
            UpdateViewModelWithCurrentGeometryAndMarkDirty();
        }
        // Also update if the window is NOT normal, but the RestoreBounds have changed (e.g. after maximizing then changing size of monitor)
        // This ensures RestoreBounds are correctly captured if user closes while maximized.
        // However, this check needs to be careful not to trigger excessively.
        // The current logic in UpdateViewModelWithCurrentGeometryAndMarkDirty handles RestoreBounds.
        if (_windowFullyLoaded && WindowState != WindowState.Normal)
        {
            UpdateViewModelWithCurrentGeometryAndMarkDirty();
        }


    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // If the window state changes (e.g., maximized, minimized, then normalized),
        // capture the geometry.
        if (_windowFullyLoaded)
        {
            UpdateViewModelWithCurrentGeometryAndMarkDirty();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_windowFullyLoaded)
        {
            // Ensure ViewModel has the absolute latest geometry before it saves.
            UpdateViewModelWithCurrentGeometryAndMarkDirty();
        }
        // ViewModel's Cleanup method will handle saving settings if they are dirty.
        // _viewModel.Cleanup() is called from Dispose().
        Dispose();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            LogOutputEditor_Unloaded(null, null);

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _viewModel.RequestGlobalScrollToEnd -= ViewModel_RequestScrollToEnd;
                _viewModel.RequestGlobalScrollToLineIndex -= ViewModel_RequestScrollToLineIndex;
                _viewModel.Cleanup();
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private void ApplyInitialFontSettingsToEditor()
    {
        if (_logOutputEditor?.TextArea != null && _viewModel != null)
        {
            if (!string.IsNullOrEmpty(_viewModel.EditorFontFamilyName))
            {
                _logOutputEditor.TextArea.FontFamily = new FontFamily(_viewModel.EditorFontFamilyName);
            }
            _logOutputEditor.TextArea.FontSize = _viewModel.EditorFontSize;

            if (_logOutputEditor.TextArea.LeftMargins != null)
            {
                foreach (var margin in _logOutputEditor.TextArea.LeftMargins.OfType<OriginalLineNumberMargin>())
                {
                    margin.RefreshFontProperties();
                }
            }
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _overviewRuler = FindVisualChild<Logonaut.UI.Helpers.OverviewRulerMargin>(_logOutputEditor);
        if (_overviewRuler is null)
            throw new InvalidOperationException("OverviewRulerMargin not found in TextEditor template.");
        _overviewRuler.RequestScrollOffset += OverviewRuler_RequestScrollOffset;
        _overviewRuler.PreviewMouseLeftButtonDown += OverviewRuler_PreviewMouseLeftButtonDown;

        TextView textView = _logOutputEditor.TextArea.TextView;
        if (textView == null)
            throw new InvalidOperationException("TextView not found within LogOutputEditor.");

        _selectedIndexTransformer = new SelectedIndexHighlightTransformer();
        textView.SetResourceReference(TextView.TagProperty, "PersistedHighlightBrush");
        _selectedIndexTransformer.HighlightBrush = textView.Tag as Brush;
        _selectedIndexTransformer.FilteredLinesSource = _viewModel.FilteredLogLines;
        textView.LineTransformers.Add(_selectedIndexTransformer);

        _chunkSeparator = new ChunkSeparatorRenderer(textView);
        textView.SetResourceReference(TextView.ToolTipProperty, "ChunkSeparatorBrush"); // Temporarily use ToolTip to pass brush resource key
        Brush? separatorBrush = textView.ToolTip as Brush; // Retrieve the brush resolved from the key
        _chunkSeparator.SeparatorBrush = separatorBrush ?? Brushes.Gray; // Assign to DP
        textView.ClearValue(TextView.ToolTipProperty); // Clear the temporary use of ToolTip
        textView.BackgroundRenderers.Add(_chunkSeparator);
        _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, _viewModel.ContextLines);


        _logOutputEditor.Unloaded += LogOutputEditor_Unloaded;

        _logOutputEditor.TextArea.PreviewMouseWheel += TextArea_PreviewMouseWheel;
        _logOutputEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        _lastKnownCaretLine = _logOutputEditor.TextArea.Caret.Line;
        _logOutputEditor.TextArea.SelectionChanged += LogOutputEditor_SelectionChanged;

        _windowFullyLoaded = true;
        ApplySplitterPosition(); // Apply splitter position after window is fully loaded and measured
    }

    /*
     * Applies the persisted splitter position. Called from MainWindow_Loaded.
     * Reads the FilterPanelWidth from the MainViewModel (which would have loaded it from settings).
     */
    private void ApplySplitterPosition()
    {
        _isProgrammaticallySettingGeometry = true;
        Debug.WriteLine($"ApplySplitterPosition: Setting _isProgrammaticallySettingGeometry=true.");
        try
        {
            if (MainContentColumnsGrid != null && MainContentColumnsGrid.ColumnDefinitions.Count > 0 && _viewModel != null)
            {
                double filterPanelWidth = _viewModel.FilterPanelWidth; // Get from ViewModel

                double currentWindowWidth = this.ActualWidth;
                if (currentWindowWidth <= 0) currentWindowWidth = _viewModel.WindowWidth;

                if (filterPanelWidth < 50) filterPanelWidth = 50;
                double maxAvailableWidth = currentWindowWidth - 150;
                if (maxAvailableWidth < 50) maxAvailableWidth = 50;

                filterPanelWidth = Math.Min(filterPanelWidth, maxAvailableWidth);

                MainContentColumnsGrid.ColumnDefinitions[0].Width = new GridLength(filterPanelWidth, GridUnitType.Pixel);
                Debug.WriteLine($"MainWindow: Applied FilterPanelWidth {filterPanelWidth} from ViewModel (Window ActualWidth: {this.ActualWidth})");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error applying splitter position from ViewModel: {ex.Message}");
        }
        finally
        {
            _isProgrammaticallySettingGeometry = false;
            Debug.WriteLine($"ApplySplitterPosition: Setting _isProgrammaticallySettingGeometry=false.");
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child != null && child is T typedChild)
            {
                return typedChild;
            }
            else
            {
                if (child is null)
                    throw new ArgumentNullException(nameof(child), "Child is null. This should not happen.");
                T? childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
        }
        return null;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        // The window handle is now available
        if (IsWindows10OrGreater())
        {
            EnableDarkTitleBar();
        }
        // ViewModel would have loaded settings in its constructor.
        // Now apply them to the window.
        LoadAndApplyWindowGeometry();
    }

    private void EnableDarkTitleBar()
    {
        var windowHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
            return;

        int useImmersiveDarkMode = 1;
        if (IsWindows10OrGreater(20180)) // Windows 10 20H1 or later
        {
            DwmSetWindowAttribute(windowHandle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
        }
        else
        {
            DwmSetWindowAttribute(windowHandle, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useImmersiveDarkMode, sizeof(int));
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        this.Close(); // This will trigger MainWindow_Closing
    }
}
