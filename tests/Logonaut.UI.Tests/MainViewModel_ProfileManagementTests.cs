using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Logonaut.Filters;
using Logonaut.Common;
using Logonaut.UI.ViewModels;
using System.Collections.ObjectModel;
using System; // For TimeSpan
using CommunityToolkit.Mvvm.Input;

namespace Logonaut.UI.Tests.ViewModels;

[TestClass] public class MainViewModel_ProfileManagementTests : MainViewModelTestBase
{

    [TestInitialize] public override void TestInitialize()
    {
        // Arrange
        base.TestInitialize();

        var defaultProfileVM = _viewModel.AvailableProfiles.FirstOrDefault(p => p.Name == "Default");
        Assert.IsNotNull(defaultProfileVM, "Default profile VM not found after base initialization.");

        if (defaultProfileVM.Model.RootFilter is not AndFilter)
        {
            defaultProfileVM.SetModelRootFilter(new AndFilter());
        }
        if (_viewModel.ActiveFilterProfile != defaultProfileVM)
        {
            _viewModel.ActiveFilterProfile = defaultProfileVM;
        }
        _mockSettings?.Reset();
    }

    [TestMethod] public void CreateNewProfileCommand_AddsUniqueProfile_SetsActive_SavesSettings()
    {
        // Arrange
        int initialCount = _viewModel.AvailableProfiles.Count;
        _mockSettings.Reset();

        // Act
        _viewModel.CreateNewProfileCommand.Execute(null);

        // Assert ViewModel State
        Assert.AreEqual(initialCount + 1, _viewModel.AvailableProfiles.Count);
        var newProfile = _viewModel.AvailableProfiles.Last();
        Assert.AreEqual("New Profile 1", newProfile.Name);
        Assert.AreSame(newProfile, _viewModel.ActiveFilterProfile, "Newly created profile should be active.");
        Assert.IsTrue(newProfile.IsEditing, "Newly created profile should be in edit mode.");

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
        Assert.AreEqual(newProfile.Name, _mockSettings.SavedSettings?.LastActiveProfileName);
        Assert.AreEqual(initialCount + 1, _mockSettings.SavedSettings?.FilterProfiles.Count);
        var savedNewProfile = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "New Profile 1");
        Assert.IsNotNull(savedNewProfile, "New profile not found in saved settings.");
        Assert.IsNull(savedNewProfile.RootFilter, "New profile's saved filter should be null.");
    }

    // Verifies: [ReqCreateMultipleFilterProfilesv1], [ReqFilterProfileNamingv1] (uniqueness aspect)
    [TestMethod] public void CreateNewProfileCommand_GeneratesUniqueName()
    {
        // Arrange: ViewModel starts with "Default"

        // Act
        _viewModel.CreateNewProfileCommand.Execute(null);
        _viewModel.CreateNewProfileCommand.Execute(null);

        // Assert
        Assert.AreEqual(3, _viewModel.AvailableProfiles.Count);
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
        _mockSettings.Reset();

        // Act
        activeProfile.Name = newValidName;

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
        var profileToRename = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(profileToRename);
        Assert.AreEqual("New Profile 1", profileToRename.Name);
        // Manually exit edit mode if needed for setup clarity
        profileToRename.EndRenameCommand.Execute(null);

        string originalName = profileToRename.Name;
        string duplicateName = "Default"; // Name of the first profile
        _mockSettings.Reset();

        // Act: Simulate setting the Name property to a duplicate value
        profileToRename.Name = duplicateName;

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
        _mockSettings.Reset();

        // Act: Simulate setting the Name property to an empty/whitespace value
        activeProfile.Name = "   "; // Whitespace

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
        _viewModel.CreateNewProfileCommand.Execute(null);
        _viewModel.CreateNewProfileCommand.Execute(null);
        int initialCount = _viewModel.AvailableProfiles.Count;
        var profileToDelete = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(profileToDelete);
        Assert.AreEqual("New Profile 2", profileToDelete.Name);
        var expectedNextActive = _viewModel.AvailableProfiles[initialCount - 2];
        _mockSettings.Reset();

        // Act
        _viewModel.DeleteProfileCommand.Execute(null);

        // Assert ViewModel State
        Assert.AreEqual(initialCount - 1, _viewModel.AvailableProfiles.Count);
        Assert.IsFalse(_viewModel.AvailableProfiles.Contains(profileToDelete));
        Assert.IsNotNull(_viewModel.ActiveFilterProfile);
        Assert.AreSame(expectedNextActive, _viewModel.ActiveFilterProfile);

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
        Assert.AreEqual(expectedNextActive.Name, _mockSettings.SavedSettings?.LastActiveProfileName);
        Assert.AreEqual(initialCount - 1, _mockSettings.SavedSettings?.FilterProfiles.Count);

        // Assert Observable Effects (FilteredLogLines is on TabViewModel, accessed via MainViewModel delegate)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "Filtered lines incorrect after profile delete/select.");

        Assert.Inconclusive("MessageBox verification requires UI testing framework.");
    }

    // Verifies: [ReqFilterProfileManageCRUDv1] (Delete last), [ReqFilterProfileSelectActivev1]
    // Keep Ignore as it relies on UI feedback mechanism not mocked here
    [Ignore("Requires mocking/intercepting MessageBox.Show")]
    [TestMethod] public void DeleteProfileCommand_DeletesLastProfile_CreatesAndSelectsDefault()
    {
        // Arrange
        Assert.AreEqual(1, _viewModel.AvailableProfiles.Count);
        var lastProfile = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(lastProfile);
        Assert.AreEqual("Default", lastProfile.Name);
        _mockSettings.Reset();

        // Act
        _viewModel.DeleteProfileCommand.Execute(null);

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

        // Assert Observable Effects
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "Filtered lines incorrect after deleting last profile.");

        Assert.Inconclusive("MessageBox verification requires UI testing framework.");
    }

    // Verifies: [ReqFilterProfileSelectActivev1], [ReqFilterDynamicUpdateViewv1], [ReqPersistSettingLastProfilev1]
    [TestMethod] public void ActiveFilterProfile_Set_UpdatesState_TriggersUpdate_SavesSettings()
    {
        // Arrange
        _viewModel.CreateNewProfileCommand.Execute(null); // Creates "New Profile 1" and sets it active
        _testContext.Send(_ => { ((RelayCommand)_viewModel.ActiveFilterProfile!.CancelRenameCommand).Execute(null); }, null); // End edit mode

        var profile1 = _viewModel.AvailableProfiles.First(p => p.Name == "Default");
        var profile2 = _viewModel.ActiveFilterProfile; // This is "New Profile 1"
        Assert.IsNotNull(profile2);
        Assert.AreEqual("New Profile 1", profile2.Name);
        _mockSettings.Reset();

        // Act
        _viewModel.ActiveFilterProfile = profile1; // Switch to "Default" profile
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        // Assert ViewModel State
        Assert.AreSame(profile1, _viewModel.ActiveFilterProfile);
        // "Default" profile has an AndFilter root from TestInitialize.
        Assert.AreEqual(1, _viewModel.ActiveTreeRootNodes.Count, "Default profile (profile1) has an AndFilter root, so tree should have 1 node.");
        Assert.IsNotNull(_viewModel.ActiveTreeRootNodes[0]);
        Assert.AreSame(profile1.RootFilterViewModel, _viewModel.ActiveTreeRootNodes[0]);
        Assert.IsNull(_viewModel.SelectedFilterNode);

        // Assert Observable Effects
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "Filtered lines incorrect after profile selection.");

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should be saved on profile change.");
        Assert.AreEqual(profile1.Name, _mockSettings.SavedSettings?.LastActiveProfileName, "Last active profile name should be saved.");
    }
}
