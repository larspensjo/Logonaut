using System;
using System.Collections.ObjectModel;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using Logonaut.UI.Services;
using Logonaut.LogTailing;

namespace Logonaut.UI.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private string _logText;
        public string LogText
        {
            get => _logText;
            set => Set(ref _logText, value);
        }

        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set 
            { 
                Set(ref _searchText, value);
                // When search text changes, update command states.
                PreviousSearchCommand.RaiseCanExecuteChanged();
                NextSearchCommand.RaiseCanExecuteChanged();
            }
        }

        private string _currentLogFilePath;
        public string CurrentLogFilePath
        {
            get => _currentLogFilePath;
            set => Set(ref _currentLogFilePath, value);
        }

        // Collection of filter profiles (saved filter trees, etc.)
        public ObservableCollection<string> FilterProfiles { get; set; }

        public RelayCommand AddFilterCommand { get; private set; }
        public RelayCommand RemoveFilterCommand { get; private set; }
        public RelayCommand PreviousSearchCommand { get; private set; }
        public RelayCommand NextSearchCommand { get; private set; }
        public RelayCommand OpenLogFileCommand { get; private set; }

        private readonly IFileDialogService _fileDialogService;

        public MainViewModel(IFileDialogService fileDialogService = null)
        {
            FilterProfiles = new ObservableCollection<string>();
            LogText = "Welcome to Logonaut!\n";
            _fileDialogService = fileDialogService ?? new FileDialogService();

            AddFilterCommand = new RelayCommand(AddFilter);
            RemoveFilterCommand = new RelayCommand(RemoveFilter, CanRemoveFilter);
            PreviousSearchCommand = new RelayCommand(PreviousSearch, CanSearch);
            NextSearchCommand = new RelayCommand(NextSearch, CanSearch);
            OpenLogFileCommand = new RelayCommand(OpenLogFile);

            // Subscribe to log lines from the tailer manager.
            LogTailerManager.Instance.LogLines.Subscribe(line =>
            {
                // Append new log lines (consider marshaling to the UI thread if necessary)
                LogText += line + "\n";
            });
        }

        private void AddFilter()
        {
            // In a real implementation, this might open a dialog or add a new filter object.
            FilterProfiles.Add("Filter " + (FilterProfiles.Count + 1));
        }

        private void RemoveFilter()
        {
            if (FilterProfiles.Count > 0)
            {
                FilterProfiles.RemoveAt(FilterProfiles.Count - 1);
            }
        }

        private bool CanRemoveFilter() => FilterProfiles.Count > 0;

        private void PreviousSearch()
        {
            // Implement logic to navigate to the previous search match.
            // For now, we just append a message.
            LogText += "Previous search executed.\n";
        }

        private void NextSearch()
        {
            LogText += "Next search executed.\n";
        }

        private bool CanSearch() => !string.IsNullOrWhiteSpace(SearchText);

        private void OpenLogFile()
        {
            // Use the file dialog service to let the user select a log file.
            var selectedFile = _fileDialogService.OpenFile("Select a log file", "Log Files|*.log;*.txt|All Files|*.*");
            if (!string.IsNullOrEmpty(selectedFile))
            {
                CurrentLogFilePath = selectedFile;
                // Reinitialize the log tailer with the selected file.
                LogTailerManager.Instance.ChangeFile(selectedFile);
                LogText += $"Now monitoring: {selectedFile}\n";
            }
        }
    }
}
