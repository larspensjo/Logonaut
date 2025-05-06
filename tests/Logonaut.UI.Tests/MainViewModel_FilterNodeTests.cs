// tests/Logonaut.UI.Tests/MainViewModel_FilterNodeTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Logonaut.Filters;
using Logonaut.UI.ViewModels; // Required for FilterViewModel
using Logonaut.Core; // For TrueFilter etc.
using Logonaut.Common; // For FilterProfile

namespace Logonaut.UI.Tests.ViewModels;

/// <summary>
/// Tests related to adding, removing, editing filter nodes within the active profile.
/// Verifies ViewModel state changes and persistence.
/// </summary>
[TestClass] public class MainViewModel_FilterNodeTests : MainViewModelTestBase // Inherit from the updated base
{
    // Note: _viewModel is created in the base TestInitialize
    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize(); // Call the base TestInitialize - this loads the initial "Default" profile

        // --- Get the existing default profile loaded during base setup ---
        var defaultProfileVM = _viewModel.AvailableProfiles.FirstOrDefault(p => p.Name == "Default");

        // --- Ensure it exists and has the desired root (or set it) ---
        if (defaultProfileVM == null)
        {
            // This case should ideally not happen if base setup is correct, but handle defensively.
            Assert.Inconclusive("Default profile VM not found after base initialization.");
            // Or, create it if absolutely necessary for the tests, but ensure no duplicates:
            // var defaultProfileModel = new FilterProfile("Default", new AndFilter());
            // defaultProfileVM = new FilterProfileViewModel(defaultProfileModel, _viewModel);
            // _viewModel.AvailableProfiles.Add(defaultProfileVM);
            // _viewModel.ActiveFilterProfile = defaultProfileVM; // Set it now
        }
        else
        {
            // Ensure the existing default profile's model has an AndFilter root for these tests
            if (defaultProfileVM.Model.RootFilter is not AndFilter)
            {
                // If the loaded default didn't have an AndFilter, set it now.
                // This modification might trigger saves depending on how FilterProfileViewModel notifies,
                // so do it *before* setting ActiveFilterProfile again if that triggers saves.
                 defaultProfileVM.SetModelRootFilter(new AndFilter()); // Use the VM's method
                 // We might need to flush context if SetModelRootFilter causes posted actions
                 _testContext?.Send(_ => {}, null);
            }
            // Ensure this profile is the active one for the tests
             if (_viewModel.ActiveFilterProfile != defaultProfileVM)
             {
                 _viewModel.ActiveFilterProfile = defaultProfileVM;
                 // Setting ActiveFilterProfile might trigger saves, let TestContext handle flush later if needed
             }
        }

         // Reset mock settings AFTER ensuring the profile state is correct for the tests
         _mockSettings?.Reset();
    }


    // Verifies: [ReqFilterNodeManageButtonsv1] replaced by [ReqDnDFilterManageV1] (Add Root)
    // Verifies: [ReqFilterDynamicUpdateViewv1] (Triggered by add)
    // Verifies: [ReqPersistSettingFilterProfilesv1] (Saving after add)
    [TestMethod] public void AddFilterCommand_EmptyProfile_SetsRoot_Selects_UpdatesAndSaves()
    {
        // Arrange: Ensure profile is empty first
        var emptyProfileModel = new FilterProfile("Empty", null);
        var emptyProfileVM = new FilterProfileViewModel(emptyProfileModel, _viewModel);
        _viewModel.AvailableProfiles.Clear();
        _viewModel.AvailableProfiles.Add(emptyProfileVM);
        _viewModel.ActiveFilterProfile = emptyProfileVM; // Make it active

        Assert.IsNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Pre-condition: Root VM should be null.");
        _mockSettings.Reset(); // Use Reset from base class

        // Act
        _viewModel.AddFilterCommand.Execute("Substring"); // This sets root directly
        _testContext.Send(_ => { }, null); // Flush context queue for update and save

        // Assert ViewModel State
        Assert.IsNotNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Root VM should be created.");
        var rootVM = _viewModel.ActiveFilterProfile.RootFilterViewModel;
        Assert.IsInstanceOfType(rootVM.Filter, typeof(SubstringFilter), "Root filter type mismatch.");
        Assert.AreEqual(1, _viewModel.ActiveTreeRootNodes.Count, "ActiveTreeRootNodes count mismatch.");
        Assert.AreSame(rootVM, _viewModel.ActiveTreeRootNodes[0], "ActiveTreeRootNodes content mismatch.");
        Assert.AreSame(rootVM, _viewModel.SelectedFilterNode, "New root should be selected.");

        // Assert Observable Effects (Filter update triggered directly)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount should be 0.");

        // Assert Persistence (Save triggered directly)
        Assert.IsTrue(_mockSettings.SaveCalledCount > 0, "Settings were not saved.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Saved settings is null.");
        Assert.AreEqual("Empty", _mockSettings.SavedSettings?.LastActiveProfileName);
        Assert.IsNotNull(_mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "Empty")?.RootFilter, "Saved root filter is null.");
        Assert.IsInstanceOfType(_mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "Empty")?.RootFilter, typeof(SubstringFilter), "Saved filter type mismatch.");
        Assert.IsFalse(_viewModel.UndoCommand.CanExecute(null), "Undo should NOT be enabled for direct root set."); // Verify bypass
    }

    // Verifies: [ReqFilterNodeManageButtonsv1] replaced by [ReqDnDFilterManageV1] (Add Child)
    // Verifies: [ReqFilterDynamicUpdateViewv1] (Triggered by Execute)
    // Verifies: [ReqPersistSettingFilterProfilesv1] (Saving via Execute)
    [TestMethod]
    public void AddFilterCommand_CompositeSelected_ExecutesAddAction_UpdatesAndSaves()
    {
        // Arrange: Uses profile from TestInitialize (already has AND root)
        var root = _viewModel.ActiveFilterProfile?.RootFilterViewModel;
        Assert.IsNotNull(root, "Setup failed: Root And node not found.");
        _viewModel.SelectedFilterNode = root; // Select the root
        _mockSettings.Reset();

        // Act: Add Regex as child (this uses Execute)
        _viewModel.AddFilterCommand.Execute("Regex");
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert ViewModel State
        Assert.AreEqual(1, root.Children.Count, "Child count mismatch.");
        var child = root.Children[0];
        Assert.IsInstanceOfType(child.Filter, typeof(RegexFilter), "Child filter type mismatch.");
        Assert.AreSame(child, _viewModel.SelectedFilterNode, "New child should be selected.");

        // Assert Observable Effects (Filter update triggered via Execute)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount should be 0.");

        // Assert Persistence (Save triggered via Execute)
        // Assert Persistence (Save triggered via Execute)
        Assert.IsTrue(_mockSettings.SaveCalledCount > 0, "Settings were not saved.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Saved settings object is null."); // Verify SavedSettings itself isn't null

        // --- Add Debug Assertions ---
        var savedProfile = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "Default");
        Assert.IsNotNull(savedProfile, "[Debug] Saved profile 'Default' not found in saved settings.");
        Assert.IsNotNull(savedProfile.RootFilter, "[Debug] Saved profile 'Default' has a null RootFilter.");
        Assert.IsInstanceOfType(savedProfile.RootFilter, typeof(AndFilter), $"[Debug] Saved profile 'Default' RootFilter is type {savedProfile.RootFilter?.GetType().Name}, expected AndFilter.");
        // --- End Debug Assertions ---

        var savedRoot = savedProfile?.RootFilter as AndFilter; // Keep original assertion line
        Assert.IsNotNull(savedRoot, "Saved root filter is null or not AndFilter."); // Original failing assertion
        Assert.AreEqual(1, savedRoot.SubFilters.Count, "Saved child count mismatch.");
        Assert.IsInstanceOfType(savedRoot.SubFilters[0], typeof(RegexFilter), "Saved child filter type mismatch.");
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null), "Undo should be enabled."); // Verify undo possible
    }

    // Verifies: [ReqFilterNodeManageButtonsv1] replaced by [ReqDnDFilterManageV1] (Remove Root)
    // Verifies: [ReqFilterDynamicUpdateViewv1] (Triggered by remove)
    // Verifies: [ReqPersistSettingFilterProfilesv1] (Saving after remove)
    [TestMethod]
    public void RemoveFilterNodeCommand_RootSelected_ClearsTree_UpdatesAndSaves()
    {
        // Arrange: Uses profile from TestInitialize (already has AND root)
        var root = _viewModel.ActiveFilterProfile?.RootFilterViewModel;
        Assert.IsNotNull(root, "Setup failed: Root node not found.");
        _viewModel.SelectedFilterNode = root;
        _mockSettings.Reset();

        // Act: Remove the root (this bypasses Execute)
        _viewModel.RemoveFilterNodeCommand.Execute(null);
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert ViewModel State
        Assert.IsNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Root VM should be null after removal.");
        Assert.AreEqual(0, _viewModel.ActiveTreeRootNodes.Count, "ActiveTreeRootNodes should be empty.");
        Assert.IsNull(_viewModel.SelectedFilterNode, "Selected node should be null.");

        // Assert Observable Effects (Filter update triggered directly)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount should be 0.");

        // Assert Persistence (Save triggered directly)
        Assert.IsTrue(_mockSettings.SaveCalledCount > 0, "Settings were not saved.");
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.IsNull(_mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "Default")?.RootFilter, "Saved root filter should be null.");
        Assert.IsFalse(_viewModel.UndoCommand.CanExecute(null), "Undo should NOT be enabled for direct root removal."); // Verify bypass
    }

    // Verifies: [ReqFilterNodeManageButtonsv1] replaced by [ReqDnDFilterManageV1] (Remove Child)
    // Verifies: [ReqFilterDynamicUpdateViewv1] (Triggered by Execute)
    // Verifies: [ReqPersistSettingFilterProfilesv1] (Saving via Execute)
    [TestMethod]
    public void RemoveFilterNodeCommand_ChildSelected_ExecutesRemoveAction_UpdatesAndSaves()
    {
        // Arrange: Add a child to the root from TestInitialize
        var root = _viewModel.ActiveFilterProfile?.RootFilterViewModel; Assert.IsNotNull(root);
        _viewModel.SelectedFilterNode = root;
        _viewModel.AddFilterCommand.Execute("Substring"); // Uses Execute
        _testContext.Send(_ => { }, null); // Flush add action updates
        var child = root.Children[0];
        _viewModel.SelectedFilterNode = child; // Select the child
        _mockSettings.Reset();

        // Act: Remove the child (this uses Execute)
        _viewModel.RemoveFilterNodeCommand.Execute(null);
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert ViewModel State
        Assert.AreEqual(0, root.Children.Count, "Child should be removed from parent VM.");
        Assert.AreSame(root, _viewModel.SelectedFilterNode, "Parent node should be selected.");

        // Assert Observable Effects (Filter update triggered via Execute)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount should be 0.");

        // Assert Persistence (Save triggered via Execute)
        Assert.IsTrue(_mockSettings.SaveCalledCount > 0, "Settings were saved.");
        var savedRoot = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "Default")?.RootFilter as AndFilter;
        Assert.IsNotNull(savedRoot, "Saved root filter is null or not AndFilter.");
        Assert.AreEqual(0, savedRoot.SubFilters.Count, "Saved child count should be zero.");
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null), "Undo should be enabled."); // Verify undo possible (add + remove)
    }

    // Verifies: [ReqFilterNodeEditInlinev1] replaced by Command Pattern
    // Verifies: [ReqFilterDynamicUpdateViewv1] (Triggered by Execute)
    // Verifies: [ReqPersistSettingFilterProfilesv1] (Saving via Execute)
    [TestMethod]
    public void FilterViewModel_EndEdit_ExecutesChangeAction_UpdatesAndSaves()
    {
        // Arrange: Add root Substring node (using bypass), then select it
        var emptyProfileModel = new FilterProfile("EditProfile", null);
        var emptyProfileVM = new FilterProfileViewModel(emptyProfileModel, _viewModel);
        _viewModel.AvailableProfiles.Clear(); _viewModel.AvailableProfiles.Add(emptyProfileVM);
        _viewModel.ActiveFilterProfile = emptyProfileVM;
        _viewModel.AddFilterCommand.Execute("Substring"); // Sets root directly
        _testContext.Send(_ => { }, null);
        var node = _viewModel.ActiveFilterProfile?.RootFilterViewModel; Assert.IsNotNull(node);
        _viewModel.SelectedFilterNode = node;
        node.BeginEditCommand.Execute(null); // Start editing
        _mockSettings.Reset();
        string updatedValue = "Updated Value";

        // Act: Simulate text change and EndEdit via VM command
        node.FilterText = updatedValue; // Update text property
        node.EndEditCommand.Execute(null); // End editing (this triggers Execute in MainViewModel)
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert ViewModel State
        Assert.IsFalse(node.IsEditing, "Node should not be in editing state.");
        Assert.AreEqual(updatedValue, node.Filter.Value, "Model value mismatch after edit.");

        // Assert Observable Effects (Filter update triggered via Execute)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount should be 0.");

        // Assert Persistence (Save triggered via Execute)
        Assert.IsTrue(_mockSettings.SaveCalledCount > 0, "Settings were saved.");
        var savedRoot = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "EditProfile")?.RootFilter as SubstringFilter;
        Assert.IsNotNull(savedRoot, "Saved root filter is null or not SubstringFilter.");
        Assert.AreEqual(updatedValue, savedRoot.Value, "Saved filter value mismatch.");
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null), "Undo should be enabled."); // Verify undo possible
    }

    // Verifies: [ReqFilterNodeToggleEnablev1] replaced by Command Pattern
    // Verifies: [ReqFilterDynamicUpdateViewv1] (Triggered by Execute)
    // Verifies: [ReqPersistSettingFilterProfilesv1] (Saving via Execute) - CORRECTED
    [TestMethod]
    public void FilterViewModel_EnabledChanged_ExecutesToggleAction_UpdatesAndSaves() // CORRECTED
    {
        // Arrange: Add root Substring node (using bypass), then select it
        var toggleProfileModel = new FilterProfile("ToggleProfile", null);
        var toggleProfileVM = new FilterProfileViewModel(toggleProfileModel, _viewModel);
        _viewModel.AvailableProfiles.Clear(); _viewModel.AvailableProfiles.Add(toggleProfileVM);
        _viewModel.ActiveFilterProfile = toggleProfileVM;
        _viewModel.AddFilterCommand.Execute("Substring"); // Sets root directly
        _testContext.Send(_ => { }, null);
        var node = _viewModel.ActiveFilterProfile?.RootFilterViewModel; Assert.IsNotNull(node);
        _viewModel.SelectedFilterNode = node;
        Assert.IsTrue(node.Enabled, "Node should be enabled initially.");
        _mockSettings.Reset();

        // Act: Change enabled state via VM property (this triggers Execute in MainViewModel)
        node.Enabled = false;
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert Model State
        Assert.IsFalse(node.Filter.Enabled, "Model enabled state mismatch.");

        // Assert Observable Effects (Filter update triggered via Execute)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount should be 0.");

        // Assert Persistence (Save IS triggered via Execute) - CORRECTED
        Assert.IsTrue(_mockSettings.SaveCalledCount > 0, "Settings should be saved on Enabled change via Execute.");
        Assert.IsNotNull(_mockSettings.SavedSettings);
        var savedRoot = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "ToggleProfile")?.RootFilter as SubstringFilter;
        Assert.IsNotNull(savedRoot);
        Assert.IsFalse(savedRoot.Enabled, "Saved enabled state mismatch.");
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null), "Undo should be enabled."); // Verify undo possible
    }

    [TestMethod] public void Execute_SingleAction_ShouldEnableUndoAndDisableRedo()
    {
        // Arrange
        var rootVm = _viewModel.ActiveFilterProfile!.RootFilterViewModel!;
        Assert.IsFalse(_viewModel.UndoCommand.CanExecute(null), "Undo should initially be disabled.");
        Assert.IsFalse(_viewModel.RedoCommand.CanExecute(null), "Redo should initially be disabled.");

        // Act
        _viewModel.AddFilterCommand.Execute("Substring"); // Executes an AddFilterAction via _viewModel.Execute

        // Assert
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null), "Undo should be enabled after execute.");
        Assert.IsFalse(_viewModel.RedoCommand.CanExecute(null), "Redo should remain disabled after execute.");
    }

    [TestMethod] public void Undo_AfterSingleAction_ShouldDisableUndoEnableRedoAndRestoreState()
    {
        // Arrange
        var rootVm = _viewModel.ActiveFilterProfile!.RootFilterViewModel!;
        var initialChildCount = rootVm.Children.Count;
        _viewModel.AddFilterCommand.Execute("Substring"); // Execute the action first
        var childCountAfterAdd = rootVm.Children.Count;
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null));
        Assert.IsFalse(_viewModel.RedoCommand.CanExecute(null));

        // Act
        _viewModel.UndoCommand.Execute(null);

        // Assert
        Assert.IsFalse(_viewModel.UndoCommand.CanExecute(null), "Undo should be disabled after undo.");
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null), "Redo should be enabled after undo.");
        Assert.AreEqual(initialChildCount, rootVm.Children.Count, "Child count should be restored after undo.");
        Assert.IsInstanceOfType(rootVm.Filter, typeof(AndFilter));
        Assert.AreEqual(initialChildCount, ((AndFilter)rootVm.Filter).SubFilters.Count, "Model child count should be restored after undo.");
    }

    [TestMethod] public void Redo_AfterUndo_ShouldEnableUndoDisableRedoAndRestoreState()
    {
        // Arrange
        var rootVm = _viewModel.ActiveFilterProfile!.RootFilterViewModel!;
        _viewModel.AddFilterCommand.Execute("Substring"); // Add
        var addedFilterModel = ((AndFilter)rootVm.Filter).SubFilters.Last(); // Get the added model
        var childCountAfterAdd = rootVm.Children.Count;
        _viewModel.UndoCommand.Execute(null); // Undo
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null));
        Assert.IsFalse(_viewModel.UndoCommand.CanExecute(null));

        // Act
        _viewModel.RedoCommand.Execute(null);

        // Assert
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null), "Undo should be enabled after redo.");
        Assert.IsFalse(_viewModel.RedoCommand.CanExecute(null), "Redo should be disabled after redo.");
        Assert.AreEqual(childCountAfterAdd, rootVm.Children.Count, "Child count should be restored after redo.");
        Assert.IsTrue(rootVm.Children.Any(vm => vm.Filter == addedFilterModel), "Redone child VM should exist.");
        Assert.IsTrue(((AndFilter)rootVm.Filter).SubFilters.Contains(addedFilterModel), "Redone child model should exist.");
    }

    [TestMethod] public void Execute_AfterUndo_ShouldClearRedoStack()
    {
        // Arrange
        var rootVm = _viewModel.ActiveFilterProfile!.RootFilterViewModel!;
        _viewModel.AddFilterCommand.Execute("Substring"); // Action 1
        _viewModel.AddFilterCommand.Execute("Regex");     // Action 2
        _viewModel.UndoCommand.Execute(null); // Undo Action 2
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null), "Redo should be enabled before new action.");

        // Act
        _viewModel.AddFilterCommand.Execute("And"); // Action 3 (New action after Undo)

        // Assert
        Assert.IsFalse(_viewModel.RedoCommand.CanExecute(null), "Redo stack should be cleared after new action.");
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null)); // Undo should still be possible for Action 1 & 3
        Assert.AreEqual(2, rootVm.Children.Count, "Should have 2 children: original substring and new AND.");
        Assert.IsInstanceOfType(rootVm.Children[0].Filter, typeof(SubstringFilter));
        Assert.IsInstanceOfType(rootVm.Children[1].Filter, typeof(AndFilter));
    }

    [TestMethod] public void Undo_ChangeFilterValue_ShouldRestorePreviousValue()
    {
        // Arrange
        var rootVm = _viewModel.ActiveFilterProfile!.RootFilterViewModel!;
        _viewModel.AddFilterCommand.Execute("Substring");
        var subVm = rootVm.Children.First();
        string oldValue = "Initial Value";
        string newValue = "Changed Value";
        subVm.Filter.Value = oldValue; // Set initial value directly for test setup
        subVm.RefreshProperties();     // Refresh VM state

        // Act: Simulate edit and commit
        subVm.BeginEditCommand.Execute(null);
        subVm.FilterText = newValue; // Simulate typing
        subVm.EndEditCommand.Execute(null); // This executes the ChangeFilterValueAction

        Assert.AreEqual(newValue, subVm.FilterText, "Value should be new value after edit.");

        // Act: Undo
        _viewModel.UndoCommand.Execute(null);

        // Assert
        Assert.AreEqual(oldValue, subVm.FilterText, "Value should be restored after undo.");
        Assert.AreEqual(oldValue, subVm.Filter.Value, "Model value should be restored after undo.");
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null));
    }

    [TestMethod] public void Undo_ToggleEnabled_ShouldRestorePreviousState()
    {
        // Arrange
        var rootVm = _viewModel.ActiveFilterProfile!.RootFilterViewModel!;
        _viewModel.AddFilterCommand.Execute("Substring");
        var subVm = rootVm.Children.First();
        bool originalState = subVm.Enabled; // Typically true initially
        bool newState = !originalState;

        // Act: Toggle enabled state (this executes ToggleFilterEnabledAction)
        subVm.Enabled = newState;
        Assert.AreEqual(newState, subVm.Enabled, "Enabled state should be changed after toggle.");

        // Act: Undo
        _viewModel.UndoCommand.Execute(null);

        // Assert
        Assert.AreEqual(originalState, subVm.Enabled, "Enabled state should be restored after undo.");
        Assert.AreEqual(originalState, subVm.Filter.Enabled, "Model enabled state should be restored after undo.");
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null));
    }

    // Example testing multiple undo/redo steps
    [TestMethod] public void UndoRedo_MultipleActions_ShouldMaintainCorrectState()
    {
        // Arrange
        var rootVm = _viewModel.ActiveFilterProfile!.RootFilterViewModel!; // Should be AND filter

        // Act: Add 3 nodes
        _viewModel.AddFilterCommand.Execute("Substring"); // Node 0 (Sub)
        _viewModel.AddFilterCommand.Execute("Regex");     // Node 1 (Regex)
        _viewModel.AddFilterCommand.Execute("And");       // Node 2 (And)

        Assert.AreEqual(3, rootVm.Children.Count);
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null));
        Assert.IsFalse(_viewModel.RedoCommand.CanExecute(null));

        // Act: Undo Node 2 (And)
        _viewModel.UndoCommand.Execute(null);
        Assert.AreEqual(2, rootVm.Children.Count);
        Assert.IsInstanceOfType(rootVm.Children.Last().Filter, typeof(RegexFilter));
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null));

        // Act: Undo Node 1 (Regex)
        _viewModel.UndoCommand.Execute(null);
        Assert.AreEqual(1, rootVm.Children.Count);
        Assert.IsInstanceOfType(rootVm.Children.Last().Filter, typeof(SubstringFilter));
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null));

        // Act: Redo Node 1 (Regex)
        _viewModel.RedoCommand.Execute(null);
         Assert.AreEqual(2, rootVm.Children.Count);
        Assert.IsInstanceOfType(rootVm.Children.Last().Filter, typeof(RegexFilter));
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null)); // Still Node 2 (And) on redo stack

        // Act: Redo Node 2 (And)
        _viewModel.RedoCommand.Execute(null);
        Assert.AreEqual(3, rootVm.Children.Count);
        Assert.IsInstanceOfType(rootVm.Children.Last().Filter, typeof(AndFilter));
        Assert.IsFalse(_viewModel.RedoCommand.CanExecute(null)); // Redo stack empty
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null));
    }
}
