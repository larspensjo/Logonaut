using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Logonaut.Filters;
using Logonaut.Common;

namespace Logonaut.UI.Tests.ViewModels;

/// Tests related to creating, deleting, renaming, selecting filter profiles
[TestClass]
public class MainViewModel_ProfileManagementTests : MainViewModelTestBase
{
    // Verifies: [ReqCreateMultipleFilterProfilesv1], [ReqFilterProfileNamingv1], [ReqFilterProfileSelectActivev1],
    //           [ReqPersistSettingFilterProfilesv1], [ReqPersistSettingLastProfilev1]
    [TestMethod] public void CreateNewProfileCommand_AddsUniqueProfile_SetsActive_SavesSettings()
    {
        int initialCount = _viewModel.AvailableProfiles.Count;
        _viewModel.CreateNewProfileCommand.Execute(null);
        Assert.AreEqual(initialCount + 1, _viewModel.AvailableProfiles.Count);
        var newProfile = _viewModel.AvailableProfiles.Last();
        Assert.AreEqual("New Profile 1", newProfile.Name);
        Assert.AreSame(newProfile, _viewModel.ActiveFilterProfile);
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(newProfile.Name, _mockSettings.SavedSettings?.LastActiveProfileName);
        Assert.AreEqual(initialCount + 1, _mockSettings.SavedSettings?.FilterProfiles.Count);
        Assert.IsTrue(_mockProcessor.UpdateFilterSettingsCallCount > 1); // Initial + after create/select
        Assert.IsInstanceOfType(_mockProcessor.LastFilterSettings?.Filter, typeof(TrueFilter)); // New profile has no filter initially
    }

