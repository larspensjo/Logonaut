using System.Windows; // For Visibility
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.UI.Commands;

namespace Logonaut.UI.ViewModels;

// Consolidates properties and commands that directly control visual aspects and user preferences for the log display, not directly tied to filtering or searching.
public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{
    // Configured number of context lines to display around filter matches.
    [ObservableProperty] private int _contextLines = 0;

    // Controls visibility of the custom line number margin.
    [NotifyPropertyChangedFor(nameof(IsCustomLineNumberMarginVisible))]
    [ObservableProperty] private bool _showLineNumbers = true;

    // Controls whether timestamp highlighting rules are applied in AvalonEdit.
    [ObservableProperty] private bool _highlightTimestamps = true;

    [ObservableProperty] private bool _isAutoScrollEnabled = true;

    [RelayCommand(CanExecute = nameof(CanDecrementContextLines))]
    private void DecrementContextLines() => ContextLines = Math.Max(0, ContextLines - 1);
    private bool CanDecrementContextLines() => ContextLines > 0;

    [RelayCommand] private void IncrementContextLines() => ContextLines++;

    partial void OnContextLinesChanged(int value)
    {
        DecrementContextLinesCommand.NotifyCanExecuteChanged();
        TriggerFilterUpdate();
        SaveCurrentSettingsDelayed();
    }
    public Visibility IsCustomLineNumberMarginVisible => ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;

    partial void OnShowLineNumbersChanged(bool value) => SaveCurrentSettingsDelayed();
    partial void OnHighlightTimestampsChanged(bool value) => SaveCurrentSettingsDelayed();

    partial void OnIsAutoScrollEnabledChanged(bool value)
    {
        if (value == true && HighlightedFilteredLineIndex != -1)
            HighlightedFilteredLineIndex = -1;

        if (value == true)
            RequestScrollToEnd?.Invoke(this, EventArgs.Empty);

        SaveCurrentSettingsDelayed();
    }

    private void LoadUiSettings(LogonautSettings settings)
    {
        ContextLines = settings.ContextLines;
        ShowLineNumbers = settings.ShowLineNumbers;
        HighlightTimestamps = settings.HighlightTimestamps;
        IsAutoScrollEnabled = settings.AutoScrollToTail;
        IsCaseSensitiveSearch = settings.IsCaseSensitiveSearch;
    }

    private void SaveUiSettings(LogonautSettings settings)
    {
        settings.ContextLines = ContextLines;
        settings.ShowLineNumbers = ShowLineNumbers;
        settings.HighlightTimestamps = HighlightTimestamps;
        settings.AutoScrollToTail = IsAutoScrollEnabled;
        settings.IsCaseSensitiveSearch = IsCaseSensitiveSearch;
    }
}
