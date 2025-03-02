using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.UI.Services;
using Logonaut.LogTailing;

namespace Logonaut.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // Add a property to hold the ThemeViewModel.
        public ThemeViewModel Theme { get; } = new ThemeViewModel();
 
        // Automatically generates public LogText { get; set; } with INotifyPropertyChanged support.
        [ObservableProperty]
        private string logText = "";

        [ObservableProperty]
        private string searchText = "";

        [ObservableProperty]
        private string? currentLogFilePath;

        public ObservableCollection<string> FilterProfiles { get; } = new ObservableCollection<string>();

        private readonly IFileDialogService _fileDialogService;

        public MainViewModel(IFileDialogService? fileDialogService = null)
        {
            _fileDialogService = fileDialogService ?? new FileDialogService();

            // Subscribe to log lines from the tailer manager.
            LogTailerManager.Instance.LogLines.Subscribe(line =>
            {
                // Consider marshaling to the UI thread if necessary.
                LogText += line + "\n";
            });
        }

        // Generates AddFilterCommand automatically.
        [RelayCommand]
        private void AddFilter()
        {
            FilterProfiles.Add("Filter " + (FilterProfiles.Count + 1));
        }

        [RelayCommand(CanExecute = nameof(CanRemoveFilter))]
        private void RemoveFilter()
        {
            if (FilterProfiles.Count > 0)
            {
                FilterProfiles.RemoveAt(FilterProfiles.Count - 1);
            }
        }

        private bool CanRemoveFilter() => FilterProfiles.Count > 0;

        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void PreviousSearch()
        {
            LogText += "Previous search executed.\n";
        }

        [RelayCommand(CanExecute = nameof(CanSearch))]
        private void NextSearch()
        {
            LogText += "Next search executed.\n";
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
            // LogText += $"Now monitoring: {selectedFile}\n";
        }
    }
}