    // Verifies: [ReqCreateMultipleFilterProfilesv1], [ReqFilterProfileNamingv1] (uniqueness aspect)
    [TestMethod] public void CreateNewProfileCommand_GeneratesUniqueName()
    {
        _viewModel.CreateNewProfileCommand.Execute(null); // "New Profile 1"
        _viewModel.CreateNewProfileCommand.Execute(null); // "New Profile 2"
        Assert.AreEqual(3, _viewModel.AvailableProfiles.Count);
        Assert.IsNotNull(_viewModel.AvailableProfiles.FirstOrDefault(p => p.Name == "New Profile 1"));
        Assert.IsNotNull(_viewModel.AvailableProfiles.FirstOrDefault(p => p.Name == "New Profile 2"));
        Assert.AreEqual("New Profile 2", _viewModel.ActiveFilterProfile?.Name);
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
    [Ignore("Requires mocking/intercepting MessageBox.Show or abstracting UI interaction")]
    [TestMethod] public void ActiveProfileName_SetToDuplicate_RevertsName_DoesNotSave_ShowsError()
    {
        // Arrange: Create a second profile to cause a duplicate name conflict
        _viewModel.CreateNewProfileCommand.Execute(null); // Creates "New Profile 1" and selects it
        var profileToRename = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(profileToRename);
        Assert.AreEqual("New Profile 1", profileToRename.Name);

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
    [Ignore("Requires mocking/intercepting MessageBox.Show")]
    [TestMethod] public void DeleteProfileCommand_RemovesActive_SelectsPrevious_SavesSettings()
    {
        _viewModel.CreateNewProfileCommand.Execute(null);
        _viewModel.CreateNewProfileCommand.Execute(null);
        int initialCount = _viewModel.AvailableProfiles.Count;
        var profileToDelete = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(profileToDelete);
        var expectedNextActive = _viewModel.AvailableProfiles[initialCount - 2];
        _viewModel.DeleteProfileCommand.Execute(null); // Assumes user confirms MessageBox
        _testContext.Send(_ => { }, null); // Allow save

        Assert.AreEqual(initialCount - 1, _viewModel.AvailableProfiles.Count);
        Assert.IsFalse(_viewModel.AvailableProfiles.Contains(profileToDelete));
        Assert.IsNotNull(_viewModel.ActiveFilterProfile);
        Assert.AreSame(expectedNextActive, _viewModel.ActiveFilterProfile);
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual(expectedNextActive.Name, _mockSettings.SavedSettings?.LastActiveProfileName);
        Assert.AreEqual(initialCount - 1, _mockSettings.SavedSettings?.FilterProfiles.Count);
        Assert.IsTrue(_mockProcessor.UpdateFilterSettingsCallCount > 0); // Triggered by selection change
        Assert.AreSame(expectedNextActive.Model.RootFilter ?? new TrueFilter(), _mockProcessor.LastFilterSettings?.Filter);
        Assert.Inconclusive("MessageBox verification requires UI testing framework.");
    }

    // Verifies: [ReqFilterProfileManageCRUDv1] (Delete last), [ReqFilterProfileSelectActivev1]
    [Ignore("Requires mocking/intercepting MessageBox.Show")]
    [TestMethod]public void DeleteProfileCommand_DeletesLastProfile_CreatesAndSelectsDefault()
    {
        Assert.AreEqual(1, _viewModel.AvailableProfiles.Count);
        var lastProfile = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(lastProfile);
        _viewModel.DeleteProfileCommand.Execute(null); // Assumes user confirms MessageBox
        _testContext.Send(_ => { }, null); // Allow save

        Assert.AreEqual(1, _viewModel.AvailableProfiles.Count);
        var newDefaultProfile = _viewModel.AvailableProfiles[0];
        Assert.IsNotNull(newDefaultProfile);
        Assert.AreNotSame(lastProfile, newDefaultProfile);
        Assert.AreEqual("Default", newDefaultProfile.Name);
        Assert.IsNull(newDefaultProfile.Model.RootFilter);
        Assert.AreSame(newDefaultProfile, _viewModel.ActiveFilterProfile);
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.AreEqual("Default", _mockSettings.SavedSettings?.LastActiveProfileName);
        Assert.IsTrue(_mockProcessor.UpdateFilterSettingsCallCount > 0); // Triggered by selection change
        Assert.IsInstanceOfType(_mockProcessor.LastFilterSettings?.Filter, typeof(TrueFilter));
        Assert.Inconclusive("MessageBox verification requires UI testing framework.");
    }

    // Verifies: [ReqFilterProfileSelectActivev1], [ReqFilterDynamicUpdateViewv1], [ReqPersistSettingLastProfilev1]
    [TestMethod] public void ActiveFilterProfile_Set_UpdatesState_TriggersProcessor_SavesSettings()
    {
        _viewModel.CreateNewProfileCommand.Execute(null); // Adds "New Profile 1", saves
        var profile1 = _viewModel.AvailableProfiles[0]; // "Default"
        var profile2 = _viewModel.ActiveFilterProfile; // "New Profile 1"
        Assert.IsNotNull(profile2);
        _mockProcessor.ResetCounters();
        _mockSettings.ResetSettings();

        // Act: Select the first profile ("Default")
        _viewModel.ActiveFilterProfile = profile1;
        _testContext.Send(_ => { }, null); // Allow save

        // Assert
        Assert.AreSame(profile1, _viewModel.ActiveFilterProfile);
        Assert.AreEqual(0, _viewModel.ActiveTreeRootNodes.Count, "Default profile initially has no root VM");
        Assert.IsNull(_viewModel.SelectedFilterNode);
        Assert.AreEqual(1, _mockProcessor.UpdateFilterSettingsCallCount, "Processor should be updated on profile change.");
        Assert.IsInstanceOfType(_mockProcessor.LastFilterSettings?.Filter, typeof(TrueFilter), "Filter should be TrueFilter for empty default profile.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should be saved on profile change.");
        Assert.AreEqual(profile1.Name, _mockSettings.SavedSettings?.LastActiveProfileName, "Last active profile name should be saved.");
    }
}
