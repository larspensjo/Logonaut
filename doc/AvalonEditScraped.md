# AvalonEdit API and Internal Insights Summary

This document provides a summary of the public API of the AvalonEdit library and includes key internal implementation details relevant for effective usage and extension.

## Namespace: `ICSharpCode.AvalonEdit`

### Class: `TextEditor`

Inherits from `System.Windows.Controls.Control`, implements `ITextEditorComponent`, `System.IServiceProvider`, `System.Windows.IWeakEventListener`.

**Summary:** The main control for the AvalonEdit text editor. It wraps a `TextArea` and `TextView` and provides common text editor functionality like scrolling, syntax highlighting management, and options.

**Public Properties:**

*   `Document`: `TextDocument` - Gets/Sets the document displayed by the text editor.
*   `Options`: `TextEditorOptions` - Gets/Sets the options currently used by the text editor.
*   `Text`: `string` - Gets/Sets the text of the current document.
*   `SyntaxHighlighting`: `IHighlightingDefinition` - Gets/sets the syntax highlighting definition used.
*   `IsReadOnly`: `bool` - Gets/Sets whether the text editor content can be changed by the user.
*   `IsModified`: `bool` - Gets/Sets the 'modified' flag, indicating if the document has unsaved changes. Tied to the `UndoStack.IsOriginalFile`.
*   `ShowLineNumbers`: `bool` - Gets/Sets whether line numbers are shown.
*   `LineNumbersForeground`: `Brush` - Gets/Sets the brush used for displaying line numbers.
*   `WordWrap`: `bool` - Gets/Sets whether word wrapping is enabled. Overrides `HorizontalScrollBarVisibility`.
*   `TextArea`: `TextArea` (readonly) - Gets the underlying `TextArea` instance.
*   `Encoding`: `Encoding` - Gets/sets the encoding used when saving the file. Auto-detected on load.
*   `CaretOffset`: `int` - Gets/Sets the caret position offset.
*   `SelectionStart`: `int` - Gets/Sets the start position offset of the selection.
*   `SelectionLength`: `int` - Gets/Sets the length of the selection.
*   `SelectedText`: `string` - Gets/Sets the selected text.
*   `LineCount`: `int` (readonly) - Gets the number of lines in the document.
*   `CanUndo`: `bool` (readonly) - Gets if the most recent command can be undone.
*   `CanRedo`: `bool` (readonly) - Gets if the most recent undone command can be redone.
*   `ExtentHeight`, `ExtentWidth`, `ViewportHeight`, `ViewportWidth`, `VerticalOffset`, `HorizontalOffset`: `double` (readonly) - Scrolling properties (forwarded from internal ScrollViewer/TextView).
*   `HorizontalScrollBarVisibility`: `ScrollBarVisibility` - Gets/Sets the horizontal scroll bar visibility.
*   `VerticalScrollBarVisibility`: `ScrollBarVisibility` - Gets/Sets the vertical scroll bar visibility.

**Public Methods:**

*   `Load(Stream stream)`: Loads text from a stream, auto-detecting encoding.
*   `Load(string fileName)`: Loads text from a file, auto-detecting encoding.
*   `Save(Stream stream)`: Saves the text to a stream using the current `Encoding`.
*   `Save(string fileName)`: Saves the text to a file using the current `Encoding`.
*   `AppendText(string textData)`: Appends text to the end of the document.
*   `Clear()`: Clears the text content.
*   `Select(int start, int length)`: Selects the specified text section.
*   `ScrollToLine(int line)`: Scrolls vertically to the specified line.
*   `ScrollTo(int line, int column)`: Scrolls vertically and horizontally to the specified position.
*   `ScrollTo(int line, int column, VisualYPosition yPositionMode, double referencedVerticalViewPortOffset, double minimumScrollFraction)`: Advanced scrolling method.
*   `GetPositionFromPoint(Point point)`: `TextViewPosition?` - Gets the text position from a point relative to the TextEditor control.
*   `BeginChange()` / `EndChange()` / `DeclareChangeBlock()`: Methods for managing document update grouping (forwarded to `TextDocument`).
*   `Copy()` / `Cut()` / `Paste()` / `Delete()` / `SelectAll()` / `Undo()` / `Redo()`: Standard editing commands.
*   `ScrollToEnd()` / `ScrollToHome()` / `ScrollToHorizontalOffset(double offset)` / `ScrollToVerticalOffset(double offset)`: Scrolling methods.
*   `LineUp()` / `LineDown()` / `LineLeft()` / `LineRight()` / `PageUp()` / `PageDown()` / `PageLeft()` / `PageRight()`: Scrolling methods.

**Public Events:**

*   `DocumentChanged`: `EventHandler` - Occurs when the `Document` property changes.
*   `OptionChanged`: `PropertyChangedEventHandler` - Occurs when the `Options` property changes or an option within the current list changes.
*   `TextChanged`: `EventHandler` - Occurs when the `Text` property changes (document content changes).
*   `PreviewMouseHover`, `MouseHover`, `PreviewMouseHoverStopped`, `MouseHoverStopped`: `MouseEventHandler` - Mouse hover events.

**Internal Insights:**

*   The `TextEditor` is the main user-facing control. It primarily acts as a container for a `ScrollViewer` which in turn hosts the `TextArea`.
*   Most editing and rendering logic resides within the `TextArea` and its contained `TextView`.
*   It manages the installation and switching of the `HighlightingColorizer` based on the `SyntaxHighlighting` property.
*   Handles focus management, forwarding focus to the internal `TextArea`.
*   Connects the `IsModified` property to the `UndoStack`'s `IsOriginalFile` state.
*   Manages the visibility of the `LineNumberMargin` based on the `ShowLineNumbers` property.

### Interface: `ITextEditorComponent`

**Summary:** Common interface implemented by `TextEditor`, `TextArea`, and `TextView`, providing access to the document and options.

**Public Properties:**

*   `Document`: `TextDocument` (readonly) - Gets the document being edited.
*   `Options`: `TextEditorOptions` (readonly) - Gets the options of the text editor component.

**Public Events:**

*   `DocumentChanged`: `EventHandler` - Occurs when the `Document` property changes.
*   `OptionChanged`: `PropertyChangedEventHandler` - Occurs when the `Options` property changes or an option within it changes.

### Class: `TextEditorOptions`

Inherits from `System.ComponentModel.INotifyPropertyChanged`.

**Summary:** Contains various options for controlling the behavior and appearance of the text editor.

**Public Properties (examples):**

*   `ShowSpaces`, `ShowTabs`, `ShowEndOfLine`, `ShowBoxForControlCharacters`: `bool` - Control visibility of non-printable characters.
*   `EnableHyperlinks`, `EnableEmailHyperlinks`, `RequireControlModifierForHyperlinkClick`: `bool` - Configure hyperlink behavior.
*   `IndentationSize`: `int` - Width of an indentation unit.
*   `ConvertTabsToSpaces`: `bool` - Use spaces instead of tabs for indentation.
*   `IndentationString`: `string` (readonly) - Gets the string used for one level of indentation based on other options.
*   `CutCopyWholeLine`: `bool` - Whether copy/cut without selection affects the whole line.
*   `AllowScrollBelowDocument`: `bool` - Whether scrolling past the end of the document is allowed.
*   `WordWrapIndentation`: `double` - Indentation for wrapped lines (excluding the first line of the wrap).
*   `InheritWordWrapIndentation`: `bool` - Whether wrapped lines inherit indentation from their first line.
*   `EnableRectangularSelection`: `bool` - Enables Alt+Mouse Drag rectangular selection.
*   `EnableTextDragDrop`: `bool` - Enables dragging text within the editor.
*   `EnableVirtualSpace`: `bool` - Allows the caret to be placed beyond the end of a line.
*   `EnableImeSupport`: `bool` - Enables Input Method Editor support.
*   `ShowColumnRuler`: `bool` - Shows a vertical ruler at a specified column.
*   `ColumnRulerPosition`: `int` - The column position for the ruler.
*   `HighlightCurrentLine`: `bool` - Enables highlighting of the line containing the caret.
*   `HideCursorWhileTyping`: `bool` - Hides the mouse cursor during text input.
*   `AllowToggleOverstrikeMode`: `bool` - Allows toggling overstrike mode via the Insert key.

**Public Methods:**

*   `GetIndentationString(int column)`: `string` - Gets the text required to indent from the specified column to the next indentation level, respecting `ConvertTabsToSpaces`.

**Internal Insights:**

