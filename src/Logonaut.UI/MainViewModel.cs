using System.Collections.ObjectModel;
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
                if (SelectedFilter.FilterModel is CompositeFilterBase compositeFilter)
                {
                    SelectedFilter.AddChildFilter(filter);
                }
                else
                {
                    // Popup an error message
                    System.Windows.MessageBox.Show("Selected filter is not a composite filter.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                // TODO: handle cases where the selected filter isn't composite.
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

        // This is a hack to force 'CanExecute' to be run again.
        partial void OnSelectedFilterChanged(FilterViewModel? value)
        {
            RemoveFilterCommand.NotifyCanExecuteChanged();
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
                        currentFilter = new NeutralFilter();
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
    }

    // A neutral filter that always returns true.
    public class NeutralFilter : IFilter
    {
        public bool Enabled { get; set; } = true;
        public bool IsMatch(string line) => true;
    }
}
