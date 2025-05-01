// tests/Logonaut.UI.Tests/MainViewModel_ProfileManagementTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Logonaut.Filters;
using Logonaut.Common;
using Logonaut.UI.ViewModels; // Explicitly use ViewModels namespace

namespace Logonaut.UI.Tests.ViewModels;

/// <summary>
/// Tests related to creating, deleting, renaming, selecting filter profiles.
/// Verifies ViewModel state changes and persistence.
/// </summary>
[TestClass] public class MainViewModel_ProfileManagementTests : MainViewModelTestBase // Inherits mocks, VM setup
{
    // Verifies: [ReqCreateMultipleFilterProfilesv1], [ReqFilterProfileNamingv1], [ReqFilterProfileSelectActivev1],
    //           [ReqPersistSettingFilterProfilesv1], [ReqPersistSettingLastProfilev1]
    [TestMethod] public void CreateNewProfileCommand_AddsUniqueProfile_SetsActive_SavesSettings()
    {
        // Arrange
        int initialCount = _viewModel.AvailableProfiles.Count;
        _mockSettings.ResetSettings(); // Clear save status

        // Act
        _viewModel.CreateNewProfileCommand.Execute(null);
        _testContext.Send(_ => { }, null); // Process potential posts like BeginEdit and Save

        // Assert ViewModel State
        Assert.AreEqual(initialCount + 1, _viewModel.AvailableProfiles.Count);
        var newProfile = _viewModel.AvailableProfiles.Last();
        Assert.AreEqual("New Profile 1", newProfile.Name);
        Assert.AreSame(newProfile, _viewModel.ActiveFilterProfile, "Newly created profile should be active.");
        Assert.IsTrue(newProfile.IsEditing, "Newly created profile should be in edit mode."); // Check edit mode

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
        Assert.AreEqual(newProfile.Name, _mockSettings.SavedSettings?.LastActiveProfileName);
        Assert.AreEqual(initialCount + 1, _mockSettings.SavedSettings?.FilterProfiles.Count);
        // Assert that the new profile has a null filter initially in the saved settings
        var savedNewProfile = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "New Profile 1");
        Assert.IsNotNull(savedNewProfile, "New profile not found in saved settings.");
        Assert.IsNull(savedNewProfile.RootFilter, "New profile's saved filter should be null.");
    }

    // Verifies: [ReqCreateMultipleFilterProfilesv1], [ReqFilterProfileNamingv1] (uniqueness aspect)
    [TestMethod] public void CreateNewProfileCommand_GeneratesUniqueName()
    {
        // Arrange: ViewModel starts with "Default"

        // Act
        _viewModel.CreateNewProfileCommand.Execute(null); // Creates "New Profile 1"
        _testContext.Send(_ => { }, null);
        _viewModel.CreateNewProfileCommand.Execute(null); // Creates "New Profile 2"
        _testContext.Send(_ => { }, null);

        // Assert
        Assert.AreEqual(3, _viewModel.AvailableProfiles.Count); // Default, NP1, NP2
        Assert.IsNotNull(_viewModel.AvailableProfiles.FirstOrDefault(p => p.Name == "Default"));
        Assert.IsNotNull(_viewModel.AvailableProfiles.FirstOrDefault(p => p.Name == "New Profile 1"));
        Assert.IsNotNull(_viewModel.AvailableProfiles.FirstOrDefault(p => p.Name == "New Profile 2"));
        Assert.AreEqual("New Profile 2", _viewModel.ActiveFilterProfile?.Name, "Last created profile should be active.");
    }

    // Verifies: [ReqFilterProfileRenameInlinev1], [ReqPersistSettingFilterProfilesv1], [ReqPersistSettingLastProfilev1]
    [TestMethod] public void ActiveProfileName_SetToValid_UpdatesModel_SavesSettings()
    {
        // Arrange
        var activeProfile = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(activeProfile, "Need an active profile to test rename.");
        string oldName = activeProfile.Name;
        string newValidName = "Renamed Profile";
        _mockSettings.ResetSettings(); // Clear previous save status

        // Act: Simulate the Name property being changed via binding (which triggers internal handler)
        activeProfile.Name = newValidName;
        _testContext.Send(_ => { }, null); // Allow handler and save to run

        // Assert
        Assert.AreEqual(newValidName, activeProfile.Name, "VM Name should be updated.");
        Assert.AreEqual(newValidName, activeProfile.Model.Name, "Model Name should be updated.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.AreEqual(newValidName, _mockSettings.SavedSettings?.LastActiveProfileName, "Saved active profile name should be the new name.");
        Assert.AreEqual(1, _mockSettings.SavedSettings?.FilterProfiles.Count, "Saved profile count should be correct.");
        Assert.AreEqual(newValidName, _mockSettings.SavedSettings?.FilterProfiles[0].Name, "Saved profile name in list should be updated.");
    }

    // Verifies: [ReqFilterProfileRenameInlinev1] (Validation)
    // Keep Ignore as it relies on UI feedback mechanism not mocked here
    [Ignore("Requires mocking/intercepting MessageBox.Show or abstracting UI interaction")]
    [TestMethod] public void ActiveProfileName_SetToDuplicate_RevertsName_DoesNotSave_ShowsError()
    {
        // Arrange: Create a second profile to cause a duplicate name conflict
        _viewModel.CreateNewProfileCommand.Execute(null); // Creates "New Profile 1" and selects it
        _testContext.Send(_ => { }, null);
        var profileToRename = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(profileToRename);
        Assert.AreEqual("New Profile 1", profileToRename.Name);
        // Manually exit edit mode if needed for setup clarity
        profileToRename.EndRenameCommand.Execute(null);

        string originalName = profileToRename.Name;
        string duplicateName = "Default"; // Name of the first profile
        _mockSettings.ResetSettings();

        // Act: Simulate setting the Name property to a duplicate value
        profileToRename.Name = duplicateName;
        _testContext.Send(_ => { }, null); // Allow handler to run

        // Assert
        Assert.AreEqual(originalName, profileToRename.Name, "VM Name should be reverted back to the original.");
        Assert.AreEqual(originalName, profileToRename.Model.Name, "Model Name should remain the original name.");
        Assert.IsNull(_mockSettings.SavedSettings, "Settings should NOT have been saved due to validation failure.");
        Assert.Inconclusive("Verification of error message requires UI interaction abstraction/mocking.");
    }

    // Verifies: [ReqFilterProfileRenameInlinev1] (Validation)
    // Keep Ignore as it relies on UI feedback mechanism not mocked here
    [Ignore("Requires mocking/intercepting MessageBox.Show or abstracting UI interaction")]
    [TestMethod] public void ActiveProfileName_SetToEmpty_RevertsName_DoesNotSave_ShowsError()
    {
        // Arrange
        var activeProfile = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(activeProfile);
        string originalName = activeProfile.Name;
        _mockSettings.ResetSettings();

        // Act: Simulate setting the Name property to an empty/whitespace value
        activeProfile.Name = "   "; // Whitespace
        _testContext.Send(_ => { }, null); // Allow handler to run

        // Assert
        Assert.AreEqual(originalName, activeProfile.Name, "VM Name should be reverted back to the original.");
        Assert.AreEqual(originalName, activeProfile.Model.Name, "Model Name should remain the original name.");
        Assert.IsNull(_mockSettings.SavedSettings, "Settings should NOT have been saved due to validation failure.");
        Assert.Inconclusive("Verification of error message requires UI interaction abstraction/mocking.");
    }

    // Verifies: [ReqFilterProfileManageCRUDv1] (Delete), [ReqFilterProfileSelectActivev1]
    // Keep Ignore as it relies on UI feedback mechanism not mocked here
    [Ignore("Requires mocking/intercepting MessageBox.Show")]
    [TestMethod] public void DeleteProfileCommand_RemovesActive_SelectsPrevious_SavesSettings()
    {
        // Arrange
        _viewModel.CreateNewProfileCommand.Execute(null); _testContext.Send(_ => { }, null); // NP1
        _viewModel.CreateNewProfileCommand.Execute(null); _testContext.Send(_ => { }, null); // NP2 (Active)
        int initialCount = _viewModel.AvailableProfiles.Count; // Should be 3
        var profileToDelete = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(profileToDelete);
        Assert.AreEqual("New Profile 2", profileToDelete.Name);
        var expectedNextActive = _viewModel.AvailableProfiles[initialCount - 2]; // Should be NP1
        _mockSettings.ResetSettings();

        // Act
        _viewModel.DeleteProfileCommand.Execute(null); // Assumes user confirms MessageBox
        _testContext.Send(_ => { }, null); // Allow save and state updates

        // Assert ViewModel State
        Assert.AreEqual(initialCount - 1, _viewModel.AvailableProfiles.Count);
        Assert.IsFalse(_viewModel.AvailableProfiles.Contains(profileToDelete));
        Assert.IsNotNull(_viewModel.ActiveFilterProfile);
        Assert.AreSame(expectedNextActive, _viewModel.ActiveFilterProfile);

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
        Assert.AreEqual(expectedNextActive.Name, _mockSettings.SavedSettings?.LastActiveProfileName);
        Assert.AreEqual(initialCount - 1, _mockSettings.SavedSettings?.FilterProfiles.Count);

        // Assert Observable Effects (Filter should be updated based on new selection)
        // We expect FilteredLogLines to be empty because doc is empty and filter is null for NP1
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "Filtered lines incorrect after profile delete/select.");

        Assert.Inconclusive("MessageBox verification requires UI testing framework.");
    }

    // Verifies: [ReqFilterProfileManageCRUDv1] (Delete last), [ReqFilterProfileSelectActivev1]
    // Keep Ignore as it relies on UI feedback mechanism not mocked here
    [Ignore("Requires mocking/intercepting MessageBox.Show")]
    [TestMethod]public void DeleteProfileCommand_DeletesLastProfile_CreatesAndSelectsDefault()
    {
        // Arrange
        Assert.AreEqual(1, _viewModel.AvailableProfiles.Count);
        var lastProfile = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(lastProfile);
        Assert.AreEqual("Default", lastProfile.Name);
        _mockSettings.ResetSettings();

        // Act
        _viewModel.DeleteProfileCommand.Execute(null); // Assumes user confirms MessageBox
        _testContext.Send(_ => { }, null); // Allow save and state updates

        // Assert ViewModel State
        Assert.AreEqual(1, _viewModel.AvailableProfiles.Count);
        var newDefaultProfile = _viewModel.AvailableProfiles[0];
        Assert.IsNotNull(newDefaultProfile);
        Assert.AreNotSame(lastProfile, newDefaultProfile, "A new default profile instance should be created.");
        Assert.AreEqual("Default", newDefaultProfile.Name);
        Assert.IsNull(newDefaultProfile.Model.RootFilter);
        Assert.AreSame(newDefaultProfile, _viewModel.ActiveFilterProfile);

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual("Default", _mockSettings.SavedSettings?.LastActiveProfileName);
        Assert.AreEqual(1, _mockSettings.SavedSettings?.FilterProfiles.Count);
        Assert.AreEqual("Default", _mockSettings.SavedSettings?.FilterProfiles[0].Name);
        Assert.IsNull(_mockSettings.SavedSettings?.FilterProfiles[0].RootFilter);

        // Assert Observable Effects (Filter should update to TrueFilter for empty default)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "Filtered lines incorrect after deleting last profile.");

        Assert.Inconclusive("MessageBox verification requires UI testing framework.");
    }

    // Verifies: [ReqFilterProfileSelectActivev1], [ReqFilterDynamicUpdateViewv1], [ReqPersistSettingLastProfilev1]
    [TestMethod] public void ActiveFilterProfile_Set_UpdatesState_TriggersUpdate_SavesSettings()
    {
        // Arrange
        _viewModel.CreateNewProfileCommand.Execute(null); // Adds "New Profile 1", saves
        _testContext.Send(_ => { }, null);
        var profile1 = _viewModel.AvailableProfiles.First(p => p.Name == "Default"); // Get "Default"
        var profile2 = _viewModel.ActiveFilterProfile; // Currently "New Profile 1"
        Assert.IsNotNull(profile2);
        Assert.AreEqual("New Profile 1", profile2.Name);
        _mockSettings.ResetSettings();

        // Act: Select the first profile ("Default")
        _viewModel.ActiveFilterProfile = profile1;
        _testContext.Send(_ => { }, null); // Allow save and state/filter update

        // Assert ViewModel State
        Assert.AreSame(profile1, _viewModel.ActiveFilterProfile);
        Assert.AreEqual(0, _viewModel.ActiveTreeRootNodes.Count, "Default profile initially has no root VM");
        Assert.IsNull(_viewModel.SelectedFilterNode);

        // Assert Observable Effects (Filter should update to TrueFilter for empty default profile)
         Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "Filtered lines incorrect after profile selection.");

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should be saved on profile change.");
        Assert.AreEqual(profile1.Name, _mockSettings.SavedSettings?.LastActiveProfileName, "Last active profile name should be saved.");
    }
}
