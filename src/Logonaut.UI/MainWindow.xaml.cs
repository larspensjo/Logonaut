using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Logonaut.UI.Helpers;
using Logonaut.UI.ViewModels;

namespace Logonaut.UI
{
    public partial class MainWindow : Window, IDisposable
    {
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
        private Logonaut.UI.Helpers.OverviewRulerMargin? _overviewRuler;
        private ChunkSeparatorRenderer? _chunkSeparator;
        private bool _disposed;

        public MainWindow()
        {
            // InitializeComponent() is the method generated from the XAML. When it runs, it parses the XAML, creates the UI elements, and wires them up.
            InitializeComponent();
            DataContext = new ViewModels.MainViewModel();
            _viewModel = (MainViewModel)DataContext;

            // Subscribe to model updates to update chunk separators
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            // Apply dark title bar if supported
            if (IsWindows10OrGreater())
            {
                EnableDarkTitleBar();
            }

            // Set up initial window state
            Loaded += MainWindow_Loaded;

            this.SourceInitialized += MainWindow_SourceInitialized;

            // Add original line number and separator margins (code-behind approach)
            SetupCustomMargins();

            // Hook up event handlers AFTER the template is applied
            LogOutputEditor.Loaded += LogOutputEditor_Loaded;
            
            // Enable clipboard paste functionality
            LogOutputEditor.TextArea.PreviewKeyDown += LogOutputEditor_PreviewKeyDown;

            // Handle mouse clicks for search reference point
            LogOutputEditor.TextArea.MouseDown += LogOutputEditor_MouseDown;

            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_chunkSeparator != null)
                {
                    LogOutputEditor.TextArea.TextView.BackgroundRenderers.Remove(_chunkSeparator);
                    _chunkSeparator.Dispose();
                    _chunkSeparator = null;
                }

                if (_overviewRuler != null)
                {
                    _overviewRuler.RequestScrollOffset -= OverviewRuler_RequestScrollOffset;
                    _overviewRuler = null;
                }

                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                }

