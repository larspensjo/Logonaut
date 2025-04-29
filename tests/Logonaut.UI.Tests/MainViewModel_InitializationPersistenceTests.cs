using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Filters;
using Logonaut.TestUtils;
using Logonaut.Common; // For LogonautSettings
using Logonaut.UI.ViewModels; // For MainViewModel
using System.Windows; // For Visibility

namespace Logonaut.UI.Tests.ViewModels;

 /// Tests related to constructor logic, loading/saving basic settings
[TestClass] public class MainViewModel_InitializationPersistenceTests : MainViewModelTestBase
{
    // Verifies: [ReqPersistSettingAutoScrollv1]
    [TestMethod] public void Constructor_LoadsAutoScrollSetting_True()
    {
        // Arrange
        var settings = MockSettingsService.CreateDefaultTestSettings();
        settings.AutoScrollToTail = true;
        _mockSettings.SettingsToReturn = settings;

        // Act: Recreate ViewModel with specific settings (base TestInitialize does this, but we override settings here)
        _viewModel = new MainViewModel(_mockSettings, _mockFileDialog, _mockProcessor, _mockLogSource, _testContext);

        // Assert
        Assert.IsTrue(_viewModel.IsAutoScrollEnabled);
    }

    // Verifies: [ReqPersistSettingAutoScrollv1]
    [TestMethod] public void Constructor_LoadsAutoScrollSetting_False()
    {
        // Arrange
        var settings = MockSettingsService.CreateDefaultTestSettings();
        settings.AutoScrollToTail = false;
        _mockSettings.SettingsToReturn = settings;

        // Act: Recreate ViewModel with specific settings
        _viewModel = new MainViewModel(_mockSettings, _mockFileDialog, _mockProcessor, _mockLogSource, _testContext);

        // Assert
        Assert.IsFalse(_viewModel.IsAutoScrollEnabled);
    }

    // Verifies: [ReqPersistSettingAutoScrollv1]
    [TestMethod] public void IsAutoScrollEnabled_Set_SavesSettings()
    {
        // Arrange
        Assert.IsTrue(_viewModel.IsAutoScrollEnabled, "Initial state should be true"); // Based on default settings
        _mockSettings.ResetSettings(); // Clear saved state

        // Act
        _viewModel.IsAutoScrollEnabled = false;
        _testContext.Send(_ => { }, null); // Ensure Post callback for save runs

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.IsFalse(_mockSettings.SavedSettings?.AutoScrollToTail, "Saved setting should be false.");
        _mockSettings.ResetSettings();

        // Act
        _viewModel.IsAutoScrollEnabled = true;
        _testContext.Send(_ => { }, null); // Ensure Post callback for save runs

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved again.");
        Assert.IsTrue(_mockSettings.SavedSettings?.AutoScrollToTail, "Saved setting should be true.");
    }

    // Verifies: [ReqSettingsLoadSavev1], [ReqPersistSettingFilterProfilesv1], [ReqPersistSettingLastProfilev1],
    //           [ReqPersistSettingContextLinesv1], [ReqPersistSettingShowLineNumsv1], [ReqPersistSettingHighlightTimev1],
    //           [ReqPersistSettingSearchCasev1]
    [TestMethod] public void Constructor_LoadsSettingsAndInitializesDefaultProfile()
    {
        // Act (ViewModel created in base TestInitialize)

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
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count, "Busy states should contain FilteringToken after constructor trigger.");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "FilteringToken should be present after constructor trigger.");
    }

    // Verifies internal mechanism of initial filter trigger
    [TestMethod] public void Constructor_TriggersInitialFilterUpdateViaActiveProfileChange()
    {
        // Assert (Processor interaction happens during construction via ActiveProfile set)
        Assert.AreEqual(1, _mockProcessor.UpdateFilterSettingsCallCount);
        Assert.IsNotNull(_mockProcessor.LastFilterSettings);
        Assert.IsInstanceOfType(_mockProcessor.LastFilterSettings?.Filter, typeof(TrueFilter));
        Assert.AreEqual(0, _mockProcessor.LastFilterSettings?.ContextLines);
        // Assert initial busy state *after* constructor finishes and triggers update
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count, "Busy states should contain one item after constructor trigger.");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "FilteringToken should be present after constructor trigger.");
    }

    // Verifies: [ReqGeneralBusyIndicatorv1] (Initial state)
    [TestMethod] public void Constructor_SetsInitialBusyState_ViaFirstFilterTrigger()
    {
        // Assert (ViewModel created in TestInitialize automatically triggers first filter)
        Assert.AreEqual(1, _mockProcessor.UpdateFilterSettingsCallCount, "Processor.UpdateFilterSettings should be called once by constructor logic.");
        Assert.AreEqual(0, _mockProcessor.ResetCallCount, "Processor.Reset should NOT be called by constructor logic.");

        // Assert NEW busy state: FilteringToken should be present
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count, "CurrentBusyStates count should be 1.");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "FilteringToken should be present.");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken, "LoadingToken should NOT be present.");
    }

    // Verifies: [ReqPersistSettingContextLinesv1]
    [TestMethod] public void ContextLines_Set_TriggersProcessorUpdate_SavesSettings()
    {
        // Arrange
        _mockProcessor.ResetCounters(); // Reset before Act
        _mockSettings.ResetSettings();

        // Act
        _viewModel.ContextLines = 5;
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert
        Assert.AreEqual(5, _viewModel.ContextLines);
        Assert.AreEqual(1, _mockProcessor.UpdateFilterSettingsCallCount);
        Assert.AreEqual(5, _mockProcessor.LastFilterSettings?.ContextLines);
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(5, _mockSettings.SavedSettings?.ContextLines);
    }

    // Verifies: [ReqPersistSettingShowLineNumsv1], [ReqToggleLineNumbersv1]
    [TestMethod] public void ShowLineNumbers_Set_SavesSettings_UpdatesVisibilityProperty()
    {
        // Arrange
        _mockSettings.ResetSettings();
        bool initialState = _viewModel.ShowLineNumbers;
        Visibility initialVisibility = _viewModel.IsCustomLineNumberMarginVisible;

        // Act
        _viewModel.ShowLineNumbers = !initialState;
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert
        Assert.AreEqual(!initialState, _viewModel.ShowLineNumbers);
        Assert.AreNotEqual(initialVisibility, _viewModel.IsCustomLineNumberMarginVisible);
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(!initialState, _mockSettings.SavedSettings?.ShowLineNumbers);
    }

    // Verifies: [ReqPersistSettingHighlightTimev1], [ReqHighlightTimestampsv1]
    [TestMethod] public void HighlightTimestamps_Set_SavesSettings()
    {
        // Arrange
        _mockSettings.ResetSettings();
        bool initialState = _viewModel.HighlightTimestamps;

        // Act
        _viewModel.HighlightTimestamps = !initialState;
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert
        Assert.AreEqual(!initialState, _viewModel.HighlightTimestamps);
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(!initialState, _mockSettings.SavedSettings?.HighlightTimestamps);
    }

    // Verifies: [ReqPersistSettingSearchCasev1], [ReqSearchCaseSensitiveOptionv1]
    [TestMethod] public void IsCaseSensitiveSearch_Set_SavesSettings_UpdatesSearch()
    {
        // Arrange
        _viewModel.FilteredLogLines.Add(new FilteredLogLine(1, "Test test"));
        // Simulate internal text update (safe call via reflection or make method protected/internal)
        var updateMethod = _viewModel.GetType().GetMethod("ReplaceLogTextInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        updateMethod?.Invoke(_viewModel, new object[] { _viewModel.FilteredLogLines.ToList() });

        _viewModel.SearchText = "Test";
        _viewModel.IsCaseSensitiveSearch = false;
        _testContext.Send(_ => { }, null); // Allow UpdateSearchMatches to run
        Assert.AreEqual(2, _viewModel.SearchMarkers.Count);
        _mockSettings.ResetSettings();

        // Act
        _viewModel.IsCaseSensitiveSearch = true;
        _testContext.Send(_ => { }, null); // Allow UpdateSearchMatches and save to run

        // Assert
        Assert.IsTrue(_viewModel.IsCaseSensitiveSearch);
        Assert.AreEqual(1, _viewModel.SearchMarkers.Count);
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.IsTrue(_mockSettings.SavedSettings?.IsCaseSensitiveSearch);
    }
}