*   Changing options typically raises the `PropertyChanged` event and might trigger redraws or invalidate measures in the `TextView` or `TextArea`.
*   This class is designed to be cloneable and serializable.

### Static Class: `AvalonEditCommands`

**Summary:** Defines custom routed commands specific to AvalonEdit.

**Public Commands (examples):**

*   `ToggleOverstrike`: Toggles overstrike mode (Ins).
*   `DeleteLine`: Deletes the current line (Ctrl+D).
*   `RemoveLeadingWhitespace`, `RemoveTrailingWhitespace`: Remove whitespace commands.
*   `ConvertToUppercase`, `ConvertToLowercase`, `ConvertToTitleCase`, `InvertCase`: Text case conversion commands.
*   `ConvertTabsToSpaces`, `ConvertSpacesToTabs`, `ConvertLeadingTabsToSpaces`, `ConvertLeadingSpacesToTabs`: Tab/space conversion commands.
*   `IndentSelection`: Runs the current `IIndentationStrategy` (Ctrl+I).

## Namespace: `ICSharpCode.AvalonEdit.CodeCompletion`

### Class: `CompletionWindow`

Inherits from `CompletionWindowBase`.

**Summary:** Represents the code completion drop-down window.

**Public Properties:**

*   `CompletionList`: `CompletionList` (readonly) - Gets the list box used in the completion window.
*   `CloseAutomatically`: `bool` - Gets/Sets whether the window should close automatically (e.g., on focus loss). Default is true.
*   `CloseWhenCaretAtBeginning`: `bool` - Gets/Sets whether the window closes if the caret moves to the start offset.

**Internal Insights:**

*   Handles user interaction within the completion list (keyboard navigation, selection, insertion request).
*   Manages a tooltip to show item descriptions.
*   Attaches to `TextArea` events (`Caret.PositionChanged`, `MouseWheel`, `PreviewTextInput`) to control its behavior and lifetime.
*   Does not actually take focus; it intercepts keyboard events from the `TextArea`.

### Class: `CompletionList`

Inherits from `System.Windows.Controls.Control`.

**Summary:** The list control used within the `CompletionWindow`.

**Public Properties:**

*   `IsFiltering`: `bool` - Controls filtering behavior (show only matching items vs. simple prefix matching). Default is true.
*   `EmptyTemplate`: `ControlTemplate` - Template shown when the list is empty.
*   `ListBox`: `CompletionListBox` (readonly) - Gets the internal `CompletionListBox`.
*   `ScrollViewer`: `ScrollViewer` (readonly) - Gets the internal `ScrollViewer`.
*   `CompletionData`: `IList<ICompletionData>` (readonly) - Gets the list where completion items (`ICompletionData`) are added.
*   `SelectedItem`: `ICompletionData` - Gets/Sets the selected completion item.

**Public Methods:**

*   `RequestInsertion(EventArgs e)`: Raises the `InsertionRequested` event.
*   `HandleKey(KeyEventArgs e)`: Allows the list to handle key presses even when focus is elsewhere.
*   `SelectItem(string text)`: Selects the best matching item based on the typed text and the `IsFiltering` mode.
*   `ScrollIntoView(ICompletionData item)`: Scrolls the specified item into view.

**Public Events:**

*   `InsertionRequested`: `EventHandler` - Raised when the user chooses an item.
*   `SelectionChanged`: `SelectionChangedEventHandler` - Occurs when the `SelectedItem` changes.

**Internal Insights:**

*   Implements the core logic for filtering/selecting items based on user input (`SelectItemFiltering`, `SelectItemWithStart`, `GetMatchQuality`).
*   Supports CamelCase matching.

### Interface: `ICompletionData`

**Summary:** Interface for items displayed in the completion list.

**Public Properties:**

*   `Image`: `ImageSource` - Icon to display.
*   `Text`: `string` - Text used for matching and filtering.
*   `Content`: `object` - Content displayed in the list (can be text or a UIElement).
*   `Description`: `object` - Content displayed in the tooltip (can be text or a UIElement).
*   `Priority`: `double` - Used for sorting/preferring items in the selection logic.

**Public Methods:**

*   `Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)`: Called to perform the completion action (e.g., inserting text).

### Class: `OverloadInsightWindow`

Inherits from `InsightWindow`.

**Summary:** An insight window specifically designed to show method overloads using an `OverloadViewer`.

**Public Properties:**

*   `Provider`: `IOverloadProvider` - Gets/Sets the provider that supplies the overload items.

**Internal Insights:**

*   Handles Up/Down arrow keys to navigate through overloads provided by the `IOverloadProvider`.

### Interface: `IOverloadProvider`

Inherits from `System.ComponentModel.INotifyPropertyChanged`.

**Summary:** Provides items for the `OverloadViewer`.

**Public Properties:**

*   `SelectedIndex`: `int` - Gets/Sets the index of the currently selected overload.
*   `Count`: `int` (readonly) - Gets the total number of overloads.
*   `CurrentIndexText`: `string` (readonly) - Gets text like "1 of 5".
*   `CurrentHeader`: `object` (readonly) - Gets the header content for the current overload.
*   `CurrentContent`: `object` (readonly) - Gets the main content for the current overload.

## Namespace: `ICSharpCode.AvalonEdit.Document`

### Interface: `IDocument`

Inherits from `ITextSource`, `System.IServiceProvider`.

**Summary:** Represents the text model, providing access to text content, line information, change events, and text anchors.

**Public Properties:**

*   `Text`: `string` - Gets/Sets the entire text content. (Setter added compared to `ITextSource`)
*   `TextLength`: `int` (readonly) - Gets the length of the text.
*   `LineCount`: `int` (readonly) - Gets the number of lines in the document.
*   `FileName`: `string` (readonly) - Gets the name of the file associated with the document (can be null).

**Public Methods:**

*   `GetLineByNumber(int lineNumber)`: `IDocumentLine` - Gets a line by its 1-based number.
*   `GetLineByOffset(int offset)`: `IDocumentLine` - Gets the line containing the specified offset.
*   `GetOffset(int line, int column)` / `GetOffset(TextLocation location)`: `int` - Converts a line/column location to an offset.
*   `GetLocation(int offset)`: `TextLocation` - Converts an offset to a line/column location.
*   `Insert(int offset, string text)` / `Insert(int offset, ITextSource text)`: Inserts text at the specified offset. Overloads allow specifying default anchor movement behavior.
*   `Remove(int offset, int length)`: Removes text.
*   `Replace(int offset, int length, string newText)` / `Replace(int offset, int length, ITextSource newText)`: Replaces text.
*   `CreateAnchor(int offset)`: `ITextAnchor` - Creates a text anchor at the specified offset.
*   `CreateSnapshot()` / `CreateSnapshot(int offset, int length)`: `ITextSource` - Creates an immutable snapshot of the document or a part of it. (Thread-safe for `TextDocument`)
*   `CreateReader()` / `CreateReader(int offset, int length)`: `TextReader` - Creates a text reader for the document content.
*   `WriteTextTo(TextWriter writer)` / `WriteTextTo(TextWriter writer, int offset, int length)`: Writes document content to a writer.
*   `BeginUpdate()` / `EndUpdate()` / `RunUpdate()`: Group document changes for undo/events. (Corresponds to `StartUndoableAction`/`EndUndoableAction`/`OpenUndoGroup` on `IDocument`)

**Public Events:**

*   `TextChanging`: `EventHandler<TextChangeEventArgs>` - Occurs just before a change is applied.
*   `TextChanged`: `EventHandler<TextChangeEventArgs>` - Occurs just after a change is applied.
*   `ChangeCompleted`: `EventHandler` - Occurs after a group of changes (ended by `EndUpdate`) is completed.
*   `FileNameChanged`: `EventHandler` - Occurs when the `FileName` property changes.

**Internal Insights:**

*   The core implementation (`TextDocument`) uses a `Rope<char>` for efficient text storage and modification.
*   Line management is handled by `LineManager` and `DocumentLineTree`.
*   Change tracking for snapshots is handled by `TextSourceVersionProvider`.
*   Text anchors are managed by `TextAnchorTree`.
*   Provides services via `IServiceProvider`, including itself as `IDocument` and `TextDocument`.

### Interface: `IDocumentLine`

Inherits from `ISegment`.

**Summary:** Represents a line within an `IDocument`.

**Public Properties:**

