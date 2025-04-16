# Improved unit testing

Okay, let's analyze the Logonaut project structure and identify areas where unit testing could be expanded to increase confidence before refactoring.

You have a well-structured application with good separation of concerns, which is excellent for testability. Here's a breakdown of potential areas for improved unit testing, focusing on logic rather than direct UI interaction:

**High-Impact Areas for New Unit Tests:**

1.  **`LogFilterProcessor` (Logonaut.Core):**
    *   **Why:** This is the core engine for reactive processing. Its correctness is critical for the application's main function. It involves Rx.NET, threading, state management (`_currentFilter`, `_currentLineIndex`), and interaction with `FilterEngine` and `LogDocument`.
    *   **What to Test:**
        *   **Initialization:** Does it subscribe correctly to inputs?
        *   **Reset Logic:** Does `Reset()` clear the `LogDocument`, reset the line counter, and emit the correct `Replace` update?
        *   **Incremental Filtering:** Given a sequence of input lines, does it correctly buffer, apply the *current* filter, append to `LogDocument`, assign correct original line numbers, and emit `Append` updates with the right `FilteredLogLine` objects? (Use `TestScheduler` from Rx.NET for timing control).
        *   **Full Re-filtering:** When `UpdateFilterSettings` is called:
            *   Does it update the internal `_currentFilter` and `_currentContextLines`?
            *   Does the debouncing (`Throttle`) work as expected (e.g., multiple rapid calls only result in one execution after the pause)? (Use `TestScheduler`).
            *   Does it correctly call `FilterEngine.ApplyFilters` with the *latest* settings and the full `LogDocument` snapshot?
            *   Does it emit a `Replace` update with the complete filtered list?
        *   **Threading:** Ensure results are correctly marshalled back to the UI thread (testing this directly might involve mocking `SynchronizationContext` or verifying the `ObserveOn` operator is used).
        *   **Error Handling:** If the input observable (`_rawLogLines`) emits an error, is it handled gracefully (e.g., propagated or logged)?
        *   **Disposal:** Does `Dispose()` clean up subscriptions?
    *   **How:** Mock `ILogTailerService`, `LogDocument`, `FilterEngine` (if needed, though testing its interaction might be better). Use `TestScheduler` from `System.Reactive.Testing` to control time for buffering and debouncing tests.

2.  **`MainViewModel` (Logonaut.UI):**
    *   **Why:** This is the central orchestrator for the UI. It manages state, handles user commands, interacts with multiple services, and prepares data for the view. While some interaction relies on UI events, much of its internal logic and command execution can be unit tested.
    *   **What to Test:**
        *   **Profile Management Logic:**
            *   `CreateNewProfileCommand`: Does it generate unique names? Add the profile VM correctly? Set the new profile as active? Call `SaveCurrentSettings`?
            *   `RenameProfileCommand`: Does it handle valid renames? Detect and prevent duplicate names (case-insensitive)? Interact with `IInputPromptService` correctly? Call `SaveCurrentSettings`? (Mock `IInputPromptService`).
            *   `DeleteProfileCommand`: Does it handle removing the active profile? Select the correct next profile? Handle deleting the *last* profile (creating a new Default)? Call `SaveCurrentSettings`?
            *   `OnActiveFilterProfileChanged`: Does it correctly update `ActiveTreeRootNodes`? Call `TriggerFilterUpdate`? Clear `SelectedFilterNode`? Call `SaveCurrentSettings`?
        *   **Filter Node Management Logic:**
            *   `AddFilterCommand`: Does it add the node correctly to the root or selected composite node? Update the underlying model? Update the `FilterViewModel` hierarchy? Select the new node? Trigger edit mode if applicable? Call `SaveCurrentSettings`? Check `CanExecute` logic thoroughly.
            *   `RemoveFilterNodeCommand`: Does it remove the node from the model and VM hierarchy? Select the parent (or clear selection if root)? Call `SaveCurrentSettings`? Check `CanExecute`.
            *   `ToggleEditNodeCommand`: Does it correctly call `BeginEdit`/`EndEdit` on the `FilterViewModel`? Check `CanExecute`.
        *   **Search Logic:**
            *   `UpdateSearchMatches`: Does it correctly find all occurrences (case-sensitive/insensitive)? Update `_searchMatches`, `SearchMarkers`, `_currentSearchIndex`, `SearchStatusText`?
            *   `PreviousSearchCommand`/`NextSearchCommand`: Do they update `_currentSearchIndex` correctly (including wrap-around)? Call `SelectAndScrollToCurrentMatch`? Check `CanExecute`.
        *   **State Updates:**
            *   Changing `ContextLines`, `ShowLineNumbers`, `HighlightTimestamps`, `IsCaseSensitiveSearch`: Do these properties update correctly and trigger `TriggerFilterUpdate` (where appropriate) and `SaveCurrentSettings`?
            *   `ApplyFilteredUpdate`: Does it correctly call `AddFilteredLines` or `ReplaceFilteredLines`? Schedule `UpdateLogTextInternal`? Update `IsBusyFiltering`?
        *   **Interaction with `LogFilterProcessor`:** Does `TriggerFilterUpdate` call `_logFilterProcessor.UpdateFilterSettings` with the correct filter from the active profile? Does `OpenLogFile` call `_logFilterProcessor.Reset()`?
    *   **How:** Mock all service dependencies (`ISettingsService`, `ILogTailerService`, `IFileDialogService`, `IInputPromptService`, `ILogFilterProcessor`). Inject a test `SynchronizationContext`.

