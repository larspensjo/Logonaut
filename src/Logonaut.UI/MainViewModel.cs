using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.UI.Services;
using Logonaut.LogTailing;
using Logonaut.Filters;
using Logonaut.Common;

namespace Logonaut.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public ThemeViewModel Theme { get; } = new();

        // The complete log text.
        public LogDocument LogDoc { get; } = new();

        // The visible log text, filtered by the selected filters.
        public ObservableCollection<string> VisibleLogLines { get; } = new();

        [ObservableProperty]
        private string searchText = "";

        [ObservableProperty]
        private string? currentLogFilePath;

        public ObservableCollection<FilterViewModel> FilterProfiles { get; } = new();

        // The currently selected filter in the tree.
        [ObservableProperty]
        private FilterViewModel? selectedFilter;

        private readonly IFileDialogService _fileDialogService;

        public MainViewModel(IFileDialogService? fileDialogService = null)
        {
            _fileDialogService = fileDialogService ?? new FileDialogService();

            // Subscribe to log lines from the tailer manager.
            LogTailerManager.Instance.LogLines.Subscribe(line =>
            {
                LogDoc.AppendLine(line);
            });
        }

        // Separate commands for adding different types of filters.
        [RelayCommand]
        private void AddSubstringFilter()
        {
            AddFilter(new SubstringFilter("New Substring Filter"));
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
    }
}