*   `TotalLength`: `int` (readonly) - Gets the length including the line delimiter.
*   `DelimiterLength`: `int` (readonly) - Gets the length of the line delimiter (0, 1, or 2).
*   `LineNumber`: `int` (readonly) - Gets the 1-based line number.
*   `PreviousLine`: `IDocumentLine` (readonly) - Gets the previous line (null if first line).
*   `NextLine`: `IDocumentLine` (readonly) - Gets the next line (null if last line).
*   `IsDeleted`: `bool` (readonly) - Gets whether the line was deleted.
*   `Offset`: `int` (readonly) - Gets the starting offset of the line (from `ISegment`).
*   `Length`: `int` (readonly) - Gets the length of the line excluding the delimiter (from `ISegment`).
*   `EndOffset`: `int` (readonly) - Gets the end offset of the line excluding the delimiter (from `ISegment`).

**Internal Insights:**

*   `DocumentLine` (the implementation) is a node in the `DocumentLineTree`, allowing efficient offset/line number conversions and updates (O(log N)).

### Interface: `ITextAnchor`

**Summary:** References an offset in the document that automatically updates when text is inserted or removed nearby.

**Public Properties:**

*   `Location`: `TextLocation` (readonly) - Gets the current text location (line/column).
*   `Offset`: `int` (readonly) - Gets the current offset.
*   `MovementType`: `AnchorMovementType` - Controls how the anchor moves when text is inserted exactly at its position (Default, BeforeInsertion, AfterInsertion).
*   `SurviveDeletion`: `bool` - Controls whether the anchor is deleted when the text surrounding it is deleted.
*   `IsDeleted`: `bool` (readonly) - Gets whether the anchor has been deleted.
*   `Line`: `int` (readonly) - Gets the current line number.
*   `Column`: `int` (readonly) - Gets the current column number.

**Public Events:**

*   `Deleted`: `EventHandler` - Occurs after the anchor is deleted.

**Internal Insights:**

*   Created via `IDocument.CreateAnchor`.
*   Implemented by `TextAnchor` which uses `TextAnchorNode` and `TextAnchorTree` for efficient updates (O(log N)).
*   Weak references are used by the document to hold anchors, allowing them to be garbage collected if unused.

### Interface: `ITextSource`

**Summary:** A read-only view of a text source. Base interface for `IDocument`.

**Public Properties:**

*   `Version`: `ITextSourceVersion` (readonly) - Gets a version identifier (null if unversioned).
*   `TextLength`: `int` (readonly) - Gets the total text length.
*   `Text`: `string` (readonly) - Gets the whole text as a string.

**Public Methods:**

*   `CreateSnapshot()` / `CreateSnapshot(int offset, int length)`: Creates an immutable snapshot.
*   `CreateReader()` / `CreateReader(int offset, int length)`: Creates a `TextReader`.
*   `GetCharAt(int offset)`: `char` - Gets the character at a specific offset.
*   `GetText(int offset, int length)` / `GetText(ISegment segment)`: `string` - Retrieves text for a portion.
*   `WriteTextTo(TextWriter writer)` / `WriteTextTo(TextWriter writer, int offset, int length)`: Writes text to a writer.
*   `IndexOf(char c, int startIndex, int count)` / `LastIndexOf(char c, int startIndex, int count)`: Finds character occurrences.
*   `IndexOfAny(char[] anyOf, int startIndex, int count)`: Finds occurrences of any character in a set.
*   `IndexOf(string searchText, int startIndex, int count, StringComparison comparisonType)` / `LastIndexOf(string searchText, int startIndex, int count, StringComparison comparisonType)`: Finds string occurrences.

### Class: `TextChangeEventArgs`

Inherits from `System.EventArgs`.

**Summary:** Describes a change to the document text. Immutable and thread-safe.

**Public Properties:**

*   `Offset`: `int` (readonly) - Offset where the change occurs.
*   `RemovedText`: `ITextSource` (readonly) - Text that was removed.
*   `RemovalLength`: `int` (readonly) - Length of removed text.
*   `InsertedText`: `ITextSource` (readonly) - Text that was inserted.
*   `InsertionLength`: `int` (readonly) - Length of inserted text.

**Public Methods:**

*   `GetNewOffset(int offset, AnchorMovementType movementType = AnchorMovementType.Default)`: `int` - Calculates the new offset after this change.
*   `Invert()`: `TextChangeEventArgs` - Creates `TextChangeEventArgs` for the reverse change (undo).

**Internal Insights:**

*   `DocumentChangeEventArgs` (derived class) adds an `OffsetChangeMap` to precisely control anchor movement during complex replacements.

### Class: `UndoStack`

Inherits from `System.ComponentModel.INotifyPropertyChanged`.

**Summary:** Manages undo and redo operations for a `TextDocument`.

**Public Properties:**

*   `IsOriginalFile`: `bool` (readonly) - Gets whether the document is in its original state (as marked by `MarkAsOriginalFile`).
*   `AcceptChanges`: `bool` (readonly) - Gets if the stack currently accepts changes (false during undo/redo).
*   `CanUndo`: `bool` (readonly) - Gets if there are actions to undo.
*   `CanRedo`: `bool` (readonly) - Gets if there are actions to redo.
*   `SizeLimit`: `int` - Gets/Sets the limit on the number of undoable actions.
*   `LastGroupDescriptor`: `object` (readonly) - Descriptor for the last undo group, used for potentially merging operations.

**Public Methods:**

*   `MarkAsOriginalFile()`: Marks the current state as original.
*   `DiscardOriginalFileMarker()`: Removes the original file marker.
*   `StartUndoGroup()` / `StartUndoGroup(object groupDescriptor)`: Starts grouping subsequent changes into a single undo step.
*   `StartContinuedUndoGroup(object groupDescriptor = null)`: Starts grouping, potentially merging with the previous group if possible.
*   `EndUndoGroup()`: Ends the current undo group.
*   `Undo()`: Undoes the last operation.
*   `Redo()`: Redoes the last undone operation.
*   `Push(IUndoableOperation operation)`: Pushes a custom undoable operation. Clears the redo stack.
*   `PushOptional(IUndoableOperation operation)`: Pushes an operation that will be included only if the containing undo group has non-optional operations.
*   `ClearRedoStack()`: Clears the redo stack.
*   `ClearAll()`: Clears both undo and redo stacks.

**Public Events:**

*   `PropertyChanged`: `PropertyChangedEventHandler` - Raised when `CanUndo` or `CanRedo` changes.

**Internal Insights:**

*   Uses a `Deque<IUndoableOperation>` internally for both undo and redo stacks.
*   `UndoOperationGroup` is used to combine actions within a `StartUndoGroup`/`EndUndoGroup` block.
*   Maintains an internal state (`StateListen`, `StatePlayback`, `StatePlaybackModifyDocument`) to manage behavior during undo/redo.
*   Document changes automatically push `DocumentChangeOperation` instances onto the stack if `AcceptChanges` is true.

## Namespace: `ICSharpCode.AvalonEdit.Editing`

### Class: `TextArea`

Inherits from `System.Windows.Controls.Control`, implements `IScrollInfo`, `IWeakEventListener`, `ITextEditorComponent`, `System.IServiceProvider`.

**Summary:** The core editing control, containing the `TextView` and handling user input, caret, selection, margins, and scrolling. It does not include the outer scroll bars, which are typically part of the `TextEditor` control template.

**Public Properties:**

*   `Document`: `TextDocument` - Gets/Sets the document. (Dependency Property)
*   `Options`: `TextEditorOptions` - Gets/Sets the options. (Dependency Property)
*   `TextView`: `TextView` (readonly) - Gets the underlying `TextView` used for rendering.
*   `Caret`: `Caret` (readonly) - Gets the caret instance.
*   `Selection`: `Selection` - Gets/Sets the current selection.
*   `LeftMargins`: `ObservableCollection<UIElement>` (readonly) - Gets the collection of margins displayed to the left.
*   `ReadOnlySectionProvider`: `IReadOnlySectionProvider` - Gets/Sets the provider for read-only sections. Default is `NoReadOnlySections.Instance`.
*   `IndentationStrategy`: `IIndentationStrategy` - Gets/Sets the indentation strategy. Default is `DefaultIndentationStrategy`.
*   `SelectionBrush`, `SelectionForeground`: `Brush` - Brushes for selection appearance. (Dependency Properties)
*   `SelectionBorder`: `Pen` - Pen for selection border. (Dependency Property)
*   `SelectionCornerRadius`: `double` - Corner radius for selection. (Dependency Property)
*   `OverstrikeMode`: `bool` - Gets/Sets whether overstrike mode is active. (Dependency Property)
*   `ActiveInputHandler`: `ITextAreaInputHandler` - Gets/Sets the active input handler.
*   `DefaultInputHandler`: `TextAreaDefaultInputHandler` (readonly) - Gets the default input handler chain.
*   `StackedInputHandlers`: `ImmutableStack<TextAreaStackedInputHandler>` (readonly) - Gets the stack of currently active input handlers.

