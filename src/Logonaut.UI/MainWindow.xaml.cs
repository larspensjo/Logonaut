﻿using System;
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
    // TODO: Keep track of optional selected line in log window. Use highlighting to show.
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
        private PersistentLineHighlightRenderer? _highlightRenderer;

        private static Logonaut.Core.FileSystemSettingsService _settingsService = new();
        private static Logonaut.LogTailing.LogTailerService _logTailerService = new();

        // It seems as if an constructor without parameters is required for XAML to work properly.
        // Inject a settings service.
        public MainWindow() : this(new MainViewModel(_settingsService, _logTailerService)) {}

        // Enable injection of the ViewModel for testing purposes
        public MainWindow(MainViewModel viewModel)
        {
            // InitializeComponent() is the method generated from the XAML. When it runs, it parses the XAML, creates the UI elements, and wires them up.
            InitializeComponent();
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

            var logOutputEditor = LogOutputEditor; // Work-around to minimize intellisense issues in XAML
            // Hook up event handlers AFTER the template is applied
            logOutputEditor.Loaded += LogOutputEditor_Loaded;
            
            // Enable clipboard paste functionality
            logOutputEditor.TextArea.PreviewKeyDown += LogOutputEditor_PreviewKeyDown;

            // Handle mouse clicks for search reference point
            logOutputEditor.TextArea.MouseDown += LogOutputEditor_MouseDown;

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
            var logOutputEditor = LogOutputEditor; // Work-around to minimize intellisense issues in XAML

            // --- Overview Ruler Setup ---
            _overviewRuler = FindVisualChild<Logonaut.UI.Helpers.OverviewRulerMargin>(logOutputEditor);
            if (_overviewRuler is null)
                throw new InvalidOperationException("OverviewRulerMargin not found in TextEditor template.");
            _overviewRuler.RequestScrollOffset += OverviewRuler_RequestScrollOffset;
            // No need for the extra Unloaded lambda here, LogOutputEditor_Unloaded handles it.

            // Get TextView *once*
            TextView textView = logOutputEditor.TextArea.TextView;
            if (textView == null) // Add null check for safety
                 throw new InvalidOperationException("TextView not found within LogOutputEditor.");


            // --- Highlight Renderer Setup ---
            _highlightRenderer = new PersistentLineHighlightRenderer(textView);

            // Highlight Index Binding
            var highlightIndexBinding = new Binding("HighlightedFilteredLineIndex")
            {
                Source = _viewModel,
                Mode = BindingMode.OneWay
            };
            BindingOperations.SetBinding(
                _highlightRenderer,
                PersistentLineHighlightRenderer.HighlightedLineIndexProperty,
                highlightIndexBinding
            );

            // Highlight Brush Binding (using TextView.Tag as proxy)
            textView.SetResourceReference(TextView.TagProperty, "PersistedHighlightBrush"); // Set resource on Tag
            var highlightBrushBinding = new Binding("Tag") // Bind to Tag
            {
                Source = textView,
                Mode = BindingMode.OneWay
            };
            BindingOperations.SetBinding(
                _highlightRenderer,
                PersistentLineHighlightRenderer.HighlightBrushProperty,
                highlightBrushBinding
            );
            textView.BackgroundRenderers.Add(_highlightRenderer);

            // --- Chunk Separator Setup ---
            _chunkSeparator = new ChunkSeparatorRenderer(textView);

            // Chunk Separator Brush Binding (using TextView.ToolTip as proxy)
            textView.SetResourceReference(TextView.ToolTipProperty, "ChunkSeparatorBrush"); // Use ToolTipProperty

            var chunkBrushBinding = new Binding("ToolTip")
            {
                Source = textView,
                Mode = BindingMode.OneWay
            };
            BindingOperations.SetBinding(
                _chunkSeparator,
                ChunkSeparatorRenderer.SeparatorBrushProperty,
                chunkBrushBinding // Use the correct binding variable
            );
            textView.BackgroundRenderers.Add(_chunkSeparator);
            _chunkSeparator.UpdateChunks(_viewModel.FilteredLogLines, _viewModel.ContextLines);

            // --- Final Cleanup Subscription ---
            logOutputEditor.Unloaded += LogOutputEditor_Unloaded; // Subscribe the main unload handler ONCE
        }

        private void LogOutputEditor_Unloaded(object? sender, RoutedEventArgs? e)
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

            if (_highlightRenderer != null)
            {
                 TextView textView = LogOutputEditor.TextArea.TextView;
                 BindingOperations.ClearBinding(_highlightRenderer, PersistentLineHighlightRenderer.HighlightedLineIndexProperty);
                 BindingOperations.ClearBinding(_highlightRenderer, PersistentLineHighlightRenderer.HighlightBrushProperty);
                 textView.ClearValue(TextView.TagProperty); // Clear the Tag property used for highlight brush

                 if (textView.BackgroundRenderers.Contains(_highlightRenderer))
                 {
                    textView.BackgroundRenderers.Remove(_highlightRenderer);
                 }
            }
            _highlightRenderer = null;

            if (_chunkSeparator != null)
            {
                TextView textView = LogOutputEditor.TextArea.TextView;
                BindingOperations.ClearBinding(_chunkSeparator, ChunkSeparatorRenderer.SeparatorBrushProperty);
                textView.ClearValue(TextView.ToolTipProperty); // Clear the ToolTip property used for chunk brush

                if (textView.BackgroundRenderers.Contains(_chunkSeparator))
                {
                    textView.BackgroundRenderers.Remove(_chunkSeparator);
                }
                _chunkSeparator.Dispose();
            }
            _chunkSeparator = null;
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
            var logOutputEditor = LogOutputEditor; // Work-around to minimize intellisense issues in XAML
             // The template should be applied now, try to find the ruler
             // Use VisualTreeHelper to find the element within the template
             _overviewRuler = FindVisualChild<Logonaut.UI.Helpers.OverviewRulerMargin>(logOutputEditor);

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
             logOutputEditor.Unloaded += (s, ev) => {
                 if (_overviewRuler != null)
                 {
                     _overviewRuler.RequestScrollOffset -= OverviewRuler_RequestScrollOffset;
                 }
                 // Also unsubscribe from Loaded/Unloaded? Might not be necessary if window closes.
             };
        }

        private void LogOutputEditor_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var logOutputEditor = LogOutputEditor; // Work-around to minimize intellisense issues in XAML
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left && DataContext is ViewModels.MainViewModel viewModel)
            {
                // Get the position in the text where the user clicked
                var positionInfo = logOutputEditor.TextArea.TextView.GetPositionFloor(e.GetPosition(logOutputEditor.TextArea.TextView));
                if (positionInfo.HasValue) // Check if a valid position was found
                {
                    // Get the 1-based line number in the *filtered* document
                    int clickedFilteredLineNumber = positionInfo.Value.Line;
                    int clickedFilteredLineIndex = clickedFilteredLineNumber - 1; // Convert to 0-based index

                    // Update the ViewModel's highlighted index
                    viewModel.HighlightedFilteredLineIndex = clickedFilteredLineIndex;

                    // Convert TextLocation (line,column) to character offset in the document
                    var characterOffset = logOutputEditor.Document.GetOffset(positionInfo.Value.Location);
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
                    if (child is null)
                        throw new ArgumentNullException(nameof(child), "Child is null. This should not happen.");
                    T? childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                        return childOfChild;
                }
            }
            return null;
        }


        private void SetupCustomMargins()
        {
            var logOutputEditor = LogOutputEditor; // Work-around to minimize intellisense issues in XAML
            var numberMargin = new Logonaut.UI.Helpers.OriginalLineNumberMargin();
            logOutputEditor.TextArea.LeftMargins.Add(numberMargin);

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
            logOutputEditor.TextArea.LeftMargins.Add(lineSeparatorMargin);
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