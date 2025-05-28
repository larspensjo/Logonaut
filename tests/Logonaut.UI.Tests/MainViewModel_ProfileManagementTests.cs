using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Logonaut.Filters;
using Logonaut.Common;
using Logonaut.UI.ViewModels;
using System.Collections.ObjectModel;
using System; // For TimeSpan
using CommunityToolkit.Mvvm.Input;

namespace Logonaut.UI.Tests.ViewModels;

/*
 * Unit tests for MainViewModel focusing on filter profile management.
 * This includes creating, deleting, renaming, and selecting filter profiles.
 * Tests verify that these actions correctly update the ViewModel's state,
 * affect the active filter tree, trigger updates to the filtered log view (via TabViewModel),
 * and persist changes to settings.
 */
[TestClass] public class MainViewModel_ProfileManagementTests : MainViewModelTestBase
{

    [TestInitialize] public override void TestInitialize()
    {
        // Arrange
        base.TestInitialize();
        base.SetupMainAndTabViewModel(); // Sets up _viewModel and _tabViewModel

        var defaultProfileVM = _viewModel.AvailableProfiles.FirstOrDefault(p => p.Name == "Default");
        Assert.IsNotNull(defaultProfileVM, "Default profile VM not found after base initialization.");

        // Ensure "Default" profile has an AndFilter root for tests that add children.
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

        // Simulate saving before checking persistence
        _viewModel.SaveCurrentSettings();

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
        // Arrange: _viewModel starts with "Default"

        // Act
        _viewModel.CreateNewProfileCommand.Execute(null); // Creates "New Profile 1"
        _viewModel.CreateNewProfileCommand.Execute(null); // Creates "New Profile 2"

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
        activeProfile.Name = newValidName; // This triggers OnActiveProfileNameChange via PropertyChanged subscription

        // Simulate saving before checking persistence
        _viewModel.SaveCurrentSettings();

        // Assert
        Assert.AreEqual(newValidName, activeProfile.Name, "VM Name should be updated.");
        Assert.AreEqual(newValidName, activeProfile.Model.Name, "Model Name should be updated.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should have been saved.");
        Assert.AreEqual(newValidName, _mockSettings.SavedSettings?.LastActiveProfileName, "Saved active profile name should be the new name.");
        Assert.AreEqual(1, _mockSettings.SavedSettings?.FilterProfiles.Count, "Saved profile count should be correct."); // Assuming only one profile was present initially ("Default")
        Assert.AreEqual(newValidName, _mockSettings.SavedSettings?.FilterProfiles[0].Name, "Saved profile name in list should be updated.");
    }

    [Ignore("Requires mocking/intercepting MessageBox.Show or abstracting UI interaction")]
    [TestMethod] public void ActiveProfileName_SetToDuplicate_RevertsName_DoesNotSave_ShowsError()
    {
        // Verifies: [ReqFilterProfileRenameInlinev1] (Validation)
        // Arrange: Create a second profile to cause a duplicate name conflict
        _viewModel.CreateNewProfileCommand.Execute(null);
        var profileToRename = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(profileToRename);
        profileToRename.EndRenameCommand.Execute(null); // Exit edit mode if it started

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

    [Ignore("Requires mocking/intercepting MessageBox.Show or abstracting UI interaction")]
    [TestMethod] public void ActiveProfileName_SetToEmpty_RevertsName_DoesNotSave_ShowsError()
    {
        // Verifies: [ReqFilterProfileRenameInlinev1] (Validation)
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

    [Ignore("Requires mocking/intercepting MessageBox.Show")]
    [TestMethod] public void DeleteProfileCommand_RemovesActive_SelectsPrevious_SavesSettings()
    {
        // Verifies: [ReqFilterProfileManageCRUDv1] (Delete), [ReqFilterProfileSelectActivev1]
        // Arrange
        _viewModel.CreateNewProfileCommand.Execute(null); // "New Profile 1"
        _viewModel.CreateNewProfileCommand.Execute(null); // "New Profile 2" (now active)
        int initialCount = _viewModel.AvailableProfiles.Count; // Should be 3 ("Default", "NP1", "NP2")
        var profileToDelete = _viewModel.ActiveFilterProfile;
        Assert.IsNotNull(profileToDelete);
        Assert.AreEqual("New Profile 2", profileToDelete.Name);
        var expectedNextActive = _viewModel.AvailableProfiles[initialCount - 2]; // Should be "New Profile 1"
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

        // Assert Observable Effects on TabViewModel (via MainViewModel delegation)
        // After profile change, TabViewModel should reflect the new filter (or lack thereof).
        // If "New Profile 1" had no filter, FilteredLogLines should be 0 (assuming empty doc).
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "Filtered lines incorrect after profile delete/select.");
        Assert.AreEqual(0, _tabViewModel.FilteredLogLinesCount);

        Assert.Inconclusive("MessageBox verification requires UI testing framework.");
    }

    [Ignore("Requires mocking/intercepting MessageBox.Show")]
    [TestMethod] public void DeleteProfileCommand_DeletesLastProfile_CreatesAndSelectsDefault()
    {
        // Verifies: [ReqFilterProfileManageCRUDv1] (Delete last), [ReqFilterProfileSelectActivev1]
        // Arrange: _viewModel has "Default" profile from setup.
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
        Assert.AreEqual(0, _tabViewModel.FilteredLogLinesCount);

        Assert.Inconclusive("MessageBox verification requires UI testing framework.");
    }

    // Verifies: [ReqFilterProfileSelectActivev1], [ReqFilterDynamicUpdateViewv1], [ReqPersistSettingLastProfilev1]
    [TestMethod] public void ActiveFilterProfile_Set_UpdatesState_TriggersUpdate_SavesSettings()
    {
        // Arrange
        _viewModel.CreateNewProfileCommand.Execute(null); // Creates "New Profile 1" and sets it active
        _testContext.Send(_ => { ((RelayCommand)_viewModel.ActiveFilterProfile!.CancelRenameCommand).Execute(null); }, null); // End edit mode

        var profile1 = _viewModel.AvailableProfiles.First(p => p.Name == "Default"); // Original "Default" profile
        var profile2 = _viewModel.ActiveFilterProfile; // This is "New Profile 1"
        Assert.IsNotNull(profile2);
        Assert.AreEqual("New Profile 1", profile2.Name);
        profile1.SetModelRootFilter(new SubstringFilter("Profile1Filter")); // Give profile1 a filter
        profile2.SetModelRootFilter(null); // Ensure profile2 has no filter for contrast
        _mockSettings.Reset();

        // Act: Switch active profile from profile2 ("New Profile 1") to profile1 ("Default")
        _viewModel.ActiveFilterProfile = profile1;
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow filter application

        // Simulate saving before checking persistence
        _viewModel.SaveCurrentSettings();

        // Assert ViewModel State
        Assert.AreSame(profile1, _viewModel.ActiveFilterProfile);
        Assert.IsNotNull(profile1.RootFilterViewModel, "Profile1's Root VM should not be null.");
        Assert.AreEqual(1, _viewModel.ActiveTreeRootNodes.Count, "ActiveTreeRootNodes count mismatch.");
        Assert.AreSame(profile1.RootFilterViewModel, _viewModel.ActiveTreeRootNodes[0]);
        Assert.IsNull(_viewModel.SelectedFilterNode);

        // Assert Observable Effects
        // Since "Default" (profile1) now has a filter and TabViewModel's LogDoc is empty, 0 lines expected.
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "Filtered lines incorrect after profile selection.");
        Assert.AreEqual(0, _tabViewModel.FilteredLogLinesCount);

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should be saved on profile change.");
        Assert.AreEqual(profile1.Name, _mockSettings.SavedSettings?.LastActiveProfileName, "Last active profile name should be saved.");
    }
}
