using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.UI.Services;
using Logonaut.LogTailing;
using Logonaut.Filters;
using Logonaut.Common;
using Logonaut.Core;

namespace Logonaut.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public ThemeViewModel Theme { get; } = new();

        // The complete log text. This will only be used as an input to the filter task.
        public LogDocument LogDoc { get; } = new();

        // The filtered text. It will be used as an input to the LogText, used by Avalon.
        // TODO: Rename VisibleLogLines to FilteredLogLines.
        public ObservableCollection<string> VisibleLogLines { get; } = new();

        // Property for binding to AvalonEdit
        [ObservableProperty]
        private string logText = string.Empty;

        [ObservableProperty]
        private string searchText = "";

        [ObservableProperty]
        private string? currentLogFilePath;

        public ObservableCollection<FilterViewModel> FilterProfiles { get; } = new();

        // The currently selected filter in the tree.
        [ObservableProperty]
        private FilterViewModel? selectedFilter;

        private readonly IFileDialogService _fileDialogService;

        // signal cancellation requests for the background filtering task
        private CancellationTokenSource? _cts;

        public MainViewModel(IFileDialogService? fileDialogService = null)
        {
            _fileDialogService = fileDialogService ?? new FileDialogService();

            // Subscribe to log lines from the tailer manager.
            LogTailerManager.Instance.LogLines.Subscribe(line =>
            {
                LogDoc.AppendLine(line);
                // No need to update LogText here, it will be updated by the background filtering task
            });
            StartBackgroundFiltering();
        }

        // Method to update the LogText property from the filtered log lines
        private void UpdateLogText()
        {
            // Always use the filtered content from VisibleLogLines
            LogText = string.Join(Environment.NewLine, VisibleLogLines);
        }

        // Separate commands for adding different types of filters.
        [RelayCommand]
        private void AddSubstringFilter()
        {
            AddFilter(new SubstringFilter(""));
        }

        [RelayCommand]
        private void AddRegexFilter()
        {
            AddFilter(new RegexFilter(".*"));
        }

        [RelayCommand]
        private void AddAndFilter()
        {
            AddFilter(new AndFilter());
        }

        [RelayCommand]
        private void AddOrFilter()
        {
            AddFilter(new OrFilter());
        }

        [RelayCommand]
        private void AddNegationFilter()
        {
            // TODO: Should not be hard coded here.
            AddFilter(new NegationFilter(new SubstringFilter("Not this")));
        }

        private void AddFilter(IFilter filter)
        {
            var newFilterVM = new FilterViewModel(filter);

            if (FilterProfiles.Count == 0)
            {
                // If no filter exists, make this the root.
                FilterProfiles.Add(newFilterVM);
                SelectedFilter = newFilterVM;
            }
            else if (SelectedFilter != null)
            {
                // Add to the currently selected composite filter.
                if (SelectedFilter.FilterModel is CompositeFilter compositeFilter)
                {
                    SelectedFilter.AddChildFilter(filter);
                }
                else
                {
                    // Popup an error message
                    System.Windows.MessageBox.Show("Selected filter is not a composite filter.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanRemoveFilter))]
        private void RemoveFilter()
        {
            if (SelectedFilter == null)
                throw new InvalidOperationException("No filter is selected.");

            if (FilterProfiles.Contains(SelectedFilter))
            {
                // Removing a root filter.
                FilterProfiles.Remove(SelectedFilter);
                SelectedFilter = null;
            }
            else if (SelectedFilter.Parent != null)
            {
                // Removing a child filter.
                SelectedFilter.Parent.RemoveChild(SelectedFilter);
            }
            UpdateFilterSubstrings();
        }
        private bool CanToggleEdit() => SelectedFilter != null && SelectedFilter.IsEditable;

        // TODO: Use CanToggleEdit conditionally to enable/disable the button.
        [RelayCommand]
        private void ToggleEdit()
        {
            if (SelectedFilter != null && SelectedFilter.IsEditable)
            {
                if (SelectedFilter.IsNotEditing)
                    SelectedFilter.BeginEdit();
                else
                    SelectedFilter.EndEdit();
            }
        }

        private FilterViewModel? _previousSelectedFilter;
        // This is a hack to force 'CanExecute' to be run again.
        partial void OnSelectedFilterChanged(FilterViewModel? value)
        {
            RemoveFilterCommand.NotifyCanExecuteChanged();
            var x = value?.IsSelected;
    
            // If there was a previously selected filter, set its IsSelected to false
            if (_previousSelectedFilter != null && _previousSelectedFilter != value)
                _previousSelectedFilter.IsSelected = false;
            
            // If there's a new selected filter, set its IsSelected to true
            if (value != null)
                value.IsSelected = true;
            _previousSelectedFilter = value;
        }

        private bool CanRemoveFilter() => SelectedFilter != null;

        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void PreviousSearch() => LogDoc.AppendLine("Previous search executed.\n");

        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void NextSearch() => LogDoc.AppendLine("Next search executed.\n");

        // This is a hack to force 'CanExecute' to be run again.
        partial void OnSearchTextChanged(string value)
        {
            PreviousSearchCommand.NotifyCanExecuteChanged();
            NextSearchCommand.NotifyCanExecuteChanged();
        }
        private bool CanSearch() => !string.IsNullOrWhiteSpace(SearchText);

        [RelayCommand]
        private void OpenLogFile()
        {
            string? selectedFile = _fileDialogService.OpenFile("Select a log file", "Log Files|*.log;*.txt|All Files|*.*");
            if (string.IsNullOrEmpty(selectedFile))
                return;
            CurrentLogFilePath = selectedFile;
            LogTailerManager.Instance.ChangeFile(selectedFile);
        }

        private void StartBackgroundFiltering()
        {
            _cts = new CancellationTokenSource();
            // Run the filtering loop on a background thread.
            Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    IFilter currentFilter;
                    if (FilterProfiles.Count == 0)
                        currentFilter = new TrueFilter();
                    else
                        currentFilter = FilterProfiles.First().FilterModel;

                    // Apply filtering. You might include a contextLines parameter.
                    var newFilteredLines = FilterEngine.ApplyFilters(LogDoc, currentFilter, contextLines: 1);

                    // Update the VisibleLogLines on the UI thread.
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        VisibleLogLines.Clear();
                        foreach (var line in newFilteredLines)
                        {
                            VisibleLogLines.Add(line);
                        }
                        
                        // Update the LogText property after filtering
                        UpdateLogText();
                    });

                    // Wait a bit before next update (throttling)
                    await Task.Delay(250, _cts.Token);
                }
            }, _cts.Token);
        }

        public void StopBackgroundFiltering()
        {
            _cts?.Cancel();
        }

        [ObservableProperty]
        private bool showLineNumbers = true;

        [ObservableProperty]
        private bool highlightTimestamps = true;
        
        [RelayCommand]
        private void UpdateFilterSubstrings()
        {
            // Create a new collection instead of clearing the existing one
            var newFilterSubstrings = new ObservableCollection<string>();

            // Traverse the filter tree and collect substrings
            if (FilterProfiles.Count > 0)
            {
                TraverseFilterTree(FilterProfiles[0]);
            }

            // Set the property to the new collection to trigger the dependency property change
            FilterSubstrings = newFilterSubstrings;

            void TraverseFilterTree(FilterViewModel filterViewModel)
            {
                // TODO: Ask the filter itself if it has a substring.
                if (filterViewModel.FilterModel is SubstringFilter substringFilter)
                {
                    // Ignore empty strings, they can't be used for highlighting
                    if (substringFilter.Substring != "")
                        newFilterSubstrings.Add(Regex.Escape(substringFilter.Substring)); // Escape for regex
                }
                else if (filterViewModel.FilterModel is RegexFilter regexFilter)
                {
                    // Ignore empty patterns
                    if (!string.IsNullOrEmpty(regexFilter.Pattern))
                        newFilterSubstrings.Add(regexFilter.Pattern); // Use directly as regex
                }

                foreach (var childFilter in filterViewModel.Children)
                {
                    TraverseFilterTree(childFilter);
                }
            }
        }

        private ObservableCollection<string> _filterSubstrings = new();
        public ObservableCollection<string> FilterSubstrings
        {
            get => _filterSubstrings;
            set => SetProperty(ref _filterSubstrings, value);
        }
    }
}
