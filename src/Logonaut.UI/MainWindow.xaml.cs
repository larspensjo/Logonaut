using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input; // Required for RoutedUICommand
using System.Windows.Documents; // Required for AdornerLayer
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.UI.Helpers;
using Logonaut.UI.ViewModels;
using System.Diagnostics; // For Debug

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

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _logOutputEditor = LogOutputEditor;
        DataContext = viewModel;
        _viewModel = viewModel;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.RequestGlobalScrollToEnd += ViewModel_RequestScrollToEnd;
        _viewModel.RequestGlobalScrollToLineIndex += ViewModel_RequestScrollToLineIndex;

        // Apply dark title bar if supported
        if (IsWindows10OrGreater())
        {
            EnableDarkTitleBar();
        }

        // --- Pass editor instance to ViewModel AFTER it's loaded ---
        _logOutputEditor.Loaded += (s, e) =>
        {
            _viewModel.SetLogEditorInstance(_logOutputEditor);
        };

        // Set up initial window state
        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;

        // Add original line number and separator margins (code-behind approach)
        SetupCustomMargins();

        _logOutputEditor.Loaded += LogOutputEditor_Loaded;
        _logOutputEditor.TextArea.PreviewKeyDown += LogOutputEditor_PreviewKeyDown;
        _logOutputEditor.TextArea.PreviewMouseDown += LogOutputEditor_PreviewMouseDown;

        Closing += MainWindow_Closing;
    }


    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
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
                _viewModel.Cleanup();
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // --- Overview Ruler Setup ---
        _overviewRuler = FindVisualChild<Logonaut.UI.Helpers.OverviewRulerMargin>(_logOutputEditor);
        if (_overviewRuler is null)
            throw new InvalidOperationException("OverviewRulerMargin not found in TextEditor template.");
        _overviewRuler.RequestScrollOffset += OverviewRuler_RequestScrollOffset;
        _overviewRuler.PreviewMouseLeftButtonDown += OverviewRuler_PreviewMouseLeftButtonDown;

        // Get TextView *once*
        TextView textView = _logOutputEditor.TextArea.TextView;
        if (textView == null)
            throw new InvalidOperationException("TextView not found within LogOutputEditor.");

        _selectedIndexTransformer = new SelectedIndexHighlightTransformer();
        // Get the initial brush from the resource dictionary via the Tag proxy
        textView.SetResourceReference(TextView.TagProperty, "PersistedHighlightBrush");
        _selectedIndexTransformer.HighlightBrush = textView.Tag as Brush;
        _selectedIndexTransformer.FilteredLinesSource = _viewModel.FilteredLogLines;
        textView.LineTransformers.Add(_selectedIndexTransformer);

        // --- Chunk Separator Setup ---
        _chunkSeparator = new ChunkSeparatorRenderer(textView);
        textView.SetResourceReference(TextView.ToolTipProperty, "ChunkSeparatorBrush");
        Brush? separatorBrush = textView.ToolTip as Brush;
        _chunkSeparator.SeparatorBrush = separatorBrush ?? Brushes.Gray;
        textView.ClearValue(TextView.ToolTipProperty);
        textView.BackgroundRenderers.Add(_chunkSeparator);
        _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, _viewModel.ContextLines);

        _logOutputEditor.Unloaded += LogOutputEditor_Unloaded;

        _logOutputEditor.TextArea.PreviewMouseWheel += TextArea_PreviewMouseWheel;
        _logOutputEditor.TextArea.PreviewKeyDown += TextArea_PreviewKeyDown;

        _logOutputEditor.TextArea.Caret.PositionChanged += Caret_PositionChanged;
        _lastKnownCaretLine = _logOutputEditor.TextArea.Caret.Line;

        _logOutputEditor.TextArea.SelectionChanged += LogOutputEditor_SelectionChanged;
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

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Cleanup();
        }
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