                _disposed = true;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_chunkSeparator != null && (e.PropertyName == nameof(MainViewModel.FilteredLogLines) || e.PropertyName == nameof(MainViewModel.ContextLines)))
            {
                _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, _viewModel.ContextLines);
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // The template should be applied now, try to find the ruler
            // Use VisualTreeHelper to find the element within the template
            _overviewRuler = FindVisualChild<Logonaut.UI.Helpers.OverviewRulerMargin>(LogOutputEditor);

            if (_overviewRuler != null)
            {
                // Hook up the event handler
                _overviewRuler.RequestScrollOffset += OverviewRuler_RequestScrollOffset;
            }
            else
            {
                // TODO: Log or handle the case where the ruler wasn't found
                System.Diagnostics.Debug.WriteLine("OverviewRulerMargin not found in TextEditor template.");
            }

            // Unsubscribe when the editor unloads to prevent memory leaks
            LogOutputEditor.Unloaded += (s, ev) => {
                if (_overviewRuler != null)
                {
                    _overviewRuler.RequestScrollOffset -= OverviewRuler_RequestScrollOffset;
                }
                // Also unsubscribe from Loaded/Unloaded? Might not be necessary if window closes.
            };

            // Initialize chunk separator
            _chunkSeparator = new ChunkSeparatorRenderer(LogOutputEditor.TextArea.TextView);
            LogOutputEditor.TextArea.TextView.BackgroundRenderers.Add(_chunkSeparator);
            _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, _viewModel.ContextLines);

            // Clean up when editor unloads
            LogOutputEditor.Unloaded += (s, ev) => {
                if (_overviewRuler != null)
                {
                    _overviewRuler.RequestScrollOffset -= OverviewRuler_RequestScrollOffset;
                }
                if (_chunkSeparator != null)
                {
                    LogOutputEditor.TextArea.TextView.BackgroundRenderers.Remove(_chunkSeparator);
                }
            };
        }

        private void LogOutputEditor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"PreviewKey pressed: {e.Key}, Modifiers: {System.Windows.Input.Keyboard.Modifiers}");
            if (e.Key == System.Windows.Input.Key.V && 
                (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control &&
                !(System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)))
            {
                if (System.Windows.Clipboard.ContainsText())
                {
                    string clipboardText = System.Windows.Clipboard.GetText();
                    if (!string.IsNullOrEmpty(clipboardText))
                    {
                        // Get the MainViewModel
                        if (DataContext is ViewModels.MainViewModel viewModel)
                        {
                            // Load the clipboard text as if it were a file
                            viewModel.LoadLogFromText(clipboardText);
                            e.Handled = true;
                        }
                    }
                }
            }
        }

        private void LogOutputEditor_Loaded(object sender, RoutedEventArgs e)
        {
             // The template should be applied now, try to find the ruler
             // Use VisualTreeHelper to find the element within the template
             _overviewRuler = FindVisualChild<Logonaut.UI.Helpers.OverviewRulerMargin>(LogOutputEditor);

             if (_overviewRuler != null)
             {
                // Hook up the event handler
                _overviewRuler.RequestScrollOffset += OverviewRuler_RequestScrollOffset;
             }
             else
             {
                // TODO: Log or handle the case where the ruler wasn't found
                System.Diagnostics.Debug.WriteLine("OverviewRulerMargin not found in TextEditor template.");
             }

             // Unsubscribe when the editor unloads to prevent memory leaks
             LogOutputEditor.Unloaded += (s, ev) => {
                 if (_overviewRuler != null)
                 {
                     _overviewRuler.RequestScrollOffset -= OverviewRuler_RequestScrollOffset;
                 }
                 // Also unsubscribe from Loaded/Unloaded? Might not be necessary if window closes.
             };
        }

        private void LogOutputEditor_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left && DataContext is ViewModels.MainViewModel viewModel)
            {
                // Get the position in the text where the user clicked
                var positionInfo = LogOutputEditor.TextArea.TextView.GetPositionFloor(e.GetPosition(LogOutputEditor.TextArea.TextView));
                if (positionInfo.HasValue)
                {
                    // Convert TextLocation (line,column) to character offset in the document
                    var textLocation = positionInfo.Value.Location;
                    var characterOffset = LogOutputEditor.Document.GetOffset(textLocation);
                    viewModel.UpdateSearchIndexFromCharacterOffset(characterOffset);
                }
            }
        }

         // Handler for the ruler's request to scroll
         private void OverviewRuler_RequestScrollOffset(object? sender, double requestedOffset)
         {
             LogOutputEditor.ScrollToVerticalOffset(requestedOffset);
         }


        // Helper to find a child element of a specific type in the visual tree
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
                    T? childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }


        private void SetupCustomMargins()
        {
            var numberMargin = new Logonaut.UI.Helpers.OriginalLineNumberMargin();
            LogOutputEditor.TextArea.LeftMargins.Add(numberMargin);

            var filteredLinesBinding = new System.Windows.Data.Binding("FilteredLogLines")
            {
                Source = this.DataContext,
                Mode = System.Windows.Data.BindingMode.OneWay
            };
            numberMargin.SetBinding(Logonaut.UI.Helpers.OriginalLineNumberMargin.FilteredLinesSourceProperty, filteredLinesBinding);

            var visibilityBinding = new System.Windows.Data.Binding("IsCustomLineNumberMarginVisible")
            {
                Source = this.DataContext,
                Mode = System.Windows.Data.BindingMode.OneWay
            };
            numberMargin.SetBinding(UIElement.VisibilityProperty, visibilityBinding);

            // --- Separator Margin ---
            var lineSeparatorMargin = new Logonaut.UI.Helpers.VerticalLineMargin();
            lineSeparatorMargin.SetBinding(UIElement.VisibilityProperty, visibilityBinding);
            LogOutputEditor.TextArea.LeftMargins.Add(lineSeparatorMargin);
        }


        private void MainWindow_SourceInitialized(object sender, EventArgs e)
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