**Public Methods:**

*   `ClearSelection()`: Sets the selection to empty.
*   `GetService(Type serviceType)`: `object` - Retrieves services from the `TextView` or `Document`.
*   `GetDeletableSegments(ISegment segment)`: `ISegment[]` - Gets the writable parts within a given segment based on the `ReadOnlySectionProvider`.
*   `PerformTextInput(string text)` / `PerformTextInput(TextCompositionEventArgs e)`: Programmatically performs text input.
*   `PushStackedInputHandler(TextAreaStackedInputHandler inputHandler)` / `PopStackedInputHandler(TextAreaStackedInputHandler inputHandler)`: Manages stacked input handlers.
*   `AllowCaretOutsideSelection()`: `IDisposable` - Temporarily allows the caret to be positioned outside the selection.

**Public Events:**

*   `DocumentChanged`: `EventHandler` - Occurs when the `Document` property changes.
*   `OptionChanged`: `PropertyChangedEventHandler` - Occurs when the `Options` property changes or an option within it changes.
*   `SelectionChanged`: `EventHandler` - Occurs when the `Selection` property changes.
*   `TextEntering`, `TextEntered`: `TextCompositionEventHandler` - Occur immediately before and after text input is handled.
*   `ActiveInputHandlerChanged`: `EventHandler` - Occurs when the `ActiveInputHandler` property changes.
*   `TextCopied`: `EventHandler<TextEventArgs>` - Occurs when text is copied to the clipboard.

**Internal Insights:**

*   Composition: `TextArea` owns the `TextView`, `Caret`, `Selection`, `Margins`, and `Input Handlers`.
*   Input Handling: Uses a chain/stack of `ITextAreaInputHandler` objects. The `DefaultInputHandler` contains nested handlers for caret navigation, editing, and mouse selection. Stacked handlers (like `SnippetInputHandler` or custom handlers) can temporarily take precedence.
*   Read-Only Support: Uses `IReadOnlySectionProvider` to determine editable regions. `NoReadOnlySections` (default) and `ReadOnlySectionDocument` are predefined providers. `TextSegmentReadOnlySectionProvider` is a common implementation using a `TextSegmentCollection`.
*   Caret/Selection Synchronization: Ensures the caret generally stays within the selection using a delayed validation mechanism (`EnsureSelectionValid`).
*   Event Order: Defines a specific order for events during document updates (see `TextDocument.Changing` event documentation).
*   IME Support: Contains logic (`ImeSupport`) to interact with Input Method Editors.

### Class: `Caret`

**Summary:** Manages the text input caret, including its position, appearance, and blinking.

**Public Properties:**

*   `Position`: `TextViewPosition` - Gets/Sets the caret position (validates visual column).
*   `Location`: `TextLocation` - Gets/Sets the caret location (line/column).
*   `Line`: `int` - Gets/Sets the caret line number.
*   `Column`: `int` - Gets/Sets the caret text column number.
*   `VisualColumn`: `int` - Gets/Sets the caret visual column number.
*   `Offset`: `int` - Gets/Sets the caret offset in the document.
*   `IsInVirtualSpace`: `bool` (readonly) - Gets whether the caret is in virtual space.
*   `DesiredXPos`: `double` - Gets/Sets the desired X-position (used for up/down movement). NaN if not set.
*   `CaretBrush`: `Brush` - Gets/Sets the brush used to draw the caret.

**Public Methods:**

*   `Show()`: Makes the caret visible and updates its screen position.
*   `Hide()`: Makes the caret invisible.
*   `BringCaretToView()`: Scrolls the text view so the caret is visible.
*   `CalculateCaretRectangle()`: `Rect` - Returns the caret rectangle relative to the document origin.

**Public Events:**

*   `PositionChanged`: `EventHandler` - Raised when the caret position changes.

**Internal Insights:**

*   Maintains both logical (`TextLocation`) and visual (`VisualColumn`) positions.
*   `VisualColumn` validation (`ValidateVisualColumn`) can be expensive as it might require interacting with the `TextView`'s rendering logic. Accessing `Location` is generally faster if the visual column isn't needed.
*   Integrates with `TextView`'s rendering via `CaretLayer`.
*   Uses Win32 `CreateCaret`/`SetCaretPos` for accessibility features like the Windows Magnifier.
*   Handles document changes (`OnDocumentChanging`, `OnDocumentChanged`) to update its position using `AnchorMovementType`.

### Class: `Selection`

**Summary:** Abstract base class for text selections within the `TextArea`.

**Public Properties:**

*   `StartPosition`: `TextViewPosition` (readonly) - Gets the start position of the selection.
*   `EndPosition`: `TextViewPosition` (readonly) - Gets the end position of the selection.
*   `Segments`: `IEnumerable<SelectionSegment>` (readonly) - Gets the selected text segments. For simple selection, this yields one segment; for rectangular selection, one segment per line.
*   `SurroundingSegment`: `ISegment` (readonly) - Gets the smallest segment containing all selected segments (null if empty).
*   `IsEmpty`: `bool` (readonly) - Gets whether the selection is empty.
*   `EnableVirtualSpace`: `bool` (readonly) - Gets whether virtual space is enabled for this selection (true for rectangular selection).
*   `Length`: `int` (readonly) - Gets the total length of the text in the selection (sum of segment lengths).
*   `IsMultiline`: `bool` (readonly) - Gets whether the selection spans multiple lines.

**Public Methods:**

*   `Create(TextArea textArea, int startOffset, int endOffset)` / `Create(TextArea textArea, TextViewPosition start, TextViewPosition end)` / `Create(TextArea textArea, ISegment segment)`: Static factory methods to create selections. Returns `EmptySelection` for zero-length selections.
*   `ReplaceSelectionWithText(string newText)`: Replaces the selected text with new text.
*   `UpdateOnDocumentChange(DocumentChangeEventArgs e)`: `Selection` - Creates an updated selection instance reflecting document changes.
*   `SetEndpoint(TextViewPosition endPosition)`: `Selection` - Returns a new selection with the endpoint moved.
*   `StartSelectionOrSetEndpoint(TextViewPosition startPosition, TextViewPosition endPosition)`: `Selection` - Creates a new selection or modifies the endpoint of the existing selection.
*   `GetText()`: `string` - Gets the selected text.
*   `CreateHtmlFragment(HtmlOptions options)`: `string` - Creates an HTML fragment representing the selection with highlighting.
*   `Contains(int offset)`: `bool` - Checks if the offset is contained within the selection.
*   `CreateDataObject(TextArea textArea)`: `DataObject` - Creates a data object for clipboard or drag-and-drop operations.

**Internal Insights:**

*   Implementations: `EmptySelection`, `SimpleSelection`, `RectangleSelection`.
*   `EmptySelection` is a singleton per `TextArea`.
*   `ReplaceSelectionWithText` handles `IReadOnlySectionProvider` to only modify writable segments.
*   `UpdateOnDocumentChange` uses text anchors implicitly or explicitly to track selection boundaries across document modifications.

### Class: `TextAreaInputHandler`

Implements `ITextAreaInputHandler`.

**Summary:** Base class for implementing input handling logic. Manages collections of command bindings, input bindings, and nested handlers.

**Public Properties:**

*   `TextArea`: `TextArea` (readonly) - The text area this handler belongs to.
*   `IsAttached`: `bool` (readonly) - Whether the handler is currently attached and active.
*   `CommandBindings`: `ICollection<CommandBinding>` (readonly) - Command bindings associated with this handler.
*   `InputBindings`: `ICollection<InputBinding>` (readonly) - Input bindings associated with this handler.
*   `NestedInputHandlers`: `ICollection<ITextAreaInputHandler>` (readonly) - Child input handlers managed by this handler.

**Public Methods:**

*   `Attach()`: Attaches the handler (adds bindings, attaches nested handlers).
*   `Detach()`: Detaches the handler.
*   `AddBinding(ICommand command, ModifierKeys modifiers, Key key, ExecutedRoutedEventHandler handler)`: Helper to add both command and input bindings.

**Internal Insights:**

