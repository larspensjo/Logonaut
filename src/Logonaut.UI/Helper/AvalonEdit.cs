using System.Windows;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Document; // Added for DocumentLine

namespace Logonaut.UI.Helpers;

// Deviation: Attached properties trigger direct manipulation of TextEditor.
// Justification: Encapsulates complex, AvalonEdit-specific logic (dynamic highlighting,
// selection, scrolling) tied to ViewModel data, avoiding overly complex XAML or
// burdening the ViewModel with View-specific control knowledge.
public static class AvalonEditHelper
{
    // Keep track of the editor instance and its associated custom highlighting definition
    private static readonly Dictionary<TextEditor, CustomHighlightingDefinition> _editorDefinitions = new();

    #region // --- BindableText Property ---

    public static readonly DependencyProperty BindableTextProperty =
        DependencyProperty.RegisterAttached(
            "BindableText",
            typeof(string),
            typeof(AvalonEditHelper),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBindableTextChanged));

    public static string GetBindableText(DependencyObject obj) => (string)obj.GetValue(BindableTextProperty);
    public static void SetBindableText(DependencyObject obj, string value) => obj.SetValue(BindableTextProperty, value);

    private static void OnBindableTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEditor editor)
        {
            string newText = e.NewValue as string ?? string.Empty;
            // Avoid recursive updates if editor text changed programmatically
            if (editor.Document != null && editor.Document.Text != newText)
            {
                // Using Document.Text is generally preferred over editor.Text for large updates
                editor.Document.Text = newText;
            }
            else if (editor.Document == null && !string.IsNullOrEmpty(newText))
            {
                // Handle initial setting if document was null
                editor.Document = new ICSharpCode.AvalonEdit.Document.TextDocument(newText);
            }
        }
    }

    #endregion // --- BindableText Property ---

    #region // --- EnableTextBinding Property (Two-Way Support) ---

    public static readonly DependencyProperty EnableTextBindingProperty =
        DependencyProperty.RegisterAttached(
            "EnableTextBinding",
            typeof(bool),
            typeof(AvalonEditHelper),
            new PropertyMetadata(false, OnEnableTextBindingChanged));

    public static bool GetEnableTextBinding(DependencyObject obj) => (bool)obj.GetValue(EnableTextBindingProperty);
    public static void SetEnableTextBinding(DependencyObject obj, bool value) => obj.SetValue(EnableTextBindingProperty, value);

    private static void OnEnableTextBindingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEditor editor)
        {
            // Ensure event handlers are managed correctly to avoid leaks
            editor.TextChanged -= Editor_TextChanged; // Always remove first
            if ((bool)e.NewValue)
            {
                editor.TextChanged += Editor_TextChanged;
            }
        }
    }

    private static void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (sender is TextEditor editor && GetEnableTextBinding(editor))
        {
            // Update the source property if the editor text changes
            if (editor.Document != null)
            {
                SetBindableText(editor, editor.Document.Text);
            }
        }
    }

    #endregion // --- EnableTextBinding Property ---

    #region // --- FilterSubstrings Property (Filter Highlighting) ---

    public static readonly DependencyProperty FilterSubstringsProperty =
        DependencyProperty.RegisterAttached(
            "FilterSubstrings",
            typeof(IEnumerable<string>),
            typeof(AvalonEditHelper),
            new PropertyMetadata(null, OnFilterSubstringsChanged));

    public static IEnumerable<string> GetFilterSubstrings(DependencyObject obj) => (IEnumerable<string>)obj.GetValue(FilterSubstringsProperty);
    public static void SetFilterSubstrings(DependencyObject obj, IEnumerable<string> value) => obj.SetValue(FilterSubstringsProperty, value);

    private static void OnFilterSubstringsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Use TryGetValue for safety
        if (d is TextEditor editor && _editorDefinitions.TryGetValue(editor, out var customDef))
        {
            var substrings = e.NewValue as IEnumerable<string> ?? Array.Empty<string>();
            // Update highlighting rules for filters
            customDef.UpdateFilterHighlighting(substrings);
            // Redraw needed to apply changes
            editor.TextArea?.TextView?.Redraw(); // Check for null TextView
        }
    }

    #endregion // --- FilterSubstrings Property ---

    #region // --- HighlightTimestamps Property ---

    public static readonly DependencyProperty HighlightTimestampsProperty =
        DependencyProperty.RegisterAttached(
            "HighlightTimestamps",
            typeof(bool),
            typeof(AvalonEditHelper),
            new PropertyMetadata(false, OnHighlightTimestampsChanged));

    public static bool GetHighlightTimestamps(DependencyObject obj) => (bool)obj.GetValue(HighlightTimestampsProperty);
    public static void SetHighlightTimestamps(DependencyObject obj, bool value) => obj.SetValue(HighlightTimestampsProperty, value);

    private static void OnHighlightTimestampsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEditor editor)
        {
            bool highlight = (bool)e.NewValue;
            if (highlight)
            {
                // Ensure custom definition exists and apply it
                if (!_editorDefinitions.ContainsKey(editor))
                {
                    ApplyAllHighlighting(editor); // Creates and applies definition
                }
                else
                {
                    // Re-apply if toggled back on
                    editor.SyntaxHighlighting = _editorDefinitions[editor];
                }
            }
            else
            {
                // Restore default highlighting if custom highlighting is turned off
                RestoreDefaultHighlighting(editor);
            }
            editor.TextArea?.TextView?.Redraw();
        }
    }

    #endregion // --- HighlightTimestamps Property ---

    #region // --- SearchTerm Property (Search Highlighting) ---

    public static readonly DependencyProperty SearchTermProperty =
        DependencyProperty.RegisterAttached(
            "SearchTerm",
            typeof(string),
            typeof(AvalonEditHelper),
            new PropertyMetadata(null, OnSearchTermChanged));

    public static string GetSearchTerm(DependencyObject obj) => (string)obj.GetValue(SearchTermProperty);
    public static void SetSearchTerm(DependencyObject obj, string value) => obj.SetValue(SearchTermProperty, value);

    private static void OnSearchTermChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEditor editor && _editorDefinitions.TryGetValue(editor, out var customDef))
        {
            var searchTerm = e.NewValue as string;
            // Assuming case-insensitive search for now, adjust if needed
            customDef.UpdateSearchHighlighting(searchTerm, matchCase: false);
            editor.TextArea?.TextView?.Redraw();
        }
    }

    #endregion // --- SearchTerm Property ---

    #region // --- Selection Properties (Select/Scroll to Match) ---

    public static readonly DependencyProperty SelectOffsetProperty =
        DependencyProperty.RegisterAttached(
            "SelectOffset",
            typeof(int),
            typeof(AvalonEditHelper),
            new PropertyMetadata(-1, OnSelectionChanged)); // Default -1 means no active selection via binding

    public static int GetSelectOffset(DependencyObject obj) => (int)obj.GetValue(SelectOffsetProperty);
    public static void SetSelectOffset(DependencyObject obj, int value) => obj.SetValue(SelectOffsetProperty, value);

    public static readonly DependencyProperty SelectLengthProperty =
        DependencyProperty.RegisterAttached(
            "SelectLength",
            typeof(int),
            typeof(AvalonEditHelper),
            new PropertyMetadata(0, OnSelectionChanged));

    public static int GetSelectLength(DependencyObject obj) => (int)obj.GetValue(SelectLengthProperty);
    public static void SetSelectLength(DependencyObject obj, int value) => obj.SetValue(SelectLengthProperty, value);

    // Callback when either SelectOffset or SelectLength changes
    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEditor editor)
        {
            int offset = GetSelectOffset(editor);
            int length = GetSelectLength(editor);

            // Perform selection and scroll asynchronously on the UI thread
            // This prevents potential issues if the properties change rapidly
            // or if the document is updated concurrently.
            editor.Dispatcher.BeginInvoke(new Action(() =>
            {
                // Check validity within the dispatched action, as state might have changed
                if (editor.Document == null || offset < 0 || length < 0 || offset + length > editor.Document.TextLength)
                {
                    // If invalid, clear selection or do nothing
                    // editor.Select(0, 0); // Option: clear selection
                    // TODO: Better error handling
                    System.Diagnostics.Debug.WriteLine($"Invalid selection parameters: Offset={offset}, Length={length}, DocLength={editor.Document?.TextLength ?? -1}");
                    return;
                }

                try
                {
                    // Select the text range
                    editor.Select(offset, length);

                    // Scroll the selection into view
                    // Get the line containing the start of the selection
                    DocumentLine line = editor.Document.GetLineByOffset(offset);
                    // Use ScrollToLine to bring the line into view. This is simpler than calculating pixel offsets.
                    editor.ScrollToLine(line.LineNumber);

                    // Optional: Attempt to center the line vertically if ScrollToLine isn't sufficient
                    // This can be less reliable and depends on the editor's layout state.
                    // var visualTop = editor.TextArea.TextView.GetVisualTopByDocumentLine(line.LineNumber);
                    // if (!double.IsNaN(visualTop) && !double.IsInfinity(visualTop)) {
                    //     editor.ScrollToVerticalOffset(visualTop - (editor.TextArea.TextView.ActualHeight / 3));
                    // }
                }
                catch (ArgumentOutOfRangeException argEx)
                {
                    // Catch specific exceptions related to invalid offsets/lengths
                    // TODO: Better error handling
                    System.Diagnostics.Debug.WriteLine($"Error during editor Select/ScrollTo (ArgumentOutOfRange): {argEx.Message}");
                }
                catch (Exception ex)
                {
                    // Catch other potential errors
                    // TODO: Better error handling
                    System.Diagnostics.Debug.WriteLine($"Error during editor Select/ScrollTo: {ex.Message}");
                }
            }), DispatcherPriority.Background); // Use Background priority to allow other UI updates first
        }
    }

    #endregion // --- Selection Properties ---

    #region // --- Highlighting Management ---

    // Applies all relevant highlighting rules based on current settings
    private static void ApplyAllHighlighting(TextEditor editor)
    {
        // Ensure definition exists or create it for this editor instance
        if (!_editorDefinitions.TryGetValue(editor, out var definition))
        {
            definition = new CustomHighlightingDefinition();
            _editorDefinitions[editor] = definition;
        }

        // Clear existing rules before re-applying to ensure clean state
        // (Could be optimized later if specific rule sets are managed)
        // definition.ClearRules(); // Be careful if ClearRules also clears colors

        // Re-apply common patterns and rules
        definition.AddCommonTimestampPatterns(); // Consider making this idempotent or clearing only specific rules
        definition.AddRule(@"\bERROR\b|\bFAILED\b|\bEXCEPTION\b", "error", true);
        definition.AddRule(@"\bWARN\b|\bWARNING\b", "warning", true);
        definition.AddRule(@"\bINFO\b|\bINFORMATION\b", "info", true);

        // Apply current filter highlighting rules
        var substrings = GetFilterSubstrings(editor);
        definition.UpdateFilterHighlighting(substrings ?? Array.Empty<string>());

        // Apply current search term highlighting rule
        var searchTerm = GetSearchTerm(editor);
        definition.UpdateSearchHighlighting(searchTerm); // Assumes case-insensitive

        // Apply the potentially updated highlighting definition to the editor
        editor.SyntaxHighlighting = definition;

        // Redraw is typically handled by the methods that trigger this (e.g., OnHighlightTimestampsChanged)
    }

    // Restores default AvalonEdit highlighting
    private static void RestoreDefaultHighlighting(TextEditor editor)
    {
        // Use built-in "Log" or null for no highlighting
        editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Log");
        // Note: Doesn't remove the definition from _editorDefinitions, allowing toggling back on.
    }

    #endregion // --- Highlighting Management ---
}
