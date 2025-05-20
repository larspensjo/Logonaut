using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
using System.Windows.Forms;

namespace Logonaut.UI;

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

    private readonly ISettingsService _settingsService;
    private bool _isManuallySettingGeometry = false;
    private bool _windowLoaded = false;

    public MainWindow(MainViewModel viewModel, ISettingsService settingsService) // Add ISettingsService parameter
    {
        InitializeComponent();
        _settingsService = settingsService; // Store injected service
        _logOutputEditor = LogOutputEditor;
        DataContext = viewModel;
        _viewModel = viewModel;

        // Subscribe to ViewModel events AFTER _viewModel is assigned
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

        // Original Closing event handler:
        // Closing += Window_Closing; 
        // Replace with the modified one below that also saves geometry:
        Closing += MainWindow_Closing_SaveGeometry; // Renamed to avoid conflict if you still have the old one
    }

    private void LoadAndApplyWindowGeometry()
    {
        _isManuallySettingGeometry = true;
        try
        {
            LogonautSettings settings = _settingsService.LoadSettings();

            // Window will always start Normal, so no need to set WindowState from settings
            this.WindowState = WindowState.Normal; 

            // Validate saved position and size
            Rect savedBounds = new Rect(settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight);
            bool onScreen = false;
            foreach (var screen in Screen.AllScreens)
            {
                var screenWorkingArea = new Rect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
                if (screenWorkingArea.IntersectsWith(savedBounds) || screenWorkingArea.Contains(savedBounds))
                {
                     // Check if a reasonable portion of the window is visible
                    if (savedBounds.Right > screenWorkingArea.Left + 50 &&
                        savedBounds.Left < screenWorkingArea.Right - 50 &&
                        savedBounds.Bottom > screenWorkingArea.Top + 30 && // Check bottom against top + 30 (for title bar)
                        savedBounds.Top < screenWorkingArea.Bottom - 30)
                    {
                        onScreen = true;
                        break;
                    }
                }
            }

            if (onScreen && settings.WindowWidth > 100 && settings.WindowHeight > 100) // Ensure dimensions are sensible
            {
                this.Top = settings.WindowTop;
                this.Left = settings.WindowLeft;
                this.Width = settings.WindowWidth;
                this.Height = settings.WindowHeight;
                Debug.WriteLine($"MainWindow: Restored Window Geometry - L:{Left}, T:{Top}, W:{Width}, H:{Height}");
            }
            else
            {
                Debug.WriteLine("MainWindow: Saved window position/size is off-screen or invalid. Using defaults or centering.");
                // Allow WPF default placement or center if desired (WPF does this by default if not set)
                // Setting default size if saved ones were problematic
                this.Width = settings.WindowWidth > 100 ? settings.WindowWidth : 1000;
                this.Height = settings.WindowHeight > 100 ? settings.WindowHeight : 700;
                // Forcing centering if it was off-screen:
                if (!onScreen) this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading/applying window geometry: {ex.Message}");
        }
        finally
        {
            _isManuallySettingGeometry = false;
        }
    }

    /*
     * Saves the current window geometry (size, position, state) and the grid splitter position
     * to the application settings. This is called on window move, resize, state change, or closing.
     * It uses RestoreBounds for maximized/minimized states to save the normal-state geometry.
     */
    private void SaveWindowGeometry()
    {
        if (_isManuallySettingGeometry || !_windowLoaded || _settingsService == null) return;

        LogonautSettings settings = _settingsService.LoadSettings();

        // Always save the Normal bounds, regardless of current state, as per your requirement
        // RestoreBounds is the key if maximized/minimized
        if (this.WindowState == WindowState.Maximized || this.WindowState == WindowState.Minimized)
        {
             if (this.RestoreBounds != Rect.Empty && this.RestoreBounds.Width > 0 && this.RestoreBounds.Height > 0)
             {
                settings.WindowTop = this.RestoreBounds.Top;
                settings.WindowLeft = this.RestoreBounds.Left;
                settings.WindowHeight = this.RestoreBounds.Height;
                settings.WindowWidth = this.RestoreBounds.Width;
             }
             // If RestoreBounds is empty (e.g. started maximized), we don't update these,
             // keeping previously saved normal bounds.
        }
        else // WindowState.Normal
        {
            settings.WindowTop = this.Top;
            settings.WindowLeft = this.Left;
            settings.WindowHeight = this.Height;
            settings.WindowWidth = this.Width;
        }
        // settings.MainWindowState = this.WindowState; // REMOVE - Not saving WindowState

        if (MainContentColumnsGrid != null && MainContentColumnsGrid.ColumnDefinitions.Count > 0)
        {
            if (!double.IsNaN(MainContentColumnsGrid.ColumnDefinitions[0].ActualWidth) &&
                !double.IsInfinity(MainContentColumnsGrid.ColumnDefinitions[0].ActualWidth) &&
                 MainContentColumnsGrid.ColumnDefinitions[0].ActualWidth > 0)
            {
                settings.FilterPanelWidth = MainContentColumnsGrid.ColumnDefinitions[0].ActualWidth;
                Debug.WriteLine($"MainWindow: Saving FilterPanelWidth as {settings.FilterPanelWidth}");
            }
            else
            {
                 Debug.WriteLine($"MainWindow: Skipped saving FilterPanelWidth due to invalid ActualWidth ({MainContentColumnsGrid.ColumnDefinitions[0].ActualWidth}).");
            }
        }

        _settingsService.SaveSettings(settings);
    }

    // Event handlers for saving geometry
    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (_windowLoaded && WindowState == WindowState.Normal) // Only save if loaded and normal
        {
            SaveWindowGeometry();
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_windowLoaded && WindowState == WindowState.Normal) // Only save if loaded and normal
        {
            // This also saves splitter implicitly if column 0 width is driven by it changing its ActualWidth
            SaveWindowGeometry();
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // WindowState is not saved, but if it becomes Normal, subsequent moves/resizes will save.
        // If you wanted to save the *fact* that it was maximized to restore maximized,
        // you'd save WindowState here. But since you don't, this doesn't need to call SaveWindowGeometry directly.
        // However, if you manually restore from maximized and then close, the RestoreBounds
        // would have been captured by SaveWindowGeometry().
    }

    // Modified closing event to include saving geometry
    private void MainWindow_Closing_SaveGeometry(object? sender, CancelEventArgs e)
    {
        if(_windowLoaded) // Ensure save only if window was fully loaded
        {
          SaveWindowGeometry();
        }
        Dispose();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // Ensure editor unload cleanup runs if window closes before unload fires
            LogOutputEditor_Unloaded(null, null);

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _viewModel.RequestGlobalScrollToEnd -= ViewModel_RequestScrollToEnd;
                _viewModel.RequestGlobalScrollToLineIndex -= ViewModel_RequestScrollToLineIndex;
                _viewModel.Cleanup(); // ViewModel cleanup (saves settings internally)
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private void ApplyInitialFontSettingsToEditor()
    {
        if (_logOutputEditor?.TextArea != null && _viewModel != null)
        {
            // Apply FontFamily
            if (!string.IsNullOrEmpty(_viewModel.EditorFontFamilyName))
            {
                _logOutputEditor.TextArea.FontFamily = new FontFamily(_viewModel.EditorFontFamilyName);
                Debug.WriteLine($"MainWindow (Initial Apply): TextArea.FontFamily explicitly set to '{_viewModel.EditorFontFamilyName}'.");
            }

            // Apply FontSize
            _logOutputEditor.TextArea.FontSize = _viewModel.EditorFontSize;
            Debug.WriteLine($"MainWindow (Initial Apply): TextArea.FontSize explicitly set to '{_viewModel.EditorFontSize}'.");

            // Also refresh custom margins as their initial setup might have used default font metrics
            // if they were initialized before the editor's font was fully set from persisted settings.
            if (_logOutputEditor.TextArea.LeftMargins != null)
            {
                foreach (var margin in _logOutputEditor.TextArea.LeftMargins.OfType<OriginalLineNumberMargin>())
                {
                    margin.RefreshFontProperties();
                    Debug.WriteLine($"MainWindow (Initial Apply): Called RefreshFontProperties on OriginalLineNumberMargin.");
                }
            }
        }
        else
        {
            Debug.WriteLine("MainWindow (Initial Apply): Could not apply initial font settings - editor, TextArea, or ViewModel is null.");
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
        textView.SetResourceReference(TextView.ToolTipProperty, "ChunkSeparatorBrush");
        Brush? separatorBrush = textView.ToolTip as Brush;
        _chunkSeparator.SeparatorBrush = separatorBrush ?? Brushes.Gray;
        textView.ClearValue(TextView.ToolTipProperty);
        textView.BackgroundRenderers.Add(_chunkSeparator);
        _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, _viewModel.ContextLines);

        _logOutputEditor.Unloaded += LogOutputEditor_Unloaded;

        _logOutputEditor.TextArea.PreviewMouseWheel += TextArea_PreviewMouseWheel;
        _logOutputEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        _lastKnownCaretLine = _logOutputEditor.TextArea.Caret.Line;
        _logOutputEditor.TextArea.SelectionChanged += LogOutputEditor_SelectionChanged;

        // If LoadAndApplyWindowGeometry was not called in SourceInitialized or needs re-evaluation
        // after full layout, it could be called here.
        // For now, assuming SourceInitialized is sufficient for Top/Left/Width/Height.
        // Splitter might be better here if its calculations need ActualWidth.
        // Let's refine LoadAndApplyWindowGeometry to be smarter.

        _windowLoaded = true; // Set flag indicating window is fully loaded
        // Apply splitter here if it depends on fully resolved ActualWidth values
        ApplySplitterPosition();
    }

    /*
     * Applies the persisted splitter position. Called from MainWindow_Loaded,
     * as it might rely on ActualWidth of the window.
     */
    private void ApplySplitterPosition()
    {
        _isManuallySettingGeometry = true; // Still relevant to prevent immediate save if column width change triggers event
        try
        {
            LogonautSettings settings = _settingsService.LoadSettings(); // Could get from a field if already loaded
            if (MainContentColumnsGrid != null && MainContentColumnsGrid.ColumnDefinitions.Count > 0)
            {
                double filterPanelWidth = settings.FilterPanelWidth;

                // Validate against current window width
                double currentWindowWidth = this.ActualWidth; // Use ActualWidth now as window is loaded
                if (currentWindowWidth <= 0) currentWindowWidth = settings.WindowWidth; // Fallback if ActualWidth not ready

                if (filterPanelWidth < 50) filterPanelWidth = 50;
                double maxAvailableWidth = currentWindowWidth - 150; // Ensure editor has at least 150px
                if (maxAvailableWidth < 50) maxAvailableWidth = 50; // Minimum sensible for filter panel

                filterPanelWidth = Math.Min(filterPanelWidth, maxAvailableWidth);

                MainContentColumnsGrid.ColumnDefinitions[0].Width = new GridLength(filterPanelWidth, GridUnitType.Pixel);
                Debug.WriteLine($"MainWindow: Applied FilterPanelWidth to {filterPanelWidth} (Window ActualWidth: {this.ActualWidth})");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error applying splitter position: {ex.Message}");
        }
        finally
        {
            _isManuallySettingGeometry = false;
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
        this.Close();
    }
}
