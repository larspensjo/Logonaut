using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Filters;
using Logonaut.TestUtils;
using Logonaut.Common; // For LogonautSettings 
using Logonaut.UI.ViewModels; // For MainViewModel 
using System.Windows; // For Visibility 
using System.Linq; // For LINQ 
using Logonaut.Core; // Required for Task

namespace Logonaut.UI.Tests.ViewModels;

/// <summary>
/// Tests related to constructor logic, loading/saving basic settings.
/// Verifies the INITIAL state and persistence mechanisms.
/// </summary>
[TestClass] public class MainViewModel_InitializationPersistenceTests : MainViewModelTestBase
{
    [TestInitialize] public override void TestInitialize()
    {
        base.TestInitialize();
        base.SetupMainAndTabViewModel();
    }

    // Verifies: [ReqPersistSettingAutoScrollv1]
    [TestMethod] public void Constructor_LoadsAutoScrollSetting_True()
    {
        // Arrange
        var settings = MockSettingsService.CreateDefaultTestSettings();
        settings.AutoScrollToTail = true;
        _mockSettings.SettingsToReturn = settings;

        // Act: Recreate ViewModel using the base class setup, which respects _mockSettings.SettingsToReturn
        // The TestInitialize in the base class handles the VM creation with the updated settings.
        // We need to explicitly call TestInitialize again OR reinstantiate the VM manually here.
        // Let's reinstantiate manually for clarity in this specific test.
        var localViewModel = new MainViewModel(_mockSettings, _mockSourceProvider, _mockFileDialog, _testContext);

        // Assert
        Assert.IsTrue(localViewModel.IsAutoScrollEnabled);
        localViewModel.Dispose(); // Clean up the local instance
    }

// Verifies: [ReqPersistSettingAutoScrollv1]
    [TestMethod]     public void Constructor_LoadsAutoScrollSetting_False()
    {
        // Arrange
        var settings = MockSettingsService.CreateDefaultTestSettings();
        settings.AutoScrollToTail = false;
        _mockSettings.SettingsToReturn = settings;

// Act: Recreate ViewModel
        var localViewModel = new MainViewModel(_mockSettings, _mockSourceProvider, _mockFileDialog, _testContext);
        
        // Assert
        Assert.IsFalse(localViewModel.IsAutoScrollEnabled);
        localViewModel.Dispose();
    }

// Verifies: [ReqPersistSettingAutoScrollv1]
    [TestMethod]     public void IsAutoScrollEnabled_Set_SavesSettings()
    {
        // Arrange: _viewModel created in TestInitialize uses default settings (AutoScroll=true)
        Assert.IsTrue(_viewModel.IsAutoScrollEnabled, "Initial state should be true");
        _mockSettings.Reset(); // Clear saved state

        // Act
        _viewModel.IsAutoScrollEnabled = false;

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.IsFalse(_mockSettings.SavedSettings?.AutoScrollToTail, "Saved setting should be false.");
        _mockSettings.Reset(); // Clear for next check

        // Act
        _viewModel.IsAutoScrollEnabled = true;

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved again.");
        Assert.IsTrue(_mockSettings.SavedSettings?.AutoScrollToTail, "Saved setting should be true.");
    }

    // Verifies: [ReqSettingsLoadSavev1], [ReqPersistSettingFilterProfilesv1], [ReqPersistSettingLastProfilev1],
    //           [ReqPersistSettingContextLinesv1], [ReqPersistSettingShowLineNumsv1], [ReqPersistSettingHighlightTimev1],
    //           [ReqPersistSettingSearchCasev1]
    [TestMethod] public void Constructor_LoadsSettingsAndInitializesDefaultProfile()
    {
        // Arrange & Act: ViewModel created in base TestInitialize with default settings

        // Assert: Basic ViewModel state reflects loaded default settings
        Assert.IsNotNull(_viewModel.AvailableProfiles);
        Assert.AreEqual(1, _viewModel.AvailableProfiles.Count);
        Assert.IsNotNull(_viewModel.ActiveFilterProfile);
        Assert.AreEqual("Default", _viewModel.ActiveFilterProfile.Name);
        Assert.IsNull(_viewModel.ActiveFilterProfile.Model.RootFilter);
        Assert.IsNull(_viewModel.ActiveFilterProfile.RootFilterViewModel);
        Assert.AreEqual(0, _viewModel.ContextLines);
        Assert.IsTrue(_viewModel.ShowLineNumbers);
        Assert.IsTrue(_viewModel.HighlightTimestamps);
        Assert.IsFalse(_viewModel.IsCaseSensitiveSearch);

        // Assert Busy State on the TabViewModel after constructor and activation
        Assert.AreEqual(1, _tabViewModel.CurrentBusyStates.Count, "Tab's Busy states should contain FilteringToken after constructor trigger.");
        CollectionAssert.Contains(_tabViewModel.CurrentBusyStates, TabViewModel.FilteringToken, "Tab's FilteringToken should be present after constructor trigger.");
        Assert.AreEqual(0, _viewModel.CurrentGlobalBusyStates.Count, "MainViewModel's GlobalBusyStates should be empty.");
    }

    [TestMethod] public void Constructor_TriggersInitialFilter_ResultsInEmptyFilteredLinesAndTabBusyState()
    {

        // Assert: Check the resulting public state of the ViewModel and TabViewModel
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "Initial FilteredLogLines count (delegated) should be 0.");
        Assert.AreEqual(0, _tabViewModel.FilteredLogLinesCount, "TabViewModel's FilteredLogLinesCount should be 0.");

        // Assert Busy State on TabViewModel
        Assert.AreEqual(1, _tabViewModel.CurrentBusyStates.Count, "Tab's Busy states should contain FilteringToken initially.");
        CollectionAssert.Contains(_tabViewModel.CurrentBusyStates, TabViewModel.FilteringToken, "Tab's FilteringToken should be present initially.");
        CollectionAssert.DoesNotContain(_tabViewModel.CurrentBusyStates, TabViewModel.LoadingToken, "Tab's LoadingToken should not be present initially.");
        
        // No local VM or subscription needed/possible here anymore.
    }

    // Verifies: [ReqGeneralBusyIndicatorv1] (Initial state) - More direct check
    [TestMethod] public void Constructor_SetsInitialBusyState_ViaFirstFilterTrigger()
    {
        // Assert Busy State on TabViewModel
        Assert.AreEqual(1, _tabViewModel.CurrentBusyStates.Count, "Tab's CurrentBusyStates count should be 1 after constructor.");
        CollectionAssert.Contains(_tabViewModel.CurrentBusyStates, TabViewModel.FilteringToken, "Tab's FilteringToken should be present.");
        CollectionAssert.DoesNotContain(_tabViewModel.CurrentBusyStates, TabViewModel.LoadingToken, "Tab's LoadingToken should NOT be present.");
        }

    // Verifies: [ReqPersistSettingContextLinesv1], [ReqFilterContextLinesv1] (indirectly by triggering update)
    [TestMethod] public void ContextLines_Set_UpdatesViewModel_TriggersUpdate_SavesSettings()
    {
        // Arrange
        int initialContextLines = _viewModel.ContextLines;
        _mockSettings.Reset();

        // Act
        _viewModel.ContextLines = 5;
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // For filter update debounce

        // Assert: Check ViewModel state
        Assert.AreEqual(5, _viewModel.ContextLines, "ContextLines property was not updated.");

        // Assert: Check observable effects 
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should remain empty after context change on empty doc.");
        Assert.AreEqual(0, _tabViewModel.FilteredLogLinesCount);

        // Assert: Check side effects (saving)
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
        Assert.AreEqual(5, _mockSettings.SavedSettings?.ContextLines, "Saved ContextLines value mismatch.");
    }

    // Verifies: [ReqPersistSettingShowLineNumsv1], [ReqToggleLineNumbersv1]
    [TestMethod] public void ShowLineNumbers_Set_SavesSettings_UpdatesVisibilityProperty()
    {
        // Arrange
        _mockSettings.Reset();
        bool initialState = _viewModel.ShowLineNumbers;
        Visibility initialVisibility = _viewModel.IsCustomLineNumberMarginVisible;
        Assert.IsTrue(initialState, "Default state should be true");
        Assert.AreEqual(Visibility.Visible, initialVisibility, "Default visibility should be Visible");

        // Act
        _viewModel.ShowLineNumbers = !initialState;

        // Assert
        Assert.AreEqual(!initialState, _viewModel.ShowLineNumbers); // Now false
        Assert.AreNotEqual(initialVisibility, _viewModel.IsCustomLineNumberMarginVisible); // Now Collapsed
        Assert.AreEqual(Visibility.Collapsed, _viewModel.IsCustomLineNumberMarginVisible);
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(!initialState, _mockSettings.SavedSettings?.ShowLineNumbers); // Saved as false
    }

