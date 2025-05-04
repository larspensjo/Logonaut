using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Filters;
using Logonaut.TestUtils;
using Logonaut.Common; // For LogonautSettings
using Logonaut.UI.ViewModels; // For MainViewModel
using System.Windows; // For Visibility
using System.Linq; // For LINQ
using Logonaut.Core; // For FilteredUpdateBase etc.

namespace Logonaut.UI.Tests.ViewModels;

/// <summary>
/// Tests related to constructor logic, loading/saving basic settings.
/// Verifies the INITIAL state and persistence mechanisms.
/// </summary>
[TestClass]
public class MainViewModel_InitializationPersistenceTests : MainViewModelTestBase // Inherits mocks, context, VM setup
{
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
    [TestMethod] public void Constructor_LoadsAutoScrollSetting_False()
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
    [TestMethod] public void IsAutoScrollEnabled_Set_SavesSettings()
    {
        // Arrange: _viewModel created in TestInitialize uses default settings (AutoScroll=true)
        Assert.IsTrue(_viewModel.IsAutoScrollEnabled, "Initial state should be true");
        _mockSettings.ResetSettings(); // Clear saved state

        // Act
        _viewModel.IsAutoScrollEnabled = false;
        _testContext.Send(_ => { }, null); // Ensure Post callback for save runs

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.IsFalse(_mockSettings.SavedSettings?.AutoScrollToTail, "Saved setting should be false.");
        _mockSettings.ResetSettings(); // Clear for next check

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
        // Arrange & Act: ViewModel created in base TestInitialize with default settings

        // Assert: Basic ViewModel state reflects loaded default settings
        Assert.IsNotNull(_viewModel.AvailableProfiles);
        Assert.AreEqual(1, _viewModel.AvailableProfiles.Count);
        Assert.IsNotNull(_viewModel.ActiveFilterProfile);
        Assert.AreEqual("Default", _viewModel.ActiveFilterProfile.Name);
        Assert.IsNull(_viewModel.ActiveFilterProfile.Model.RootFilter); // Model check
        Assert.IsNull(_viewModel.ActiveFilterProfile.RootFilterViewModel); // ViewModel check
        Assert.AreEqual(0, _viewModel.ContextLines);
        Assert.IsTrue(_viewModel.ShowLineNumbers);
        Assert.IsTrue(_viewModel.HighlightTimestamps);
        Assert.IsFalse(_viewModel.IsCaseSensitiveSearch);
        // Assert Busy State *after* constructor finished and triggered update via OnActiveProfileChanged
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count, "Busy states should contain FilteringToken after constructor trigger.");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "FilteringToken should be present after constructor trigger.");
    }

    // Verifies internal mechanism of initial filter trigger and its observable effect on VM state
    [TestMethod] public void Constructor_TriggersInitialFilter_ResultsInEmptyFilteredLinesAndBusyState() // Renamed for clarity
    {
        // Arrange: Use the _viewModel created in the base TestInitialize,
        // which has already gone through the constructor logic.

        // Act: Flush the context queue to ensure any actions posted during
        // the constructor (like setting busy state or initial filter update) have completed.
        _testContext.Send(_ => { }, null);

        // Assert: Check the resulting public state of the ViewModel
        // The internal processor ran an initial filter on an empty LogDoc.
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "Initial FilteredLogLines count should be 0.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "Initial FilteredLogLinesCount should be 0.");

        // Assert Busy State after constructor logic and context flush
        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count, "Busy states should contain FilteringToken after constructor trigger.");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "FilteringToken should be present initially.");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken, "LoadingToken should not be present initially.");

        // No local VM or subscription needed/possible here anymore.
    }

    // Verifies: [ReqGeneralBusyIndicatorv1] (Initial state) - More direct check
    [TestMethod] public void Constructor_SetsInitialBusyState_ViaFirstFilterTrigger()
    {
        // Arrange & Act: ViewModel created in TestInitialize automatically triggers first filter

        // Assert Busy State *after* constructor finished
        // The filter trigger happens within the constructor logic (via OnActiveFilterProfileChanged)
        // and posts the busy state addition.
        _testContext.Send(_ => { }, null); // Flush the context queue AFTER construction completes

        Assert.AreEqual(1, _viewModel.CurrentBusyStates.Count, "CurrentBusyStates count should be 1 after constructor.");
        CollectionAssert.Contains(_viewModel.CurrentBusyStates, MainViewModel.FilteringToken, "FilteringToken should be present.");
        CollectionAssert.DoesNotContain(_viewModel.CurrentBusyStates, MainViewModel.LoadingToken, "LoadingToken should NOT be present.");
    }

    // Verifies: [ReqPersistSettingContextLinesv1], [ReqFilterContextLinesv1] (indirectly by triggering update)
    [TestMethod] public void ContextLines_Set_UpdatesViewModel_TriggersUpdate_SavesSettings() // Renamed slightly
    {
        // Arrange
        int initialContextLines = _viewModel.ContextLines; // Usually 0 from defaults
        _mockSettings.ResetSettings(); // Clear save status before Act

        // Act
        _viewModel.ContextLines = 5;
        _testContext.Send(_ => { }, null); // Flush context queue for update trigger and save

        // Assert: Check ViewModel state
        Assert.AreEqual(5, _viewModel.ContextLines, "ContextLines property was not updated.");

        // Assert: Check observable effects (indirectly)
        // Since the LogDoc is empty, changing context lines should still result
        // in an empty filtered list after the internal update runs.
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should remain empty after context change on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount);
        // We can infer the update was triggered because the state didn't unexpectedly change,
        // and saving occurred. A more robust check would involve setting up a LogDoc with lines.

        // Assert: Check side effects (saving)
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
        Assert.AreEqual(5, _mockSettings.SavedSettings?.ContextLines, "Saved ContextLines value mismatch.");
    }

    // Verifies: [ReqPersistSettingShowLineNumsv1], [ReqToggleLineNumbersv1]
    [TestMethod] public void ShowLineNumbers_Set_SavesSettings_UpdatesVisibilityProperty()
    {
        // Arrange
        _mockSettings.ResetSettings();
        bool initialState = _viewModel.ShowLineNumbers;
        Visibility initialVisibility = _viewModel.IsCustomLineNumberMarginVisible;
        Assert.IsTrue(initialState, "Default state should be true");
        Assert.AreEqual(Visibility.Visible, initialVisibility, "Default visibility should be Visible");

        // Act
        _viewModel.ShowLineNumbers = !initialState; // Set to false
        _testContext.Send(_ => { }, null); // Flush context queue

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
        _mockSettings.ResetSettings();
        bool initialState = _viewModel.HighlightTimestamps;
        Assert.IsTrue(initialState, "Default state should be true");

        // Act
        _viewModel.HighlightTimestamps = !initialState; // Set to false
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert
        Assert.AreEqual(!initialState, _viewModel.HighlightTimestamps); // Now false
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(!initialState, _mockSettings.SavedSettings?.HighlightTimestamps); // Saved as false
    }

    // Verifies: [ReqPersistSettingSearchCasev1], [ReqSearchCaseSensitiveOptionv1]
    [TestMethod] public async Task IsCaseSensitiveSearch_Set_SavesSettings_UpdatesSearch() // Keep async Task
    {
        // Arrange
        var activeSource = GetActiveMockSource();
        activeSource.LinesForInitialRead = new List<string> { "Test test" };

        // 1. Open File
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null); // Await the command

        // 2. Advance background scheduler to allow filtering pipeline to run
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Adjust time if needed

        // 3. Process UI queue to handle the ApplyFilteredUpdate posted by the filter pipeline
        _testContext.Send(_ => { }, null);

        // --- ASSERTION POINT: Verify FilteredLogLines is populated ---
        Assert.AreEqual(1, _viewModel.FilteredLogLines.Count, "FilteredLogLines not populated after file open and UI update.");
        Assert.AreEqual("Test test", _viewModel.FilteredLogLines[0].Text, "FilteredLogLines content mismatch.");

        // 4. NOW perform the initial search
        _viewModel.SearchText = "Test";
        _viewModel.IsCaseSensitiveSearch = false;

        // 5. Process UI queue for the UpdateSearchMatches call (likely synchronous here, but good practice)
        _testContext.Send(_ => { }, null);

        // 6. Assert initial search results
        Assert.AreEqual(2, _viewModel.SearchMarkers.Count, "Initial case-insensitive search failed."); // Should pass now
        _mockSettings.ResetSettings(); // Clear save state AFTER initial search setup

        // Act
        _viewModel.IsCaseSensitiveSearch = true; // Change to case-sensitive

        // Process UI queue for UpdateSearchMatches AND the SaveCurrentSettings post
        _testContext.Send(_ => { }, null);

        // Assert
        Assert.IsTrue(_viewModel.IsCaseSensitiveSearch);
        Assert.AreEqual(1, _viewModel.SearchMarkers.Count, "Case-sensitive search marker count mismatch.");
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

        // Act: Recreate the ViewModel to force loading the arranged settings
        // The base TestInitialize runs before each test, so _viewModel uses the settings above.
        var localViewModel = new MainViewModel(_mockSettings, _mockSourceProvider, _mockFileDialog, _testContext, _backgroundScheduler);


        // Assert
        Assert.AreEqual(55.5, localViewModel.SimulatorLPS);
        Assert.AreEqual(123.0, localViewModel.SimulatorErrorFrequency);
        Assert.AreEqual(4567.0, localViewModel.SimulatorBurstSize);

        localViewModel.Dispose(); // Dispose the local instance
    }

    // Verifies: [ReqPersistSettingSimulatorV1] (Saving LPS)
    [TestMethod] public void SimulatorLPS_Set_SavesSettings()
    {
        // Arrange: ViewModel starts with default settings (10.0)
        _mockSettings.ResetSettings(); // Clear save status

        // Act
        _viewModel.ToggleSimulatorCommand.Execute(null); // Start simulator mode
        _viewModel.SimulatorLPS = 77.0;
        _testContext.Send(_ => { }, null); // Process the SaveCurrentSettingsDelayed post

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.AreEqual(77.0, _mockSettings.SavedSettings?.SimulatorLPS, "Saved SimulatorLPS mismatch.");
    }

    // Verifies: [ReqPersistSettingSimulatorV1] (Saving Error Frequency)
    [TestMethod] public void SimulatorErrorFrequency_Set_SavesSettings()
    {
        // Arrange: ViewModel starts with default settings (100.0)
        _mockSettings.ResetSettings();

        // Act
        _viewModel.ToggleSimulatorCommand.Execute(null); // Start simulator mode
        _testContext.Send(_ => { }, null);

        _viewModel.SimulatorErrorFrequency = 50.0;
        _testContext.Send(_ => { }, null);

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(50.0, _mockSettings.SavedSettings?.SimulatorErrorFrequency);
    }

    // Verifies: [ReqPersistSettingSimulatorV1] (Saving Burst Size)
    [TestMethod] public void SimulatorBurstSize_Set_SavesSettings()
    {
        // Arrange: ViewModel starts with default settings (1000.0)
        _mockSettings.ResetSettings();

        // Act
        _viewModel.ToggleSimulatorCommand.Execute(null); // Start simulator mode
        _viewModel.SimulatorBurstSize = 9999.0;
        _testContext.Send(_ => { }, null);

        // Assert
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(9999.0, _mockSettings.SavedSettings?.SimulatorBurstSize);
    }
}