*   Provides a structured way to group related input logic (e.g., `CaretNavigationCommandHandler`, `EditingCommandHandler`).
*   The `TextArea` uses a `DefaultInputHandler` which aggregates several specific handlers (caret, editing, mouse).
*   Custom handlers can be created and pushed onto the `TextArea.StackedInputHandlers` stack.

## Namespace: `ICSharpCode.AvalonEdit.Rendering`

### Class: `TextView`

Inherits from `System.Windows.FrameworkElement`, implements `IScrollInfo`, `IWeakEventListener`, `ITextEditorComponent`, `System.IServiceProvider`.

**Summary:** The core rendering control. It manages visual lines, interacts with the document's height tree, handles layers for background/selection/text/caret rendering, and provides coordinate transformations. It does not handle user input directly.

**Public Properties:**

*   `Document`: `TextDocument` - Gets/Sets the document. (Dependency Property)
*   `Options`: `TextEditorOptions` - Gets/Sets the options. (Dependency Property)
*   `VisualLines`: `ReadOnlyCollection<VisualLine>` (readonly) - Gets the currently rendered visual lines. Throws `VisualLinesInvalidException` if accessed when invalid.
*   `VisualLinesValid`: `bool` (readonly) - Gets whether the `VisualLines` collection is currently valid.
*   `Layers`: `UIElementCollection` (readonly) - Gets the collection of UIElement layers used for rendering (e.g., SelectionLayer, TextLayer, CaretLayer).
*   `ElementGenerators`: `IList<VisualLineElementGenerator>` (readonly) - Collection for registering custom visual line element generators.
*   `LineTransformers`: `IList<IVisualLineTransformer>` (readonly) - Collection for registering visual line transformers (e.g., colorizers).
*   `BackgroundRenderers`: `IList<IBackgroundRenderer>` (readonly) - Collection for registering background renderers.
*   `DefaultLineHeight`: `double` (readonly) - Gets the height of a standard line of text.
*   `DefaultBaseline`: `double` (readonly) - Gets the default baseline position within a line.
*   `WideSpaceWidth`: `double` (readonly) - Gets the width of a 'wide space' (typically 'x'), used for tab width calculation.
*   `DocumentHeight`: `double` (readonly) - Gets the total height of the document content in device-independent pixels.
*   `ScrollOffset`: `Vector` (readonly) - Gets the current scroll offset.
*   `NonPrintableCharacterBrush`, `LinkTextForegroundBrush`, `LinkTextBackgroundBrush`: `Brush` - Brushes for specific rendering elements. (Dependency Properties)
*   `LinkTextUnderline`: `bool` - Whether links are underlined. (Dependency Property)
*   `ColumnRulerPen`: `Pen` - Pen for the column ruler. (Dependency Property)
*   `CurrentLineBackground`: `Brush`, `CurrentLineBorder`: `Pen` - Appearance for the current line highlight. (Dependency Properties)
*   `HighlightedLine`: `int` - Gets/Sets the line number to be highlighted by the `CurrentLineHighlightRenderer`.

**Public Methods:**

*   `GetService(Type serviceType)`: `object` - Retrieves services registered with the `TextView` or its `Document`.
*   `Redraw()` / `Redraw(VisualLine visualLine, DispatcherPriority redrawPriority)` / `Redraw(int offset, int length, DispatcherPriority redrawPriority)` / `Redraw(ISegment segment, DispatcherPriority redrawPriority)`: Invalidates visual lines and schedules a re-measure/redraw.
*   `InvalidateLayer(KnownLayer knownLayer)`: Schedules a redraw for a specific layer without invalidating visual lines.
*   `EnsureVisualLines()`: Forces the creation/validation of visual lines if they are currently invalid.
*   `GetVisualLine(int documentLineNumber)`: `VisualLine` - Gets the visual line containing a document line number, if it's currently rendered. Returns null otherwise.
*   `GetOrConstructVisualLine(DocumentLine documentLine)`: `VisualLine` - Gets the visual line for a document line, constructing it if necessary (even if outside the visible area).
*   `GetVisualLineFromVisualTop(double visualTop)`: `VisualLine` - Gets the visual line at a specific vertical position.
*   `GetVisualTopByDocumentLine(int line)`: `double` - Gets the vertical position of the top of a document line.
*   `GetVisualPosition(TextViewPosition position, VisualYPosition yPositionMode)`: `Point` - Converts a `TextViewPosition` (including visual column) to a visual coordinate.
*   `GetPosition(Point visualPosition)`: `TextViewPosition?` - Converts a visual coordinate to the nearest `TextViewPosition`.
*   `GetPositionFloor(Point visualPosition)`: `TextViewPosition?` - Converts a visual coordinate to the `TextViewPosition` immediately before the point.
*   `GetDocumentLineByVisualTop(double visualTop)`: `DocumentLine` - Gets the document line at a specific vertical position.
*   `InsertLayer(UIElement layer, KnownLayer referencedLayer, LayerInsertionPosition position)`: Inserts a custom UIElement layer relative to a known layer.
*   `CollapseLines(DocumentLine start, DocumentLine end)`: `CollapsedLineSection` - Marks lines as collapsed for rendering purposes (used internally by features like folding).

**Public Events:**

*   `DocumentChanged`: `EventHandler` - Occurs when the `Document` property changes.
*   `OptionChanged`: `PropertyChangedEventHandler` - Occurs when the `Options` property changes or an option within it changes.
*   `VisualLinesChanged`: `EventHandler` - Occurs after the set of rendered visual lines has changed.
*   `VisualLineConstructionStarting`: `EventHandler<VisualLineConstructionStartEventArgs>` - Occurs before visual lines are generated for the viewport.
*   `ScrollOffsetChanged`: `EventHandler` - Occurs when the scroll offset changes.

**Internal Insights:**

*   **Rendering Pipeline:** The core process involves:
    1.  Determining the visible document lines based on scroll position and the `HeightTree`.
    2.  For each visible line (or lines spanned by collapsed sections):
        a.  Run `VisualLineElementGenerator`s to create initial `VisualLineElement`s (like `VisualLineText`).
        b.  Run `IVisualLineTransformer`s (like `HighlightingColorizer`) to modify these elements (e.g., split for color changes).
        c.  Use WPF's `TextFormatter` to create `TextLine` objects from the `VisualLineElement`s (handles shaping, word wrapping, bidi).
        d.  Store the resulting `VisualLine`.
*   **Virtualization:** Only creates `VisualLine` objects for the currently visible region (+ a small buffer usually). Relies heavily on the `HeightTree` for vertical scrolling.
*   **HeightTree:** An augmented red-black tree mapping document lines to vertical positions and heights, crucial for efficient scrolling and handling collapsed sections.
*   **Layers:** Uses a `LayerCollection` to manage rendering order (Background, Selection, Text, Caret, and custom layers). Background rendering is handled by `IBackgroundRenderer`.
*   **Inline Objects:** Manages the lifecycle and layout of UIElements embedded within the text via `InlineObjectRun` and `InlineObjectElement`.

### Class: `VisualLine`

**Summary:** Represents a line as displayed in the `TextView`. A single `VisualLine` can correspond to multiple `DocumentLine`s if lines are collapsed (e.g., via folding).

**Public Properties:**

*   `Document`: `TextDocument` (readonly) - The document containing this line.
*   `FirstDocumentLine`: `DocumentLine` (readonly) - The first document line displayed by this visual line.
*   `LastDocumentLine`: `DocumentLine` (readonly) - The last document line displayed by this visual line.
*   `Elements`: `ReadOnlyCollection<VisualLineElement>` (readonly) - The elements that make up this visual line.
*   `TextLines`: `ReadOnlyCollection<TextLine>` (readonly) - The WPF `TextLine` objects used for rendering (created after transformations).
*   `StartOffset`: `int` (readonly) - The start offset in the document.
*   `VisualLength`: `int` (readonly) - The length in visual columns (excluding end-of-line marker).
*   `VisualLengthWithEndOfLineMarker`: `int` (readonly) - The length in visual columns including a potential end-of-line marker.
*   `Height`: `double` (readonly) - The total height of the visual line.
*   `VisualTop`: `double` (readonly) - The vertical position relative to the document start.
*   `IsDisposed`: `bool` (readonly) - Gets whether the visual line has been disposed.

**Public Methods:**

