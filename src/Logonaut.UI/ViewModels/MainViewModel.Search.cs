using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Logonaut.UI.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    // --- Search State & Ruler Markers ---
    private List<SearchResult> _searchMatches = new();
    private int _currentSearchIndex = -1;
    [ObservableProperty] private ObservableCollection<SearchResult> _searchMarkers = new();

    // Text entered by the user for searching.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviousSearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextSearchCommand))]
    [NotifyPropertyChangedFor(nameof(SearchStatusText))]
    private string _searchText = "";

    // Properties for target selection in AvalonEdit
    [ObservableProperty] private int _currentMatchOffset = -1;
    [ObservableProperty] private int _currentMatchLength = 0;

    // Status text for search
    public string SearchStatusText
    {
        get
        {
            if (string.IsNullOrEmpty(SearchText)) return "";
            if (_searchMatches.Count == 0) return "Phrase not found";
            if (_currentSearchIndex == -1) return $"{_searchMatches.Count} matches found";
            return $"Match {_currentSearchIndex + 1} of {_searchMatches.Count}";
        }
    }

    // Controls whether search is case sensitive
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchStatusText))]
    private bool _isCaseSensitiveSearch = false;

    [RelayCommand(CanExecute = nameof(CanSearch))] private void PreviousSearch()
    {
        if (_searchMatches.Count == 0) return;
        if (_currentSearchIndex == -1) _currentSearchIndex = _searchMatches.Count - 1;
        else _currentSearchIndex = (_currentSearchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
        SelectAndScrollToCurrentMatch();
        OnPropertyChanged(nameof(SearchStatusText));
    }

    [RelayCommand(CanExecute = nameof(CanSearch))] private void NextSearch()
    {
        if (_searchMatches.Count == 0) return;
        _currentSearchIndex = (_currentSearchIndex + 1) % _searchMatches.Count; // Wrap around
        SelectAndScrollToCurrentMatch();
        OnPropertyChanged(nameof(SearchStatusText));
    }
    private bool CanSearch() => !string.IsNullOrWhiteSpace(SearchText);

    private void SelectAndScrollToCurrentMatch()
    {
        if (_currentSearchIndex >= 0 && _currentSearchIndex < _searchMatches.Count)
        {
            var match = _searchMatches[_currentSearchIndex];
            CurrentMatchOffset = match.Offset;
            CurrentMatchLength = match.Length;
            int lineIndex = FindFilteredLineIndexContainingOffset(CurrentMatchOffset);
            HighlightedFilteredLineIndex = lineIndex;
        }
        else
        {
            CurrentMatchOffset = -1;
            CurrentMatchLength = 0;
            HighlightedFilteredLineIndex = -1;
        }
    }

    partial void OnSearchTextChanged(string value) => UpdateSearchMatches();

    private void ResetSearchState()
    {
        _searchMatches.Clear();
        SearchMarkers.Clear();
        _currentSearchIndex = -1;
        SelectAndScrollToCurrentMatch();
    }

    // Core search logic - updates internal list and ruler markers
    private void UpdateSearchMatches()
    {
        string currentSearchTerm = SearchText;
        string textToSearch = GetCurrentDocumentText();
        ResetSearchState();
        if (string.IsNullOrEmpty(currentSearchTerm) || string.IsNullOrEmpty(textToSearch))
        {
            OnPropertyChanged(nameof(SearchStatusText));
            return;
        }

        int offset = 0;
        var tempMarkers = new List<SearchResult>();
        while (offset < textToSearch.Length)
        {
            int foundIndex = textToSearch.IndexOf(currentSearchTerm, offset, IsCaseSensitiveSearch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            if (foundIndex == -1) break;
            var newMatch = new SearchResult(foundIndex, currentSearchTerm.Length);
            _searchMatches.Add(newMatch);
            tempMarkers.Add(newMatch);
            offset = foundIndex + 1;
        }
        foreach (var marker in tempMarkers) SearchMarkers.Add(marker);
        OnPropertyChanged(nameof(SearchStatusText));
    }

    partial void OnIsCaseSensitiveSearchChanged(bool value)
    {
        UpdateSearchMatches();
        SaveCurrentSettingsDelayed();
    }
}

/// <summary>
/// Represents the position and length of a found search match within the text.
/// Used for internal tracking and for markers on the OverviewRuler.
/// </summary>
public record SearchResult(int Offset, int Length);
