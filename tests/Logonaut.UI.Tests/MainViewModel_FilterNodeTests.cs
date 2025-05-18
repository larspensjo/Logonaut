using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Logonaut.Filters;
using Logonaut.UI.ViewModels; 
using Logonaut.Core; 
using Logonaut.Common; 

namespace Logonaut.UI.Tests.ViewModels;

[TestClass] public class MainViewModel_FilterNodeTests : MainViewModelTestBase
{

    [TestInitialize] public override void TestInitialize()
    {
        base.TestInitialize();
        base.SetupMainAndTabViewModel();

        // Ensure the active profile is "Default" and has an AndFilter root for these tests.
        var defaultProfileVM = _viewModel.AvailableProfiles.FirstOrDefault(p => p.Name == "Default");
        Assert.IsNotNull(defaultProfileVM, "Default profile VM not found after base initialization.");

        if (defaultProfileVM.Model.RootFilter is not AndFilter)
        {
            defaultProfileVM.SetModelRootFilter(new AndFilter());        }
        if (_viewModel.ActiveFilterProfile != defaultProfileVM)
        {
            _viewModel.ActiveFilterProfile = defaultProfileVM;
        }
        _mockSettings?.Reset(); // Reset after ensuring profile state
    }

    [TestMethod] public void AddFilterCommand_EmptyProfile_SetsRoot_Selects_UpdatesAndSaves()
    {
        // Arrange: Ensure profile is empty first
        var emptyProfileModel = new FilterProfile("Empty", null);
        var emptyProfileVM = new FilterProfileViewModel(emptyProfileModel, _viewModel);
        _viewModel.AvailableProfiles.Clear();
        _viewModel.AvailableProfiles.Add(emptyProfileVM);
        _viewModel.ActiveFilterProfile = emptyProfileVM;

        Assert.IsNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Pre-condition: Root VM should be null.");
        _mockSettings.Reset();

        // Act
        _viewModel.AddFilterCommand.Execute("SubstringType");

        // Assert ViewModel State
        Assert.IsNotNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Root VM should be created.");
        var rootVM = _viewModel.ActiveFilterProfile.RootFilterViewModel;
        Assert.IsInstanceOfType(rootVM.Filter, typeof(SubstringFilter), "Root filter type mismatch.");
        Assert.AreEqual(1, _viewModel.ActiveTreeRootNodes.Count, "ActiveTreeRootNodes count mismatch.");
        Assert.AreSame(rootVM, _viewModel.ActiveTreeRootNodes[0], "ActiveTreeRootNodes content mismatch.");
        Assert.AreSame(rootVM, _viewModel.SelectedFilterNode, "New root should be selected.");

        // Assert Observable Effects (FilteredLogLines is on TabViewModel, accessed via MainViewModel delegate)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _tabViewModel.FilteredLogLinesCount, "TabViewModel.FilteredLogLinesCount should be 0.");


        // Assert Persistence 
        Assert.IsTrue(_mockSettings.SaveCalledCount > 0, "Settings were not saved.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Saved settings is null.");
        Assert.AreEqual("Empty", _mockSettings.SavedSettings?.LastActiveProfileName);
        Assert.IsNotNull(_mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "Empty")?.RootFilter, "Saved root filter is null.");
        Assert.IsInstanceOfType(_mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "Empty")?.RootFilter, typeof(SubstringFilter), "Saved filter type mismatch.");
        Assert.IsFalse(_viewModel.UndoCommand.CanExecute(null), "Undo should NOT be enabled for direct root set.");
    }

    [TestMethod] public void AddFilterCommand_CompositeSelected_ExecutesAddAction_UpdatesAndSaves()
    {
        // Arrange
        var root = _viewModel.ActiveFilterProfile?.RootFilterViewModel;
        Assert.IsNotNull(root, "Setup failed: Root And node not found.");
        Assert.IsInstanceOfType(root.Filter, typeof(AndFilter), "Root filter should be AndFilter for this test.");
        _viewModel.SelectedFilterNode = root;
        _mockSettings.Reset();

        // Act
        _viewModel.AddFilterCommand.Execute("RegexType");

        // Assert ViewModel State
        Assert.AreEqual(1, root.Children.Count, "Child count mismatch.");
        var child = root.Children[0];
        Assert.IsInstanceOfType(child.Filter, typeof(RegexFilter), "Child filter type mismatch.");
        Assert.AreSame(child, _viewModel.SelectedFilterNode, "New child should be selected.");

        // Assert Observable Effects
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _tabViewModel.FilteredLogLinesCount, "TabViewModel.FilteredLogLinesCount should be 0.");


        // Assert Persistence 
        Assert.IsTrue(_mockSettings.SaveCalledCount > 0, "Settings were not saved.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Saved settings object is null.");

        var savedProfile = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "Default");
        Assert.IsNotNull(savedProfile, "Saved profile 'Default' not found.");
        Assert.IsNotNull(savedProfile.RootFilter, "Saved profile 'Default' has a null RootFilter.");
        Assert.IsInstanceOfType(savedProfile.RootFilter, typeof(AndFilter), $"Saved profile 'Default' RootFilter is type {savedProfile.RootFilter?.GetType().Name}, expected AndFilter.");

        var savedRoot = savedProfile?.RootFilter as AndFilter;
        Assert.IsNotNull(savedRoot, "Saved root filter is null or not AndFilter.");
        Assert.AreEqual(1, savedRoot.SubFilters.Count, "Saved child count mismatch.");
        Assert.IsInstanceOfType(savedRoot.SubFilters[0], typeof(RegexFilter), "Saved child filter type mismatch.");
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null), "Undo should be enabled.");
    }

    [TestMethod] public void RemoveFilterNodeCommand_RootSelected_ClearsTree_UpdatesAndSaves()
    {
        // Arrange
        var root = _viewModel.ActiveFilterProfile?.RootFilterViewModel;
        Assert.IsNotNull(root, "Setup failed: Root node not found.");
        _viewModel.SelectedFilterNode = root;
        _mockSettings.Reset();

        // Act
        _viewModel.RemoveFilterNodeCommand.Execute(null);

        // Assert ViewModel State
        Assert.IsNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Root VM should be null after removal.");
        Assert.AreEqual(0, _viewModel.ActiveTreeRootNodes.Count, "ActiveTreeRootNodes should be empty.");
        Assert.IsNull(_viewModel.SelectedFilterNode, "Selected node should be null.");

        // Assert Observable Effects
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _tabViewModel.FilteredLogLinesCount, "TabViewModel.FilteredLogLinesCount should be 0.");

        // Assert Persistence
        Assert.IsTrue(_mockSettings.SaveCalledCount > 0, "Settings were not saved.");
        Assert.IsNotNull(_mockSettings.SavedSettings);
        Assert.IsNull(_mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "Default")?.RootFilter, "Saved root filter should be null.");
        Assert.IsFalse(_viewModel.UndoCommand.CanExecute(null), "Undo should NOT be enabled for direct root removal.");
    }

    [TestMethod] public void RemoveFilterNodeCommand_ChildSelected_ExecutesRemoveAction_UpdatesAndSaves()
    {
        // Arrange
        var root = _viewModel.ActiveFilterProfile?.RootFilterViewModel; Assert.IsNotNull(root);
        _viewModel.SelectedFilterNode = root;
        _viewModel.AddFilterCommand.Execute("SubstringType");
        var child = root.Children[0];
        _viewModel.SelectedFilterNode = child;
        _mockSettings.Reset();

        // Act
        _viewModel.RemoveFilterNodeCommand.Execute(null);

        // Assert ViewModel State
        Assert.AreEqual(0, root.Children.Count, "Child should be removed from parent VM.");
        Assert.AreSame(root, _viewModel.SelectedFilterNode, "Parent node should be selected.");

        // Assert Observable Effects
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _tabViewModel.FilteredLogLinesCount, "TabViewModel.FilteredLogLinesCount should be 0.");

        // Assert Persistence
        Assert.IsTrue(_mockSettings.SaveCalledCount > 0, "Settings were saved.");
        var savedRoot = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "Default")?.RootFilter as AndFilter;
        Assert.IsNotNull(savedRoot, "Saved root filter is null or not AndFilter.");
        Assert.AreEqual(0, savedRoot.SubFilters.Count, "Saved child count should be zero.");
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null), "Undo should be enabled.");
    }

    [TestMethod] public void FilterViewModel_EndEdit_ExecutesChangeAction_UpdatesAndSaves()
    {
        // Arrange
        var emptyProfileModel = new FilterProfile("EditProfile", null);
        var emptyProfileVM = new FilterProfileViewModel(emptyProfileModel, _viewModel);
        _viewModel.AvailableProfiles.Clear(); _viewModel.AvailableProfiles.Add(emptyProfileVM);
        _viewModel.ActiveFilterProfile = emptyProfileVM;
        _viewModel.AddFilterCommand.Execute("SubstringType");
        var node = _viewModel.ActiveFilterProfile?.RootFilterViewModel; Assert.IsNotNull(node);
        _viewModel.SelectedFilterNode = node;
        node.BeginEditCommand.Execute(null);
        _mockSettings.Reset();
        string updatedValue = "Updated Value";

        // Act
        node.FilterText = updatedValue;
        node.EndEditCommand.Execute(null);

        // Assert ViewModel State
        Assert.IsFalse(node.IsEditing, "Node should not be in editing state.");
        Assert.AreEqual(updatedValue, node.Filter.Value, "Model value mismatch after edit.");

        // Assert Observable Effects
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _tabViewModel.FilteredLogLinesCount, "TabViewModel.FilteredLogLinesCount should be 0.");

        // Assert Persistence 
        Assert.IsTrue(_mockSettings.SaveCalledCount > 0, "Settings were saved.");
        var savedRoot = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "EditProfile")?.RootFilter as SubstringFilter;
        Assert.IsNotNull(savedRoot, "Saved root filter is null or not SubstringFilter.");
        Assert.AreEqual(updatedValue, savedRoot.Value, "Saved filter value mismatch.");
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null), "Undo should be enabled.");
    }

    [TestMethod] public void FilterViewModel_EnabledChanged_ExecutesToggleAction_UpdatesAndSaves()
    {
        // Arrange
        var toggleProfileModel = new FilterProfile("ToggleProfile", null);
        var toggleProfileVM = new FilterProfileViewModel(toggleProfileModel, _viewModel);
        _viewModel.AvailableProfiles.Clear(); _viewModel.AvailableProfiles.Add(toggleProfileVM);
        _viewModel.ActiveFilterProfile = toggleProfileVM;
        _viewModel.AddFilterCommand.Execute("SubstringType");
        var node = _viewModel.ActiveFilterProfile?.RootFilterViewModel; Assert.IsNotNull(node);
        _viewModel.SelectedFilterNode = node;
        Assert.IsTrue(node.Enabled, "Node should be enabled initially.");
        _mockSettings.Reset();

        // Act
        node.Enabled = false;

        // Assert Model State
        Assert.IsFalse(node.Filter.Enabled, "Model enabled state mismatch.");

        // Assert Observable Effects
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _tabViewModel.FilteredLogLinesCount, "TabViewModel.FilteredLogLinesCount should be 0.");

        // Assert Persistence 
        Assert.IsTrue(_mockSettings.SaveCalledCount > 0, "Settings should be saved on Enabled change via Execute.");
        Assert.IsNotNull(_mockSettings.SavedSettings);
        var savedRoot = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault(p => p.Name == "ToggleProfile")?.RootFilter as SubstringFilter;
        Assert.IsNotNull(savedRoot);
        Assert.IsFalse(savedRoot.Enabled, "Saved enabled state mismatch.");
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null), "Undo should be enabled.");
    }

    [TestMethod] public void Execute_SingleAction_ShouldEnableUndoAndDisableRedo()
    {
        // Arrange
        var rootVm = _viewModel.ActiveFilterProfile!.RootFilterViewModel!;
        Assert.IsFalse(_viewModel.UndoCommand.CanExecute(null), "Undo should initially be disabled.");
        Assert.IsFalse(_viewModel.RedoCommand.CanExecute(null), "Redo should initially be disabled.");

        // Act
        _viewModel.AddFilterCommand.Execute("SubstringType");

        // Assert
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null), "Undo should be enabled after execute.");
        Assert.IsFalse(_viewModel.RedoCommand.CanExecute(null), "Redo should remain disabled after execute.");
    }

    [TestMethod] public void Undo_AfterSingleAction_ShouldDisableUndoEnableRedoAndRestoreState()
    {
        // Arrange
        var rootVm = _viewModel.ActiveFilterProfile!.RootFilterViewModel!;
        var initialChildCount = rootVm.Children.Count;
        _viewModel.AddFilterCommand.Execute("SubstringType");
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
        _viewModel.AddFilterCommand.Execute("SubstringType");
        var addedFilterModel = ((AndFilter)rootVm.Filter).SubFilters.Last();
        var childCountAfterAdd = rootVm.Children.Count;
        _viewModel.UndoCommand.Execute(null);
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
        _viewModel.AddFilterCommand.Execute("SubstringType");
        _viewModel.AddFilterCommand.Execute("RegexType");
        _viewModel.UndoCommand.Execute(null);
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null), "Redo should be enabled before new action.");

        // Act
        _viewModel.AddFilterCommand.Execute("AndType");

        // Assert
        Assert.IsFalse(_viewModel.RedoCommand.CanExecute(null), "Redo stack should be cleared after new action.");
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null));
        Assert.AreEqual(2, rootVm.Children.Count, "Should have 2 children: original substring and new AND.");
        Assert.IsInstanceOfType(rootVm.Children[0].Filter, typeof(SubstringFilter));
        Assert.IsInstanceOfType(rootVm.Children[1].Filter, typeof(AndFilter));
    }

    [TestMethod] public void Undo_ChangeFilterValue_ShouldRestorePreviousValue()
    {
        // Arrange
        var rootVm = _viewModel.ActiveFilterProfile!.RootFilterViewModel!;
        _viewModel.AddFilterCommand.Execute("SubstringType");
        var subVm = rootVm.Children.First();
        string oldValue = "Initial Value";
        string newValue = "Changed Value";
        subVm.Filter.Value = oldValue;
        subVm.RefreshProperties();

        // Act
        subVm.BeginEditCommand.Execute(null);
        subVm.FilterText = newValue;
        subVm.EndEditCommand.Execute(null);

        Assert.AreEqual(newValue, subVm.FilterText, "Value should be new value after edit.");

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
        _viewModel.AddFilterCommand.Execute("SubstringType");
        var subVm = rootVm.Children.First();
        bool originalState = subVm.Enabled;
        bool newState = !originalState;

        // Act
        subVm.Enabled = newState;
        Assert.AreEqual(newState, subVm.Enabled, "Enabled state should be changed after toggle.");

        _viewModel.UndoCommand.Execute(null);

        // Assert
        Assert.AreEqual(originalState, subVm.Enabled, "Enabled state should be restored after undo.");
        Assert.AreEqual(originalState, subVm.Filter.Enabled, "Model enabled state should be restored after undo.");
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null));
    }

    [TestMethod] public void UndoRedo_MultipleActions_ShouldMaintainCorrectState()
    {
        // Arrange
        var rootVm = _viewModel.ActiveFilterProfile!.RootFilterViewModel!;

        // Act
        _viewModel.AddFilterCommand.Execute("SubstringType");
        _viewModel.AddFilterCommand.Execute("RegexType");
        _viewModel.AddFilterCommand.Execute("AndType");

        Assert.AreEqual(3, rootVm.Children.Count);
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null));
        Assert.IsFalse(_viewModel.RedoCommand.CanExecute(null));

        _viewModel.UndoCommand.Execute(null);
        Assert.AreEqual(2, rootVm.Children.Count);
        Assert.IsInstanceOfType(rootVm.Children.Last().Filter, typeof(RegexFilter));
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null));

        _viewModel.UndoCommand.Execute(null);
        Assert.AreEqual(1, rootVm.Children.Count);
        Assert.IsInstanceOfType(rootVm.Children.Last().Filter, typeof(SubstringFilter));
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null));

        _viewModel.RedoCommand.Execute(null);
        Assert.AreEqual(2, rootVm.Children.Count);
        Assert.IsInstanceOfType(rootVm.Children.Last().Filter, typeof(RegexFilter));
        Assert.IsTrue(_viewModel.RedoCommand.CanExecute(null));

        _viewModel.RedoCommand.Execute(null);
        Assert.AreEqual(3, rootVm.Children.Count);
        Assert.IsInstanceOfType(rootVm.Children.Last().Filter, typeof(AndFilter));
        Assert.IsFalse(_viewModel.RedoCommand.CanExecute(null));
        Assert.IsTrue(_viewModel.UndoCommand.CanExecute(null));
    }
}
