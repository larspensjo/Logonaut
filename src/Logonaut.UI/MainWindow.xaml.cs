using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data; // Required for Binding
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
        private OverviewRulerMargin? _overviewRuler;
        private ChunkSeparatorRenderer? _chunkSeparator;
        private bool _disposed;

        private static Logonaut.Core.FileSystemSettingsService _settingsService = new();

        // It seems as if an constructor without parameters is required for XAML to work properly.
        // Inject a settings service.
        public MainWindow() : this(new MainViewModel(_settingsService)) {}

        // Enable injection of the ViewModel for testing purposes
        public MainWindow(MainViewModel viewModel)
        {
            // InitializeComponent() is the method generated from the XAML. When it runs, it parses the XAML, creates the UI elements, and wires them up.
            try 
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during initialization
                MessageBox.Show($"Error initializing the UI: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw; // Rethrow the exception to ensure the application doesn't continue in an invalid state
            }
            DataContext = viewModel;
            _viewModel = viewModel;

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
                // Ensure editor unload cleanup runs if window closes before unload fires
                LogOutputEditor_Unloaded(null, null);

                // Dispose ViewModel
                _viewModel?.Cleanup(); // Use existing cleanup which includes Dispose

                if (_viewModel != null)
                    _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

                _disposed = true;
                GC.SuppressFinalize(this); // If you add a finalizer
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
            TextView textView = LogOutputEditor.TextArea.TextView;
            _chunkSeparator = new ChunkSeparatorRenderer(textView);

            // Bind the SeparatorBrush property to the Dynamic Resource
            // We need to do this in code as TextView is internal to AvalonEdit's template
            textView.SetResourceReference(
                TextView.TagProperty,
                "ChunkSeparatorBrush"); // The key defined in theme XAML files

            // Bind ChunkSeparatorRenderer.SeparatorBrush to TextView.Tag
            // This creates a standard one-way WPF binding. It binds the SeparatorBrush property of our _chunkSeparator instance to the Tag property
            // of the textView. Now, whenever textView.Tag changes (because the dynamic resource updated), this binding will push the new Brush
            // value into _chunkSeparator.SeparatorBrush.
            var brushBinding = new Binding("Tag")
            {
                Source = textView, // Bind to the TextView instance
                Mode = BindingMode.OneWay // Get the value from TextView.Tag
            };
            BindingOperations.SetBinding(
                _chunkSeparator,
                ChunkSeparatorRenderer.SeparatorBrushProperty,
                brushBinding);
            textView.BackgroundRenderers.Add(_chunkSeparator);
            _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, _viewModel.ContextLines);

            // Clean up when editor unloads
            LogOutputEditor.Unloaded += LogOutputEditor_Unloaded;
        }

        private void LogOutputEditor_Unloaded(object? sender, RoutedEventArgs e)
        {
            // Clean up Overview Ruler binding
            if (_overviewRuler != null)
                _overviewRuler.RequestScrollOffset -= OverviewRuler_RequestScrollOffset;

            // Clean up Chunk Separator
            if (_chunkSeparator != null)
            {
                 TextView textView = LogOutputEditor.TextArea.TextView;
                 // Clear bindings
                 BindingOperations.ClearBinding(_chunkSeparator, ChunkSeparatorRenderer.SeparatorBrushProperty);
                 textView.ClearValue(TextView.TagProperty); // Clear the resource reference on Tag

                 // Remove renderer and dispose
                 if(textView.BackgroundRenderers.Contains(_chunkSeparator))
                 {
                    textView.BackgroundRenderers.Remove(_chunkSeparator);
                 }
                 _chunkSeparator.Dispose();
            }
            _overviewRuler = null; // Release reference
            _chunkSeparator = null; // Release reference
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
                viewModel.SelectedFilterNode = e.NewValue as FilterViewModel;
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