*   `GetVisualColumn(int relativeTextOffset)`: `int` - Gets visual column from a document offset relative to the visual line's start.
*   `GetRelativeOffset(int visualColumn)`: `int` - Gets the document offset relative to the visual line's start from a visual column.
*   `GetTextLine(int visualColumn)` / `GetTextLine(int visualColumn, bool isAtEndOfLine)`: `TextLine` - Gets the WPF `TextLine` containing the specified visual column.
*   `GetTextLineVisualYPosition(TextLine textLine, VisualYPosition yPositionMode)`: `double` - Gets the Y position of a specific `TextLine` within the `VisualLine`.
*   `GetTextLineVisualStartColumn(TextLine textLine)`: `int` - Gets the starting visual column of a specific `TextLine`.
*   `GetTextLineByVisualYPosition(double visualTop)`: `TextLine` - Gets the `TextLine` at a specific Y position within the `VisualLine`.
*   `GetVisualPosition(int visualColumn, VisualYPosition yPositionMode)`: `Point` - Gets the visual coordinate for a visual column.
*   `GetTextViewPosition(int visualColumn)` / `GetTextViewPosition(Point visualPosition, bool allowVirtualSpace)` / `GetTextViewPositionFloor(Point visualPosition, bool allowVirtualSpace)`: `TextViewPosition` - Converts visual columns/coordinates to `TextViewPosition`.
*   `GetNextCaretPosition(int visualColumn, LogicalDirection direction, CaretPositioningMode mode, bool allowVirtualSpace)`: `int` - Finds the next valid caret stop.

**Internal Insights:**

*   Represents the link between the logical document (`DocumentLine`) and the rendered output (`TextLine`).
*   Is constructed on demand by the `TextView`'s measure pass.
*   Holds the list of `VisualLineElement`s which are processed by generators and transformers.
*   Manages the conversion between visual columns and document offsets within the line.

### Class: `VisualLineElementGenerator`

**Summary:** Abstract base class for generators that create `VisualLineElement`s based on document content. Used before line transformers.

**Public Methods:**

*   `StartGeneration(ITextRunConstructionContext context)`: Called before processing a document line.
*   `FinishGeneration()`: Called after processing a document line.
*   `GetFirstInterestedOffset(int startOffset)`: `int` - Returns the first offset >= `startOffset` where this generator might want to produce an element, or -1 if not interested.
*   `ConstructElement(int offset)`: `VisualLineElement` - Constructs an element at the specified offset. Can return null.

**Internal Insights:**

*   Used early in the rendering pipeline to replace text sections with custom elements (e.g., displaying spaces/tabs, handling folding).
*   Examples: `SingleCharacterElementGenerator`, `FoldingElementGenerator`, `LinkElementGenerator`.

### Interface: `IVisualLineTransformer`

**Summary:** Allows modifying the list of `VisualLineElement`s after they have been generated.

**Public Methods:**

*   `Transform(ITextRunConstructionContext context, IList<VisualLineElement> elements)`: Modifies the elements for the current visual line.

**Internal Insights:**

*   Used later in the rendering pipeline than generators.
*   Primary use case is syntax highlighting (`HighlightingColorizer`, `DocumentColorizingTransformer`) which splits `VisualLineText` elements and applies colors.
*   Transformers can split elements but generally should not change the total `DocumentLength` represented by the elements.

### Interface: `IBackgroundRenderer`

**Summary:** Allows custom drawing behind or in front of specific text layers (`KnownLayer`).

**Public Properties:**

*   `Layer`: `KnownLayer` (readonly) - Specifies the layer to draw on (e.g., Background, Selection, Text).

**Public Methods:**

*   `Draw(TextView textView, DrawingContext drawingContext)`: Performs the custom drawing.

**Internal Insights:**

*   Registered in `TextView.BackgroundRenderers`.
*   Provides a way to add visual elements without the overhead of full UIElements (e.g., `SearchResultBackgroundRenderer`, `ColumnRulerRenderer`).

## Namespace: `ICSharpCode.AvalonEdit.Highlighting`

### Class: `HighlightingManager`

Implements `IHighlightingDefinitionReferenceResolver`.

**Summary:** Manages syntax highlighting definitions. Provides methods to register and retrieve definitions by name or file extension.

**Public Static Properties:**

*   `Instance`: `HighlightingManager` (readonly) - Gets the default HighlightingManager instance with built-in highlightings.

**Public Properties:**

*   `HighlightingDefinitions`: `ReadOnlyCollection<IHighlightingDefinition>` (readonly) - Gets a collection of all registered highlighting definitions.

**Public Methods:**

*   `GetDefinition(string name)`: `IHighlightingDefinition` - Gets a definition by name (returns null if not found).
*   `GetDefinitionByExtension(string extension)`: `IHighlightingDefinition` - Gets a definition by file extension (returns null if not found).
*   `RegisterHighlighting(string name, string[] extensions, IHighlightingDefinition highlighting)`: Registers a loaded highlighting definition.
*   `RegisterHighlighting(string name, string[] extensions, Func<IHighlightingDefinition> lazyLoadedHighlighting)`: Registers a highlighting definition that is loaded on demand.

**Internal Insights:**

*   The default instance (`HighlightingManager.Instance`) uses lazy loading for built-in definitions defined in XSHD resources.
*   Definitions are stored in dictionaries for lookup by name and extension.
*   Implements `IHighlightingDefinitionReferenceResolver` to allow XSHD definitions to reference rulesets or colors in other definitions (though cross-definition references need careful handling, especially with lazy loading).

### Interface: `IHighlightingDefinition`

**Summary:** Represents a syntax highlighting definition.

**Public Properties:**

*   `Name`: `string` (readonly) - The name of the highlighting definition.
*   `MainRuleSet`: `HighlightingRuleSet` (readonly) - The main rule set for the language.
*   `NamedHighlightingColors`: `IEnumerable<HighlightingColor>` (readonly) - Gets all named highlighting colors defined.
*   `Properties`: `IDictionary<string, string>` (readonly) - Gets key/value pairs defined in the XSHD file.

**Public Methods:**

*   `GetNamedRuleSet(string name)`: `HighlightingRuleSet` - Gets a rule set by name within this definition (null if not found).
*   `GetNamedColor(string name)`: `HighlightingColor` - Gets a highlighting color by name within this definition (null if not found).

**Internal Insights:**

*   Typically implemented by loading an XSHD file (`XmlHighlightingDefinition`).
*   Defines the structure (rulesets, spans, keywords, colors) used by the `HighlightingEngine`.

### Class: `HighlightingColorizer`

Inherits from `DocumentColorizingTransformer`.

**Summary:** An `IVisualLineTransformer` that applies syntax highlighting based on an `IHighlightingDefinition` or an `IHighlighter`.

**Internal Insights:**

*   Uses an `IHighlighter` (usually `DocumentHighlighter`) internally to get highlighting information for each line.
*   Applies colors and font styles to `VisualLineElement`s by splitting them as needed.
*   Manages the lifecycle of the `IHighlighter` instance, creating one if only a definition is provided.
*   Connects to `TextView` events to manage the highlighter and trigger redraws when highlighting state changes propagate (`OnHighlightStateChanged`).

### Class: `DocumentHighlighter`

Implements `ILineTracker`, `IHighlighter`.

**Summary:** Performs syntax highlighting for a `TextDocument` based on an `IHighlightingDefinition`. Manages the highlighting state (span stack) across lines.

**Public Properties:**

*   `Document`: `IDocument` (readonly) - The document being highlighted.
*   `InitialSpanStack`: `ImmutableStack<HighlightingSpan>` - Gets/Sets the initial span stack state at the beginning of the document.

**Public Methods:**

*   `HighlightLine(int lineNumber)`: `HighlightedLine` - Highlights a single line and updates the internal state.
*   `GetSpanStack(int lineNumber)`: `ImmutableStack<HighlightingSpan>` - Gets the span stack state at the *end* of the specified line number (0 returns initial state).
*   `GetColorStack(int lineNumber)`: `IEnumerable<HighlightingColor>` - Gets the stack of active colors at the end of the specified line.
*   `UpdateHighlightingState(int lineNumber)`: Forces the highlighter to process lines up to the specified line number.
*   `InvalidateHighlighting()`: Invalidates all cached highlighting information, forcing a re-highlight when needed.
*   `BeginHighlighting()` / `EndHighlighting()`: Can optimize performance for multiple `HighlightLine` calls.
*   `GetNamedColor(string name)`: `HighlightingColor` - Retrieves a named color from the definition.

**Public Events:**

*   `HighlightingStateChanged`: `HighlightingStateChangedEventHandler` - Raised when the highlighting state at the end of a line changes.

