using System.Collections.ObjectModel;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;

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

        // Collection of filter profiles (e.g., saved filter trees with names)
        public ObservableCollection<string> FilterProfiles { get; set; }

        public RelayCommand AddFilterCommand { get; private set; }
        public RelayCommand RemoveFilterCommand { get; private set; }
        public RelayCommand PreviousSearchCommand { get; private set; }
        public RelayCommand NextSearchCommand { get; private set; }

        public MainViewModel()
        {
            FilterProfiles = new ObservableCollection<string>();
            LogText = "Welcome to Logonaut!" + "\n";

            AddFilterCommand = new RelayCommand(AddFilter);
            RemoveFilterCommand = new RelayCommand(RemoveFilter, CanRemoveFilter);
            PreviousSearchCommand = new RelayCommand(PreviousSearch, CanSearch);
            NextSearchCommand = new RelayCommand(NextSearch, CanSearch);
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

        private bool CanRemoveFilter()
        {
            return FilterProfiles.Count > 0;
        }

        private void PreviousSearch()
        {
            // Implement logic to navigate to the previous search match.
            // For now, we just append a message.
            LogText += "Previous search executed." + "\n";
        }

        private void NextSearch()
        {
            // Implement logic to navigate to the next search match.
            LogText += "Next search executed." + "\n";
        }

        private bool CanSearch()
        {
            // Enable search commands only when there is search text.
            return !string.IsNullOrWhiteSpace(SearchText);
        }
    }
}