// Verifies: [ReqPersistSettingHighlightTimev1], [ReqHighlightTimestampsv1]
    [TestMethod] public void HighlightTimestamps_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();
        bool initialState = _viewModel.HighlightTimestamps;
        Assert.IsTrue(initialState, "Default state should be true");

        // Act
        _viewModel.HighlightTimestamps = !initialState;

        // Assert
        Assert.AreEqual(!initialState, _viewModel.HighlightTimestamps); // Now false
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(!initialState, _mockSettings.SavedSettings?.HighlightTimestamps); // Saved as false
    }

// Verifies: [ReqPersistSettingSearchCasev1], [ReqSearchCaseSensitiveOptionv1]
    [TestMethod] public async Task IsCaseSensitiveSearch_Set_SavesSettings_UpdatesSearch() // Keep async Task
    {
        // Arrange
        _mockFileLogSource.LinesForInitialRead = new List<string> { "Test test" };
        _mockFileDialog.FileToReturn = "test.log"; // Ensure a file is "opened"

        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "FilteredLogLines not populated.");
        Assert.AreEqual("Test test", _viewModel.FilteredLogLines[0].Text, "FilteredLogLines content.");

        // MainViewModel.IsCaseSensitiveSearch directly sets TabViewModel.IsCaseSensitiveSearch
        _viewModel.SearchText = "Test"; // This sets TabViewModel.SearchText via delegation
        _viewModel.IsCaseSensitiveSearch = false; // This sets TabViewModel.IsCaseSensitiveSearch


        Assert.AreEqual(2, _tabViewModel.SearchMarkers.Count, "Initial case-insensitive search failed.");
        _mockSettings.Reset();

        // Act
        _viewModel.IsCaseSensitiveSearch = true; // This sets TabViewModel.IsCaseSensitiveSearch

        // Assert
        Assert.IsTrue(_viewModel.IsCaseSensitiveSearch); // MainViewModel's property
        Assert.IsTrue(_tabViewModel.IsCaseSensitiveSearch); // TabViewModel's property
        Assert.AreEqual(1, _tabViewModel.SearchMarkers.Count, "Case-sensitive search marker count mismatch.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings save failed.");
        Assert.IsTrue(_mockSettings.SavedSettings?.IsCaseSensitiveSearch, "Saved case sensitivity mismatch.");
    }

    // Verifies: [ReqPersistSettingSimulatorV1] (Loading part)
    [TestMethod] public void Constructor_LoadsSimulatorSettings()
    {
        // Arrange
        var settings = MockSettingsService.CreateDefaultTestSettings();
        settings.SimulatorLPS = 55.5;
        settings.SimulatorErrorFrequency = 123.0;
        settings.SimulatorBurstSize = 4567.0;
        _mockSettings.SettingsToReturn = settings;

        var localViewModel = new MainViewModel(_mockSettings, _mockSourceProvider, _mockFileDialog, _testContext, _backgroundScheduler);

        // Assert
        Assert.AreEqual(55.5, localViewModel.SimulatorLPS);
        Assert.AreEqual(123.0, localViewModel.SimulatorErrorFrequency);
        Assert.AreEqual(4567.0, localViewModel.SimulatorBurstSize);

        localViewModel.Dispose();
    }

    // Verifies: [ReqPersistSettingSimulatorV1] (Saving LPS)
    [TestMethod] public void SimulatorLPS_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();

        // Act
        _viewModel.ToggleSimulatorCommand.Execute(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        _viewModel.SimulatorLPS = 77.0;
        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.AreEqual(77.0, _mockSettings.SavedSettings?.SimulatorLPS, "Saved SimulatorLPS mismatch.");
    }

    // Verifies: [ReqPersistSettingSimulatorV1] (Saving Error Frequency)
    [TestMethod] public void SimulatorErrorFrequency_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();

        // Act
        _viewModel.ToggleSimulatorCommand.Execute(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        _viewModel.SimulatorErrorFrequency = 50.0;

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(50.0, _mockSettings.SavedSettings?.SimulatorErrorFrequency);
    }

    // Verifies: [ReqPersistSettingSimulatorV1] (Saving Burst Size)
    [TestMethod] public void SimulatorBurstSize_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();

        // Act
        _viewModel.ToggleSimulatorCommand.Execute(null);
        _viewModel.SimulatorBurstSize = 9999.0;

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(9999.0, _mockSettings.SavedSettings?.SimulatorBurstSize);
    }
}