**Internal Insights:**

*   **Engine:** Uses an internal `HighlightingEngine` which processes rulesets based on the current `SpanStack`.
*   **State Management:** Stores the `SpanStack` at the end of each line using a `CompressingTreeList` for efficiency. This allows incremental updates. Only lines after a change or state invalidation need to be re-processed.
*   **On-Demand Highlighting:** Highlighting is typically performed only for the lines needed for rendering or explicitly requested via `UpdateHighlightingState` or `GetSpanStack`.
*   **Invalidation:** Implements `ILineTracker` to automatically invalidate highlighting state when the document changes.

### Class: `HighlightedLine`

**Summary:** Represents a document line with its calculated highlighting sections.

**Public Properties:**

*   `Document`: `IDocument` (readonly) - The document containing the line.
*   `DocumentLine`: `IDocumentLine` (readonly) - The corresponding document line.
*   `Sections`: `IList<HighlightedSection>` (readonly) - The list of highlighted sections, sorted by offset, potentially nested.

**Public Methods:**

*   `ToHtml(...)`: `string` - Generates an HTML representation of the line or a part of it.
*   `ToRichText()`: `RichText` - Creates a `RichText` object representing the line.
*   `ToRichTextModel()`: `RichTextModel` - Creates a `RichTextModel` with the highlighting information.

**Internal Insights:**

*   Produced by `IHighlighter.HighlightLine`.
*   `MergeWith` method allows combining highlighting information from multiple sources (e.g., syntax highlighting and semantic highlighting).

### Static Class: `HighlightingLoader`

**Summary:** Provides static methods to load XSHD (XML Syntax Highlighting Definition) files.

**Public Methods:**

*   `LoadXshd(XmlReader reader)`: `XshdSyntaxDefinition` - Loads the XSHD syntax definition model from XML. Handles both V1 and V2 formats.
*   `Load(XshdSyntaxDefinition syntaxDefinition, IHighlightingDefinitionReferenceResolver resolver)`: `IHighlightingDefinition` - Compiles an `XshdSyntaxDefinition` model into a usable `IHighlightingDefinition` (an `XmlHighlightingDefinition`).
*   `Load(XmlReader reader, IHighlightingDefinitionReferenceResolver resolver)`: `IHighlightingDefinition` - Loads and compiles an XSHD file directly from an `XmlReader`.

**Internal Insights:**

*   Uses V1Loader or V2Loader internally based on the XML namespace.
*   Provides XML validation against schemas (can be skipped).

## Namespace: `ICSharpCode.AvalonEdit.Folding`

### Class: `FoldingManager`

Implements `IWeakEventListener`.

**Summary:** Manages a collection of `FoldingSection`s for a `TextDocument`. It interacts with `TextView`s to handle the visual collapsing of lines.

**Public Static Methods:**

*   `Install(TextArea textArea)`: `FoldingManager` - Creates and attaches a `FoldingManager` to a `TextArea`, adding necessary UI elements (like `FoldingMargin`).
*   `Uninstall(FoldingManager manager)`: Removes a `FoldingManager` installed via `Install`.

**Public Properties:**

*   `AllFoldings`: `IEnumerable<FoldingSection>` (readonly) - Gets all currently managed foldings.

**Public Methods:**

*   `CreateFolding(int startOffset, int endOffset)`: `FoldingSection` - Creates a new folding section.
*   `RemoveFolding(FoldingSection fs)`: Removes a specific folding section.
*   `Clear()`: Removes all folding sections.
*   `GetNextFoldedFoldingStart(int startOffset)`: `int` - Finds the start offset of the next folded section at or after `startOffset`.
*   `GetNextFolding(int startOffset)`: `FoldingSection` - Gets the next folding section starting at or after `startOffset`.
*   `GetFoldingsAt(int startOffset)`: `ReadOnlyCollection<FoldingSection>` - Gets all foldings starting exactly at `startOffset`.
*   `GetFoldingsContaining(int offset)`: `ReadOnlyCollection<FoldingSection>` - Gets all foldings that contain the given offset.
*   `UpdateFoldings(IEnumerable<NewFolding> newFoldings, int firstErrorOffset)`: Updates the managed foldings based on a new list, trying to preserve the collapsed state of existing foldings.

**Internal Insights:**

*   Uses a `TextSegmentCollection<FoldingSection>` internally to store and manage foldings efficiently.
*   Updates folding section offsets automatically when the document changes (via `IWeakEventListener` on `TextDocument.Changed`).
*   Manages `CollapsedLineSection` objects in the `HeightTree` for each attached `TextView` when a `FoldingSection`'s `IsFolded` property is set to true.
*   The `Install` method creates a `FoldingManagerInstallation` derived class which also sets up the `FoldingMargin` and `FoldingElementGenerator`.

### Class: `FoldingSection`

Inherits from `TextSegment`.

**Summary:** Represents a region in the document that can be folded.

**Public Properties:**

*   `IsFolded`: `bool` - Gets/Sets whether this section is currently folded (collapsed).
*   `Title`: `string` - Gets/Sets the text displayed when the section is folded (defaults to "...").
*   `TextContent`: `string` (readonly) - Gets the text content of the section.
*   `Tag`: `object` - Gets/Sets an arbitrary object associated with this folding section.

**Internal Insights:**

*   Created via `FoldingManager.CreateFolding`.
*   Setting `IsFolded` triggers the creation or removal of `CollapsedLineSection`s in the `HeightTree` via the `FoldingManager`.
*   `OnSegmentChanged` override ensures that `CollapsedLineSection`s are updated if the folding's start/end offsets change.

### Abstract Class: (Strategies like `XmlFoldingStrategy`, `BraceFoldingStrategy`)

**Summary:** Provide logic to detect foldable regions in a document and generate `NewFolding` objects for `FoldingManager.UpdateFoldings`.

**Public Methods (example):**

*   `UpdateFoldings(FoldingManager manager, TextDocument document)`: Generates foldings and updates the manager.
*   `CreateNewFoldings(TextDocument document, out int firstErrorOffset)` / `CreateNewFoldings(ITextSource document)`: `IEnumerable<NewFolding>` - Generates the list of potential foldings.

### Class: `FoldingMargin`

Inherits from `AbstractMargin`.

**Summary:** A UI element that displays markers ('+' or '-') for `FoldingSection`s and allows users to collapse/expand them by clicking.

**Public Properties:**

*   `FoldingManager`: `FoldingManager` - The manager providing the foldings to display.
*   `FoldingMarkerBrush`, `FoldingMarkerBackgroundBrush`, `SelectedFoldingMarkerBrush`, `SelectedFoldingMarkerBackgroundBrush`: `Brush` - Attached dependency properties (and instance properties) to control marker appearance.

**Internal Insights:**

*   Renders itself by drawing lines connecting the start and end of foldings and drawing interactive markers (`FoldingMarginMarker`) at the start of each fold that begins on a visible line.
*   Uses the `TextView.VisualLines` and the `FoldingManager` to determine what to draw.
*   Handles mouse clicks on markers to toggle the `IsFolded` state of the corresponding `FoldingSection`.

## Namespace: `ICSharpCode.AvalonEdit.Search`

### Class: `SearchPanel`

Inherits from `System.Windows.Controls.Control`.

**Summary:** A UI control providing a search interface (search box, options, next/previous buttons) typically displayed as an adorner over the `TextArea`.

**Public Static Methods:**

*   `Install(TextEditor editor)` / `Install(TextArea textArea)`: `SearchPanel` - Creates and installs the search panel onto a `TextEditor` or `TextArea`.

**Public Properties:**

*   `UseRegex`, `MatchCase`, `WholeWords`: `bool` - Search options (Dependency Properties).
*   `SearchPattern`: `string` - The text or regex to search for (Dependency Property).
*   `MarkerBrush`: `Brush`, `MarkerPen`: `Pen`, `MarkerCornerRadius`: `double` - Appearance options for highlighting search results (Dependency Properties).
*   `Localization`: `Localization` - Allows customizing UI text (Dependency Property).
*   `IsClosed`: `bool` (readonly) - Gets whether the panel is currently closed (not visible).

**Public Methods:**

*   `Open()`: Opens the search panel.
*   `Close()`: Closes the search panel.
*   `Reactivate()`: Sets focus to the search text box and selects its content.
*   `FindNext()`: Finds and selects the next search result.
*   `FindPrevious()`: Finds and selects the previous search result.
*   `Uninstall()`: Removes the search panel and its input handler.
*   `RegisterCommands(CommandBindingCollection commandBindings)`: Adds search commands to a collection (usually for the main window).