3.  **`FileSystemSettingsService` (Logonaut.Core):**
    *   **Why:** Ensures settings persistence works correctly, including creating defaults and handling potential errors.
    *   **What to Test:**
        *   `LoadSettings`: Does it correctly deserialize valid JSON? Does it return default settings if the file doesn't exist? Does it handle JSON parsing errors gracefully (e.g., return defaults)? Does it correctly apply `EnsureValidSettings` logic (e.g., create default profile if missing, fix `LastActiveProfileName`)?
        *   `SaveSettings`: Does it correctly serialize the `LogonautSettings` object to JSON, including `IFilter` polymorphism (`TypeNameHandling.All`)?
        *   File Path Logic: Does `GetSettingsFilePath` return the expected path in `%LocalAppData%`?
    *   **How:** You might need to interact with the actual file system (using temporary files/directories created and cleaned up during tests) or abstract file system operations behind an interface (`IFileSystem`) for easier mocking.

**Medium-Impact Areas:**

4.  **`CustomHighlightingDefinition` (Logonaut.UI.Helpers):**
    *   **Why:** Contains the logic for managing dynamic highlighting rules based on filters and search terms.
    *   **What to Test:**
        *   Adding/removing rules (`AddRule`, `AddTimestampPattern`, `UpdateSearchHighlighting`, `UpdateFilterHighlighting`): Does the `MainRuleSet.Rules` collection reflect the changes correctly? Are regex patterns created correctly (case sensitivity, escaping)?
        *   Color retrieval (`GetNamedColor`).
        *   Handling of invalid regex patterns during rule updates (does it skip or throw?).
    *   **How:** Test the methods directly. No WPF dependencies needed for the core logic.

5.  **`FilterViewModel`/`FilterProfileViewModel` (Logonaut.UI):**
    *   **Why:** While some tests exist, verify interactions and callbacks more thoroughly.
    *   **What to Test (`FilterViewModel`):**
        *   `Enabled` setter: Does it call `NotifyFilterConfigurationChanged`?
        *   `EndEditCommand`: Does it call `NotifyFilterConfigurationChanged`?
        *   `AddChildFilter`/`RemoveChild`: Do they call `NotifyFilterConfigurationChanged`? Do they correctly update the underlying model and the `Children` collection?
    *   **What to Test (`FilterProfileViewModel`):**
        *   Constructor: Does it correctly create the `RootFilterViewModel` hierarchy?
        *   `OnNameChanged`: Does it update the underlying `Model.Name`?
        *   `SetModelRootFilter`: Does it update the model, refresh the `RootFilterViewModel`, and call the callback?
    *   **How:** Instantiate directly, potentially passing mock actions for the callbacks to verify they are invoked.

**UI Testing Considerations:**

*   **Direct UI Unit Testing:** Testing WPF UI elements (like `OriginalLineNumberMargin`, `OverviewRulerMargin`, `MainWindow.xaml.cs` logic, or verifying `AvalonEditHelper`'s effects directly) is generally **very difficult** with standard unit testing frameworks (MSTest, xUnit, NUnit). These tests often require a running `Dispatcher`, UI thread (STA), and instantiation of controls, which unit test runners don't typically provide correctly.
*   **Testing ViewModel State:** The most effective way to "unit test" UI behavior is to test the *ViewModel state* that drives the UI. For example:
    *   Instead of testing if `OriginalLineNumberMargin` *draws* the right numbers, test if `MainViewModel.FilteredLogLines` contains the correct `FilteredLogLine` objects with the expected `OriginalLineNumber`.
    *   Instead of testing if `OverviewRulerMargin` *draws* markers, test if `MainViewModel.SearchMarkers` contains the correct `SearchResult` objects.
    *   Instead of testing if `AvalonEditHelper` correctly selects text, test if `MainViewModel.CurrentMatchOffset` and `CurrentMatchLength` are set correctly after calling `NextSearchCommand`/`PreviousSearchCommand`.
*   **UI Automation:** For true end-to-end UI testing (simulating clicks, checking visual elements), you need UI automation frameworks like:
    *   **FlaUI:** A good C# library for automating Win32, WinForms, and WPF applications.
    *   **Appium with WinAppDriver:** Uses the WebDriver protocol to automate UWP and classic Windows applications.
    These are typically separate test projects and run slower than unit tests.

**Summary & Recommendation:**

1.  **Highest Priority:** Focus on `LogFilterProcessor`. This is complex, critical, and testable with mocking and `TestScheduler`.
2.  **Next Priority:** Expand tests for `MainViewModel`, covering profile/node management commands, search state, and interactions with services (using mocks). Test `FileSystemSettingsService` for persistence correctness.
3.  **Then:** Improve coverage for `CustomHighlightingDefinition` and `FilterViewModel`/`FilterProfileViewModel` interactions/callbacks.
4.  **UI-Related:** For `AvalonEditHelper` and custom margins, focus unit tests on the `MainViewModel` state that *feeds* them. For validating the actual visual rendering or code-behind logic, consider adding separate UI Automation tests later if necessary.

Avoid testing code paths that directly involve `MessageBox.Show`. If the logic *around* the `MessageBox` call is important (e.g., checking a condition before showing it), test that condition. The `MessageBox` call itself is a UI side effect best ignored in unit tests or abstracted behind a testable service if its interaction *must* be verified.