using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.UI.Services;
using Logonaut.LogTailing;
using Logonaut.Filters;

namespace Logonaut.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public ThemeViewModel Theme { get; } = new ThemeViewModel();

        // Automatically generates public LogText { get; set; } with INotifyPropertyChanged support.
        [ObservableProperty]
        private string logText = "";

        [ObservableProperty]
        private string searchText = "";

        [ObservableProperty]
        private string? currentLogFilePath;

        // Now a collection of FilterViewModel.
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
                LogText += line + "\n";
            });
        }

        // Generates AddFilterCommand automatically.
        [RelayCommand]
        private void AddFilter()
        {
            if (FilterProfiles.Count == 0)
            {
                // Create a new filter as the root.
                var newFilterModel = new SubstringFilter("New Filter");
                var newFilterVM = new FilterViewModel(newFilterModel);
                FilterProfiles.Add(newFilterVM);
                SelectedFilter = newFilterVM;
            }
            else if (SelectedFilter != null)
            {
                // TODO: If the selected filter isn't composite, we should disable the AddFilter button.
                if (SelectedFilter.FilterModel is CompositeFilterBase compositeFilter)
                {
                    var childFilterModel = new SubstringFilter("New Child Filter");
                    SelectedFilter.AddChildFilter(childFilterModel);
                }
                else
                {
                    // Popup an error message
                    System.Windows.MessageBox.Show("Selected filter is not a composite filter.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                // Optionally, handle the case where the selected filter is not composite.
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
        private void PreviousSearch() => LogText += "Previous search executed.\n";

        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void NextSearch() => LogText += "Next search executed.\n";

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
