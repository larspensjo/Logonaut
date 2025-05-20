using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Filters;
using Logonaut.TestUtils;
using Logonaut.Common; 
using Logonaut.UI.ViewModels; 
using System.Windows; 
using System.Linq; 
using System.Threading.Tasks;
using System;
using Logonaut.Core;

namespace Logonaut.UI.Tests.ViewModels;

/*
 * Unit tests for MainViewModel focusing on its initialization and persistence logic.
 * These tests verify that settings are correctly loaded upon construction,
 * default states are established, and changes to UI-related settings (including font preferences)
 * are persisted and trigger appropriate updates within the application.
 */
[TestClass] public class MainViewModel_InitializationPersistenceTests : MainViewModelTestBase
{
    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        base.SetupMainAndTabViewModel(); // Sets up _viewModel and _tabViewModel
    }

    // Verifies: [ReqPersistSettingAutoScrollv1]
    [TestMethod] public void Constructor_LoadsAutoScrollSetting_True()
    {
        // Arrange
        var settings = MockSettingsService.CreateDefaultTestSettings();
        settings.AutoScrollToTail = true;
        _mockSettings.SettingsToReturn = settings;

        var localViewModel = new MainViewModel(_mockSettings, _mockSourceProvider, _mockFileDialog, _testContext, _backgroundScheduler);
        var localTabViewModel = GetInternalTabViewModel(localViewModel); // Get its internal tab

        // Act: Activate the tab which applies settings
        // TabViewModel.ActivateAsync is called by MainViewModel's constructor after loading settings.
        // We are creating a localViewModel, so its constructor will run and call TabVM.ActivateAsync.
        // Need to advance scheduler to let activation and initial filtering complete.
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);


        // Assert
        Assert.IsTrue(localViewModel.IsAutoScrollEnabled, "MainViewModel IsAutoScrollEnabled mismatch.");
        Assert.IsTrue(localTabViewModel.IsAutoScrollEnabled, "TabViewModel IsAutoScrollEnabled mismatch."); // TabVM gets it from MainVM
        localViewModel.Dispose();
    }

    // Verifies: [ReqPersistSettingAutoScrollv1]
    [TestMethod] public void Constructor_LoadsAutoScrollSetting_False()
    {
        // Arrange
        var settings = MockSettingsService.CreateDefaultTestSettings();
        settings.AutoScrollToTail = false;
        _mockSettings.SettingsToReturn = settings;

        var localViewModel = new MainViewModel(_mockSettings, _mockSourceProvider, _mockFileDialog, _testContext, _backgroundScheduler);
        var localTabViewModel = GetInternalTabViewModel(localViewModel);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.IsFalse(localViewModel.IsAutoScrollEnabled, "MainViewModel IsAutoScrollEnabled mismatch.");
        Assert.IsFalse(localTabViewModel.IsAutoScrollEnabled, "TabViewModel IsAutoScrollEnabled mismatch.");
        localViewModel.Dispose();
    }

    // Verifies: [ReqPersistSettingAutoScrollv1]
    [TestMethod] public void IsAutoScrollEnabled_Set_SavesSettings()
    {
        // Arrange: _viewModel created in TestInitialize uses default settings (AutoScroll=true)
        Assert.IsTrue(_viewModel.IsAutoScrollEnabled, "Initial state should be true");
        _mockSettings.Reset();

        // Act
        _viewModel.IsAutoScrollEnabled = false;

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.IsFalse(_mockSettings.SavedSettings?.AutoScrollToTail, "Saved setting should be false.");
        Assert.IsFalse(_tabViewModel.IsAutoScrollEnabled, "TabViewModel's IsAutoScrollEnabled should be updated to false.");
        _mockSettings.Reset();

        // Act
        _viewModel.IsAutoScrollEnabled = true;

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved again.");
        Assert.IsTrue(_mockSettings.SavedSettings?.AutoScrollToTail, "Saved setting should be true.");
        Assert.IsTrue(_tabViewModel.IsAutoScrollEnabled, "TabViewModel's IsAutoScrollEnabled should be updated to true.");
    }

    // Verifies: [ReqSettingsLoadSavev1], [ReqPersistSettingFilterProfilesv1], [ReqPersistSettingLastProfilev1],
    //           [ReqPersistSettingContextLinesv1], [ReqPersistSettingShowLineNumsv1], [ReqPersistSettingHighlightTimev1],
    //           [ReqPersistSettingSearchCasev1]
    [TestMethod] public void Constructor_LoadsSettingsAndInitializesDefaultProfile()
    {
        // Arrange & Act are done in base.TestInitialize() and SetupMainAndTabViewModel()

        // Assert: Basic ViewModel state reflects loaded default settings
        Assert.IsNotNull(_viewModel.AvailableProfiles);
        Assert.AreEqual(1, _viewModel.AvailableProfiles.Count);
        Assert.IsNotNull(_viewModel.ActiveFilterProfile);
        Assert.AreEqual("Default", _viewModel.ActiveFilterProfile.Name);
        Assert.IsNull(_viewModel.ActiveFilterProfile.Model.RootFilter, "Default profile should have null root filter initially from mock settings.");
        Assert.IsNull(_viewModel.ActiveFilterProfile.RootFilterViewModel, "Default profile should have null root VM initially.");

        Assert.AreEqual(0, _viewModel.ContextLines);
        Assert.IsTrue(_viewModel.ShowLineNumbers);
        Assert.IsTrue(_viewModel.HighlightTimestamps);
        Assert.IsFalse(_viewModel.IsCaseSensitiveSearch); // MainViewModel property
        Assert.IsFalse(_tabViewModel.IsCaseSensitiveSearch); // TabViewModel property after init

        // Assert Busy State on the TabViewModel after constructor and activation
        // The constructor calls ActivateAsync, which in turn calls ApplyFilterSettings via LogDataProcessor.
        // This should put the TabViewModel into a Filtering state initially.
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow activation to complete

        Assert.IsFalse(_tabViewModel.IsLoading, "TabViewModel should not be considered 'Loading' after initial activation.");
        // If initial processing completed, FilteringToken should be removed.
        // Depending on exact timing and whether initial filter is truly "empty", it might briefly appear.
        // For an empty document, ReplaceFilteredUpdate (IsInitialLoadProcessingComplete=true) comes from processor.
        // This removes LoadingToken. FilteringToken is added/removed by ApplyFilteredUpdateToThisTab.
        // If no lines, the initial "Replace" will occur.
        Assert.AreEqual(0, _tabViewModel.CurrentBusyStates.Count, "Tab's Busy states should be empty after initial activation and filtering.");
        Assert.AreEqual(0, _viewModel.CurrentGlobalBusyStates.Count, "MainViewModel's GlobalBusyStates should be empty.");
    }

    // This test is simplified as detailed stream processing is tested in LogDataProcessorTests.
    // We focus on the outcome on TabViewModel.
    [TestMethod] public void Constructor_TriggersInitialFilter_ResultsInEmptyFilteredLinesOnTab()
    {
        // Arrange & Act done in TestInitialize

        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow activation to complete

        // Assert: Check the resulting public state of the TabViewModel
        Assert.AreEqual(0, _tabViewModel.FilteredLogLines.Count, "TabViewModel's FilteredLogLines count should be 0 initially.");
        Assert.AreEqual(0, _tabViewModel.FilteredLogLinesCount, "TabViewModel's FilteredLogLinesCount property should be 0.");
        Assert.IsFalse(_tabViewModel.IsLoading, "TabViewModel IsLoading should be false.");
    }


    // Verifies: [ReqGeneralBusyIndicatorv1] (Initial state) - More direct check
    [TestMethod] public void Constructor_InitialTabBusyState_IsCorrect()
    {
        // Arrange & Act done in TestInitialize

        // The MainViewModel constructor calls TabViewModel.ActivateAsync.
        // TabViewModel.ActivateAsync calls LogDataProcessor.ActivateAsync.
        // LogDataProcessor.ActivateAsync calls ReactiveFilteredLogStream.Reset() then UpdateFilterSettings().
        // ReactiveFilteredLogStream.UpdateFilterSettings() triggers a full refilter.
        // The full refilter pipeline in ReactiveFilteredLogStream produces a ReplaceFilteredUpdate.
        // TabViewModel.ApplyFilteredUpdateToThisTab receives this, adds FilteringToken, then removes it.
        // If initial load completes, LoadingToken is also removed.

        // Assert that immediately after MainViewModel constructor, TabViewModel might be busy.
        // This assertion captures the state *before* TestScheduler is advanced for completion.
        Assert.IsTrue(_tabViewModel.CurrentBusyStates.Contains(TabViewModel.LoadingToken) ||
                      _tabViewModel.CurrentBusyStates.Contains(TabViewModel.FilteringToken),
                      "Tab's CurrentBusyStates should contain a token immediately after MainViewModel construction.");

        // Act: Advance scheduler to let initial processing complete
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert: After processing, busy states should be clear.
        Assert.AreEqual(0, _tabViewModel.CurrentBusyStates.Count, "Tab's CurrentBusyStates count should be 0 after initial processing.");
        Assert.IsFalse(_tabViewModel.IsLoading, "TabViewModel IsLoading should be false after initial processing.");
    }

    // Verifies: [ReqPersistSettingContextLinesv1], [ReqFilterContextLinesv1]
    [TestMethod] public void ContextLines_Set_UpdatesViewModel_TriggersUpdate_SavesSettings()
    {
        // Arrange
        int initialContextLines = _viewModel.ContextLines;
        _mockSettings.Reset();
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Ensure initial filter is done
        _tabViewModel.CurrentBusyStates.Clear(); // Clear just before action

        // Act
        _viewModel.ContextLines = 5;
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // For filter update debounce in MainViewModel

        // Assert: Check ViewModel state
        Assert.AreEqual(5, _viewModel.ContextLines, "ContextLines property was not updated.");

        // Assert: Check observable effects on TabViewModel
        Assert.AreEqual(0, _tabViewModel.FilteredLogLines.Count, "Tab's FilteredLogLines should remain empty after context change on empty doc.");
        Assert.IsFalse(_tabViewModel.IsLoading, "TabViewModel IsLoading should be false after context change.");

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
    [TestMethod] public async Task IsCaseSensitiveSearch_Set_SavesSettings_UpdatesSearch()
    {
        // Arrange
        _mockFileLogSource.LinesForInitialRead = new List<string> { "Test test" };
        _mockFileDialog.FileToReturn = "test.log";

        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        Assert.AreEqual(1, _tabViewModel.FilteredLogLines.Count, "FilteredLogLines not populated.");
        Assert.AreEqual("Test test", _tabViewModel.FilteredLogLines[0].Text, "FilteredLogLines content.");

        _tabViewModel.SearchText = "Test"; // Set directly on TabViewModel
        _tabViewModel.IsCaseSensitiveSearch = false; // Set directly on TabViewModel
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks); // For search update

        Assert.AreEqual(2, _tabViewModel.SearchMarkers.Count, "Initial case-insensitive search failed.");
        _mockSettings.Reset();

        // Act: Change MainViewModel's property, which should propagate to TabViewModel
        _viewModel.IsCaseSensitiveSearch = true;
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks); // For search update

        // Assert
        Assert.IsTrue(_viewModel.IsCaseSensitiveSearch, "MainViewModel's property not set.");
        Assert.IsTrue(_tabViewModel.IsCaseSensitiveSearch, "TabViewModel's property not updated.");
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
        // No need to call ActivateAsync, constructor handles initial load of settings.

        // Assert
        Assert.AreEqual(55.5, localViewModel.SimulatorLPS);
        Assert.AreEqual(123.0, localViewModel.SimulatorErrorFrequency);
        Assert.AreEqual(4567.0, localViewModel.SimulatorBurstSize);

        localViewModel.Dispose();
    }

    // Verifies: [ReqPersistSettingSimulatorV1] (Saving LPS)
    [TestMethod] public async Task SimulatorLPS_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();

        // Act: ToggleSimulatorCommand is async
        await _viewModel.ToggleSimulatorCommand.ExecuteAsync(null); // This will setup the simulator in TabViewModel
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow activation

        _viewModel.SimulatorLPS = 77.0; // This should save settings

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.AreEqual(77.0, _mockSettings.SavedSettings?.SimulatorLPS, "Saved SimulatorLPS mismatch.");
    }

    // Verifies: [ReqPersistSettingSimulatorV1] (Saving Error Frequency)
    [TestMethod] public async Task SimulatorErrorFrequency_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();
        await _viewModel.ToggleSimulatorCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Act
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
        _viewModel.SimulatorBurstSize = 9999.0;

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(9999.0, _mockSettings.SavedSettings?.SimulatorBurstSize);
    }

    // Verifies: [ReqPersistSettingFontFamilyV1] (Loading), [ReqPersistSettingFontSizeV1] (Loading)
    [TestMethod] public void Constructor_LoadsFontSettings()
    {
        // Arrange
        var settings = MockSettingsService.CreateDefaultTestSettings();
        settings.EditorFontFamilyName = "Courier New";
        settings.EditorFontSize = 14.0;
        _mockSettings.SettingsToReturn = settings;

        // Act
        var localViewModel = new MainViewModel(_mockSettings, _mockSourceProvider, _mockFileDialog, _testContext, _backgroundScheduler);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow initial setup

        // Assert
        Assert.AreEqual("Courier New", localViewModel.EditorFontFamilyName, "EditorFontFamilyName not loaded correctly.");
        Assert.AreEqual(14.0, localViewModel.EditorFontSize, "EditorFontSize not loaded correctly.");

        localViewModel.Dispose();
    }

    // Verifies: [ReqPersistSettingFontFamilyV1] (Saving)
    [TestMethod] public void EditorFontFamilyName_Set_SavesSettings()
    {
        // Arrange: _viewModel is created in TestInitialize using default settings ("Consolas", 12.0)
        _mockSettings.Reset(); // Clear any save calls from constructor

        // Act
        _viewModel.EditorFontFamilyName = "Cascadia Mono";

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.AreEqual("Cascadia Mono", _mockSettings.SavedSettings?.EditorFontFamilyName, "Saved EditorFontFamilyName mismatch.");
        Assert.AreEqual("Cascadia Mono", _viewModel.EditorFontFamilyName, "ViewModel EditorFontFamilyName mismatch after set.");
    }

    // Verifies: [ReqPersistSettingFontSizeV1] (Saving)
    [TestMethod] public void EditorFontSize_Set_SavesSettings()
    {
        // Arrange: _viewModel is created in TestInitialize using default settings ("Consolas", 12.0)
        _mockSettings.Reset(); // Clear any save calls from constructor

        // Act
        _viewModel.EditorFontSize = 16.0;

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.AreEqual(16.0, _mockSettings.SavedSettings?.EditorFontSize, "Saved EditorFontSize mismatch.");
        Assert.AreEqual(16.0, _viewModel.EditorFontSize, "ViewModel EditorFontSize mismatch after set.");
    }
}
