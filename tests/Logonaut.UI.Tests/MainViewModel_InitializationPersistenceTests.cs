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
    [TestInitialize] public override void TestInitialize()
    {
        // Arrange
        base.TestInitialize();
        base.SetupMainViewModel();
    }

    // Verifies: [ReqPersistSettingAutoScrollv1]
    [TestMethod] public void Constructor_LoadsAutoScrollSetting_True()
    {
        // Arrange
        var settings = MockSettingsService.CreateDefaultTestSettings();
        settings.AutoScrollToTail = true;
        _mockSettings.SettingsToReturn = settings;

        // Act
        var localViewModel = new MainViewModel(_mockSettings, _mockSourceProvider, _mockFileDialog, _testContext, _backgroundScheduler);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        var localTabViewModel = localViewModel.ActiveTabViewModel;
        Assert.IsNotNull(localTabViewModel, "ActiveTabViewModel should not be null.");

        // Assert
        Assert.IsTrue(localViewModel.IsAutoScrollEnabled, "MainViewModel IsAutoScrollEnabled mismatch.");
        Assert.IsTrue(localTabViewModel.IsAutoScrollEnabled, "TabViewModel IsAutoScrollEnabled mismatch.");
        localViewModel.Dispose();
    }

    // Verifies: [ReqPersistSettingAutoScrollv1]
    [TestMethod] public void Constructor_LoadsAutoScrollSetting_False()
    {
        // Arrange
        var settings = MockSettingsService.CreateDefaultTestSettings();
        settings.AutoScrollToTail = false;
        _mockSettings.SettingsToReturn = settings;

        // Act
        var localViewModel = new MainViewModel(_mockSettings, _mockSourceProvider, _mockFileDialog, _testContext, _backgroundScheduler);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        var localTabViewModel = localViewModel.ActiveTabViewModel;
        Assert.IsNotNull(localTabViewModel, "ActiveTabViewModel should not be null.");

        // Assert
        Assert.IsFalse(localViewModel.IsAutoScrollEnabled, "MainViewModel IsAutoScrollEnabled mismatch.");
        Assert.IsFalse(localTabViewModel.IsAutoScrollEnabled, "TabViewModel IsAutoScrollEnabled mismatch.");
        localViewModel.Dispose();
    }

    // Verifies: [ReqPersistSettingAutoScrollv1]
    [TestMethod] public void IsAutoScrollEnabled_Set_SavesSettings()
    {
        // Arrange
        Assert.IsTrue(_viewModel.IsAutoScrollEnabled, "Initial state should be true");
        var activeTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(activeTab, "ActiveTabViewModel should not be null for test.");
        _mockSettings.Reset();

        // Act
        _viewModel.IsAutoScrollEnabled = false;
        _viewModel.SaveCurrentSettings();

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.IsFalse(_mockSettings.SavedSettings?.AutoScrollToTail, "Saved setting should be false.");
        Assert.IsFalse(activeTab.IsAutoScrollEnabled, "TabViewModel's IsAutoScrollEnabled should be updated to false.");
        _mockSettings.Reset();

        // Act
        _viewModel.IsAutoScrollEnabled = true;
        _viewModel.SaveCurrentSettings();

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved again.");
        Assert.IsTrue(_mockSettings.SavedSettings?.AutoScrollToTail, "Saved setting should be true.");
        Assert.IsTrue(activeTab.IsAutoScrollEnabled, "TabViewModel's IsAutoScrollEnabled should be updated to true.");
    }

    // Verifies: [ReqSettingsLoadSavev1], [ReqPersistSettingFilterProfilesv1], [ReqPersistSettingLastProfilev1],
    //           [ReqPersistSettingContextLinesv1], [ReqPersistSettingShowLineNumsv1], [ReqPersistSettingHighlightTimev1],
    //           [ReqPersistSettingSearchCasev1]
    [TestMethod] public void Constructor_LoadsSettingsAndInitializesDefaultProfile()
    {
        // Arrange & Act
        // Done in base.TestInitialize() and SetupMainViewModel()
        var activeTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(activeTab, "Active tab should exist after construction.");
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.IsNotNull(_viewModel.AvailableProfiles);
        Assert.AreEqual(1, _viewModel.AvailableProfiles.Count);
        Assert.IsNotNull(_viewModel.ActiveFilterProfile);
        Assert.AreEqual("Default", _viewModel.ActiveFilterProfile.Name);
        Assert.IsNull(_viewModel.ActiveFilterProfile.Model.RootFilter, "Default profile should have null root filter initially from mock settings.");
        Assert.IsNull(_viewModel.ActiveFilterProfile.RootFilterViewModel, "Default profile should have null root VM initially.");
        Assert.AreEqual(0, _viewModel.ContextLines);
        Assert.IsTrue(_viewModel.ShowLineNumbers);
        Assert.IsTrue(_viewModel.HighlightTimestamps);
        Assert.IsFalse(_viewModel.IsCaseSensitiveSearch);
        Assert.IsFalse(activeTab.IsCaseSensitiveSearch);
        Assert.IsFalse(activeTab.IsLoading, "TabViewModel should not be considered 'Loading' after initial activation.");
        Assert.AreEqual(0, activeTab.CurrentBusyStates.Count, "Tab's Busy states should be empty after initial activation and filtering.");
        Assert.AreEqual(0, _viewModel.CurrentGlobalBusyStates.Count, "MainViewModel's GlobalBusyStates should be empty.");
    }

    [TestMethod] public void Constructor_TriggersInitialFilter_ResultsInEmptyFilteredLinesOnTab()
    {
        // Arrange & Act
        // Done in TestInitialize
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        var activeTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(activeTab, "ActiveTabViewModel should not be null.");

        // Assert
        Assert.AreEqual(0, activeTab.FilteredLogLines.Count, "TabViewModel's FilteredLogLines count should be 0 initially.");
        Assert.AreEqual(0, activeTab.FilteredLogLinesCount, "TabViewModel's FilteredLogLinesCount property should be 0.");
        Assert.IsFalse(activeTab.IsLoading, "TabViewModel IsLoading should be false.");
    }

    // Verifies: [ReqGeneralBusyIndicatorv1]
    [TestMethod] public void Constructor_InitialTabBusyState_IsCorrect()
    {
        // Arrange & Act
        // Done in TestInitialize
        var activeTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(activeTab, "ActiveTabViewModel should not be null.");


        // Assert
        Assert.IsTrue(activeTab.CurrentBusyStates.Any(), "Tab's CurrentBusyStates should contain a token immediately after MainViewModel construction.");

        // Act
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.AreEqual(0, activeTab.CurrentBusyStates.Count, "Tab's CurrentBusyStates count should be 0 after initial processing.");
        Assert.IsFalse(activeTab.IsLoading, "TabViewModel IsLoading should be false after initial processing.");
    }

    // Verifies: [ReqPersistSettingContextLinesv1], [ReqFilterContextLinesv1]
    [TestMethod] public void ContextLines_Set_UpdatesViewModel_TriggersUpdate_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        var activeTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(activeTab, "ActiveTabViewModel should not be null.");
        activeTab.CurrentBusyStates.Clear();

        // Act
        _viewModel.ContextLines = 5;
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        _viewModel.SaveCurrentSettings();

        // Assert
        Assert.AreEqual(5, _viewModel.ContextLines, "ContextLines property was not updated.");
        Assert.AreEqual(0, activeTab.FilteredLogLines.Count, "Tab's FilteredLogLines should remain empty after context change on empty doc.");
        Assert.IsFalse(activeTab.IsLoading, "TabViewModel IsLoading should be false after context change.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
        Assert.AreEqual(5, _mockSettings.SavedSettings?.ContextLines, "Saved ContextLines value mismatch.");
    }

    // Verifies: [ReqPersistSettingShowLineNumsv1], [ReqToggleLineNumbersv1]
    [TestMethod] public void ShowLineNumbers_Set_SavesSettings_UpdatesVisibilityProperty()
    {
        // Arrange
        _mockSettings.Reset();
        Assert.IsTrue(_viewModel.ShowLineNumbers, "Default state should be true");
        Assert.AreEqual(Visibility.Visible, _viewModel.IsCustomLineNumberMarginVisible, "Default visibility should be Visible");

        // Act
        _viewModel.ShowLineNumbers = false;
        _viewModel.SaveCurrentSettings();

        // Assert
        Assert.IsFalse(_viewModel.ShowLineNumbers);
        Assert.AreEqual(Visibility.Collapsed, _viewModel.IsCustomLineNumberMarginVisible);
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.IsFalse(_mockSettings.SavedSettings?.ShowLineNumbers);
    }

    // Verifies: [ReqPersistSettingHighlightTimev1], [ReqHighlightTimestampsv1]
    [TestMethod] public void HighlightTimestamps_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();
        Assert.IsTrue(_viewModel.HighlightTimestamps, "Default state should be true");

        // Act
        _viewModel.HighlightTimestamps = false;
        _viewModel.SaveCurrentSettings();

        // Assert
        Assert.IsFalse(_viewModel.HighlightTimestamps);
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.IsFalse(_mockSettings.SavedSettings?.HighlightTimestamps);
    }

    // Verifies: [ReqPersistSettingSearchCasev1], [ReqSearchCaseSensitiveOptionv1]
    [TestMethod] public async Task IsCaseSensitiveSearch_Set_SavesSettings_UpdatesSearch()
    {
        // Arrange
        _mockFileLogSource.LinesForInitialRead = new List<string> { "Test test" };
        _mockFileDialog.FileToReturn = "test.log";
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        var activeTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(activeTab, "Active tab is null after opening file.");
        Assert.AreEqual(1, activeTab.FilteredLogLines.Count, "FilteredLogLines not populated.");

        activeTab.SearchText = "Test";
        _viewModel.IsCaseSensitiveSearch = false; // Set global property which should propagate
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(200).Ticks);
        Assert.AreEqual(2, activeTab.SearchMarkers.Count, "Initial case-insensitive search failed.");
        _mockSettings.Reset();

        // Act
        _viewModel.IsCaseSensitiveSearch = true;
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(200).Ticks);
        _viewModel.SaveCurrentSettings();

        // Assert
        Assert.IsTrue(_viewModel.IsCaseSensitiveSearch, "MainViewModel's property not set.");
        Assert.IsTrue(activeTab.IsCaseSensitiveSearch, "TabViewModel's property not updated.");
        Assert.AreEqual(1, activeTab.SearchMarkers.Count, "Case-sensitive search marker count mismatch.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings save failed.");
        Assert.IsTrue(_mockSettings.SavedSettings?.IsCaseSensitiveSearch, "Saved case sensitivity mismatch.");
    }

    // Verifies: [ReqPersistSettingSimulatorV1]
    [TestMethod] public void Constructor_LoadsSimulatorSettings()
    {
        // Arrange
        var settings = MockSettingsService.CreateDefaultTestSettings();
        settings.SimulatorLPS = 55.5;
        settings.SimulatorErrorFrequency = 123.0;
        settings.SimulatorBurstSize = 4567.0;
        _mockSettings.SettingsToReturn = settings;

        // Act
        var localViewModel = new MainViewModel(_mockSettings, _mockSourceProvider, _mockFileDialog, _testContext, _backgroundScheduler);

        // Assert
        Assert.AreEqual(55.5, localViewModel.SimulatorLPS);
        Assert.AreEqual(123.0, localViewModel.SimulatorErrorFrequency);
        Assert.AreEqual(4567.0, localViewModel.SimulatorBurstSize);
        localViewModel.Dispose();
    }

    // Verifies: [ReqPersistSettingSimulatorV1]
    [TestMethod] public async Task SimulatorLPS_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();
        await _viewModel.ToggleSimulatorCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Act
        _viewModel.SimulatorLPS = 77.0;
        _viewModel.SaveCurrentSettings();

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.AreEqual(77.0, _mockSettings.SavedSettings?.SimulatorLPS, "Saved SimulatorLPS mismatch.");
    }

    // Verifies: [ReqPersistSettingSimulatorV1]
    [TestMethod] public async Task SimulatorErrorFrequency_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();
        await _viewModel.ToggleSimulatorCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Act
        _viewModel.SimulatorErrorFrequency = 50.0;
        _viewModel.SaveCurrentSettings();

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(50.0, _mockSettings.SavedSettings?.SimulatorErrorFrequency);
    }

    // Verifies: [ReqPersistSettingSimulatorV1]
    [TestMethod] public void SimulatorBurstSize_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();

        // Act
        _viewModel.SimulatorBurstSize = 9999.0;
        _viewModel.SaveCurrentSettings();

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(9999.0, _mockSettings.SavedSettings?.SimulatorBurstSize);
    }

    // Verifies: [ReqPersistSettingFontFamilyV1], [ReqPersistSettingFontSizeV1]
    [TestMethod] public void Constructor_LoadsFontSettings()
    {
        // Arrange
        var settings = MockSettingsService.CreateDefaultTestSettings();
        settings.EditorFontFamilyName = "Courier New";
        settings.EditorFontSize = 14.0;
        _mockSettings.SettingsToReturn = settings;

        // Act
        var localViewModel = new MainViewModel(_mockSettings, _mockSourceProvider, _mockFileDialog, _testContext, _backgroundScheduler);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert
        Assert.AreEqual("Courier New", localViewModel.EditorFontFamilyName, "EditorFontFamilyName not loaded correctly.");
        Assert.AreEqual(14.0, localViewModel.EditorFontSize, "EditorFontSize not loaded correctly.");
        localViewModel.Dispose();
    }

    // Verifies: [ReqPersistSettingFontFamilyV1]
    [TestMethod] public void EditorFontFamilyName_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();

        // Act
        _viewModel.EditorFontFamilyName = "Cascadia Mono";
        _viewModel.SaveCurrentSettings();

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.AreEqual("Cascadia Mono", _mockSettings.SavedSettings?.EditorFontFamilyName, "Saved EditorFontFamilyName mismatch.");
        Assert.AreEqual("Cascadia Mono", _viewModel.EditorFontFamilyName, "ViewModel EditorFontFamilyName mismatch after set.");
    }

    // Verifies: [ReqPersistSettingFontSizeV1]
    [TestMethod] public void EditorFontSize_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.Reset();

        // Act
        _viewModel.EditorFontSize = 16.0;
        _viewModel.SaveCurrentSettings();

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.AreEqual(16.0, _mockSettings.SavedSettings?.EditorFontSize, "Saved EditorFontSize mismatch.");
        Assert.AreEqual(16.0, _viewModel.EditorFontSize, "ViewModel EditorFontSize mismatch after set.");
    }
}