**Public Events:**

*   `SearchOptionsChanged`: `EventHandler<SearchOptionsChangedEventArgs>` - Fired when search options (`MatchCase`, `UseRegex`, etc.) change.

**Internal Insights:**

*   Installed via `SearchPanel.Install`, which creates a `SearchPanelAdorner` to host the panel and adds a `SearchResultBackgroundRenderer` to the `TextView`.
*   Uses an `ISearchStrategy` (created via `SearchStrategyFactory`) based on the current options to find matches.
*   `SearchResultBackgroundRenderer` uses a `TextSegmentCollection` to efficiently store and display result highlighting.
*   Includes a `SearchInputHandler` that handles keyboard shortcuts (F3, Shift+F3, Esc).

### Static Class: `SearchCommands`

**Summary:** Defines routed commands related to the search panel.

**Public Commands:**

*   `FindNext` (F3)
*   `FindPrevious` (Shift+F3)
*   `CloseSearchPanel` (Esc)

### Interface: `ISearchStrategy`

Inherits from `System.IEquatable<ISearchStrategy>`.

**Summary:** Defines the interface for search algorithms.

**Public Methods:**

*   `FindAll(ITextSource document, int offset, int length)`: `IEnumerable<ISearchResult>` - Finds all occurrences within a range. Must be thread-safe.
*   `FindNext(ITextSource document, int offset, int length)`: `ISearchResult` - Finds the next occurrence within a range. Must be thread-safe.

### Interface: `ISearchResult`

Inherits from `ISegment`.

**Summary:** Represents a single search result (a matched segment).

**Public Methods:**

*   `ReplaceWith(string replacement)`: `string` - Performs replacement, handling substitutions like $1 (used in regex replacements).

**Internal Insights:**

*   `SearchStrategyFactory` creates the appropriate `ISearchStrategy` (e.g., `RegexSearchStrategy`) based on options.
*   `RegexSearchStrategy` uses `System.Text.RegularExpressions.Regex` for matching.
*   `SearchResult` implements `ISearchResult` for regex matches.

## Namespace: `ICSharpCode.AvalonEdit.Snippets`

### Class: `Snippet`

Inherits from `SnippetContainerElement`.

**Summary:** Represents a code snippet that can be inserted into the editor, potentially containing interactive elements like replaceable fields or caret placeholders.

**Public Methods:**

*   `Insert(TextArea textArea)`: Inserts the snippet into the specified `TextArea`.

**Internal Insights:**

*   The `Insert` method creates an `InsertionContext` and then calls the `Insert` method of the root `SnippetContainerElement`.

### Class: `SnippetContainerElement`

Inherits from `SnippetElement`.

**Summary:** A snippet element that can contain other snippet elements. Base class for `Snippet`.

**Public Properties:**

*   `Elements`: `IList<SnippetElement>` (readonly) - Gets the list of child elements.

**Internal Insights:**

*   Its `Insert` method simply iterates through the child `Elements` and calls their `Insert` methods sequentially within the same `InsertionContext`.

### Abstract Class: `SnippetElement`

**Summary:** Abstract base class for all elements within a snippet (text, replaceable fields, anchors, etc.).

**Public Methods:**

*   `Insert(InsertionContext context)`: Abstract method to perform the insertion logic for this element.
*   `ToTextRun()`: `Inline` - Converts the snippet element to a WPF `Inline` for display purposes (e.g., in a snippet listbox). Default implementation returns null.

### Class: `SnippetTextElement`

Inherits from `SnippetElement`.

**Summary:** Represents a simple, non-interactive piece of text within a snippet.

**Public Properties:**

*   `Text`: `string` - The text to insert.

**Internal Insights:**

*   Its `Insert` method calls `context.InsertText`, which handles indentation and line terminators.

### Class: `SnippetReplaceableTextElement`

Inherits from `SnippetTextElement`.

**Summary:** Represents a piece of text that the user is expected to replace. It becomes an interactive element after insertion.

**Internal Insights:**

*   Its `Insert` method calls the base (`SnippetTextElement.Insert`) and then registers a `ReplaceableActiveElement` with the `InsertionContext`.

### Interface: `IActiveElement`

**Summary:** Interface for elements that remain interactive after the snippet is inserted (e.g., replaceable fields, caret position markers).

**Public Properties:**

*   `IsEditable`: `bool` (readonly) - Gets whether the element can be selected via Tab navigation.
*   `Segment`: `ISegment` (readonly) - Gets the document segment associated with this element (can be null or change over time).

**Public Methods:**

*   `OnInsertionCompleted()`: Called once all snippet elements have been inserted and the main insertion phase is complete. Anchors should typically be created here.
*   `Deactivate(SnippetEventArgs e)`: Called when the snippet's interactive mode ends for any reason (e.g., user presses Enter/Esc, deletes the snippet).

### Class: `InsertionContext`

**Summary:** Holds the state during snippet insertion and manages the interactive mode afterwards.

**Public Properties:**

*   `TextArea`, `Document`, `SelectedText`, `Indentation`, `Tab`, `LineTerminator`: Provide context information.
*   `InsertionPosition`: `int` - The current position where text is being inserted. Updated by `InsertText`.
*   `StartPosition`: `int` (readonly) - The offset where the snippet insertion began.
*   `ActiveElements`: `IEnumerable<IActiveElement>` (readonly) - Gets the list of registered active elements.

**Public Methods:**

*   `InsertText(string text)`: Inserts text, handling indentation and line endings, and updates `InsertionPosition`.
*   `RegisterActiveElement(SnippetElement owner, IActiveElement element)`: Registers an active element created by a snippet element.
*   `GetActiveElement(SnippetElement owner)`: `IActiveElement` - Retrieves the active element corresponding to a snippet element.
*   `RaiseInsertionCompleted(EventArgs e)`: Finalizes the insertion phase, calls `OnInsertionCompleted` on active elements, and potentially starts interactive mode via `SnippetInputHandler`.
*   `Deactivate(SnippetEventArgs e)`: Ends interactive mode and calls `Deactivate` on active elements.
*   `Link(ISegment mainElement, ISegment[] boundElements)`: Helper method to create a main replaceable element and bind other segments to it.

**Public Events:**

*   `InsertionCompleted`: `EventHandler` - Raised after insertion and `OnInsertionCompleted` calls are finished.
*   `Deactivated`: `EventHandler<SnippetEventArgs>` - Raised when interactive mode ends.

**Internal Insights:**

*   Manages the lifecycle (Insertion -> RaisingInsertionCompleted -> Interactive -> RaisingDeactivated -> Deactivated).
*   Registers a `SnippetInputHandler` when entering interactive mode to handle Tab, Enter, and Esc keys.
*   Uses `TextAnchor`s internally (e.g., `wholeSnippetAnchor`) and listens for document changes via `IWeakEventListener` to detect deletion.

### Class: `SnippetCaretElement`

Inherits from `SnippetElement`.

**Summary:** Marks the final caret position after snippet insertion and deactivation (unless deactivated by Esc or deletion).

**Internal Insights:**

*   Its `Insert` method calls the static `SetCaret` helper, which creates a `TextAnchor` and registers a handler on the context's `Deactivated` event to set the caret position if the deactivation reason is suitable (Return or NoActiveElements).

### Class: `SnippetSelectionElement`

Inherits from `SnippetElement`.

**Summary:** Inserts the text that was selected *before* the snippet insertion began, optionally re-indenting it.

**Public Properties:**

*   `Indentation`: `int` - The number of *additional* indentation levels to apply to the inserted text.

**Internal Insights:**

*   Uses `context.SelectedText`.
*   Replaces line endings and adds indentation to each line before inserting.

### Class: `SnippetBoundElement`

Inherits from `SnippetElement`.

**Summary:** Creates an interactive element whose text content mirrors a target `SnippetReplaceableTextElement`.

**Public Properties:**

*   `TargetElement`: `SnippetReplaceableTextElement` - The element whose text this element should reflect.

**Public Methods:**

*   `ConvertText(string input)`: `string` - Virtual method to allow derived classes to transform the text before displaying it (e.g., change case).

**Internal Insights:**

*   Registers a `BoundActiveElement` during insertion.
*   `BoundActiveElement` listens to the `TextChanged` event of the target `IReplaceableActiveElement` and updates its own segment's text in the document, applying the `ConvertText` transformation.