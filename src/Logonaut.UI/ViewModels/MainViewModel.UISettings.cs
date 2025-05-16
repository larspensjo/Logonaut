using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.UI.Commands;

namespace Logonaut.UI.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{
    [ObservableProperty] private int _contextLines = 0;
    [NotifyPropertyChangedFor(nameof(IsCustomLineNumberMarginVisible))]
    [ObservableProperty] private bool _showLineNumbers = true;

    // Controls whether timestamp highlighting rules are applied in AvalonEdit.
    [ObservableProperty] private bool _highlightTimestamps = true;
    [ObservableProperty] private bool _isAutoScrollEnabled = true;

    [RelayCommand(CanExecute = nameof(CanDecrementContextLines))]
    private void DecrementContextLines() => ContextLines = Math.Max(0, ContextLines - 1);
    private bool CanDecrementContextLines() => ContextLines > 0;

    [RelayCommand] private void IncrementContextLines() => ContextLines++;

    // When global ContextLines changes, the active tab's filter stream needs to be updated.
    partial void OnContextLinesChanged(int value)
    {
        DecrementContextLinesCommand.NotifyCanExecuteChanged();
        // TriggerFilterUpdate will pick up the new ContextLines value and pass it
        // to _internalTabViewModel.ApplyFiltersFromProfile
        TriggerFilterUpdate(); 
        SaveCurrentSettingsDelayed();
    }
    public Visibility IsCustomLineNumberMarginVisible => ShowLineNumbers ? Visibility.Visible : Visibility.Collapsed;

    // When these global UI settings change, the active tab might need to re-render or re-filter.
    // HighlightTimestamps changes AvalonEdit behavior via binding.
    // ShowLineNumbers changes margin visibility via binding.
    // IsAutoScrollEnabled directly affects behavior.
    partial void OnShowLineNumbersChanged(bool value) 
    {
        // If _internalTabViewModel's ActivateAsync depends on this, it might need re-activation or specific update call.
        // For now, assuming bindings in AvalonEditHelper handle this.
        SaveCurrentSettingsDelayed();
    }
    partial void OnHighlightTimestampsChanged(bool value) 
    {
        // Similar to ShowLineNumbers, AvalonEditHelper binding should react.
        // If a re-filter/re-render of content is needed:
        // TriggerFilterUpdate(); // Could be too heavy, but ensures consistency.
        SaveCurrentSettingsDelayed();
    }

    partial void OnIsAutoScrollEnabledChanged(bool value)
    {
        // Logic for HighlightedFilteredLineIndex if auto-scroll enabled is now in TabViewModel.
        // MainViewModel might need to inform TabViewModel if this global setting changes.
        // For Phase 0.1, TabViewModel doesn't have its own IsAutoScrollEnabled.
        // The RequestScrollToEnd event on TabViewModel will be conditional on this global setting.
        if (value == true && HighlightedOriginalLineNumber != -1) // Use original line number for consistency
        {
            // If auto-scroll is re-enabled, clear any persistent highlight to allow scrolling to end.
            HighlightedOriginalLineNumber = -1; 
        }
        if (value == true) RequestGlobalScrollToEnd?.Invoke(this, EventArgs.Empty);
        SaveCurrentSettingsDelayed();
    }

    private void LoadUiSettings(LogonautSettings settings)
    {
        ContextLines = settings.ContextLines;
        ShowLineNumbers = settings.ShowLineNumbers;
        HighlightTimestamps = settings.HighlightTimestamps;
        IsAutoScrollEnabled = settings.AutoScrollToTail;
        // IsCaseSensitiveSearch is now part of TabViewModel, but the global default can be loaded here.
        // For Phase 0.1, MainViewModel.IsCaseSensitiveSearch acts as the source for _internalTabViewModel.
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
