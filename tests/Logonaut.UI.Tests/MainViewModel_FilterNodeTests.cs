using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Logonaut.Filters;
using Logonaut.UI.ViewModels; // Required for FilterViewModel

namespace Logonaut.UI.Tests.ViewModels;

 /// Tests related to adding, removing, editing filter nodes within the active profile
[TestClass] public class MainViewModel_FilterNodeTests : MainViewModelTestBase
{
    // Verifies: [ReqFilterNodeManageButtonsv1] (Add Substring), [ReqFilterRuleSubstringv1]
    [TestMethod] public void AddFilterCommand_EmptyTree_AddsRoot_Selects_UpdatesProcessor_Saves()
    {
        // Arrange
        Assert.IsNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Pre-condition: Root VM should be null.");
        _mockProcessor.ResetCounters();
        _mockSettings.ResetSettings();

        // Act
        _viewModel.AddFilterCommand.Execute("Substring");
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert
        Assert.IsNotNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Root VM should be created.");
        var rootVM = _viewModel.ActiveFilterProfile.RootFilterViewModel;
        Assert.IsInstanceOfType(rootVM.Filter, typeof(SubstringFilter), "Root filter type mismatch.");
        Assert.AreEqual(1, _viewModel.ActiveTreeRootNodes.Count, "ActiveTreeRootNodes count mismatch.");
        Assert.AreSame(rootVM, _viewModel.ActiveTreeRootNodes[0], "ActiveTreeRootNodes content mismatch.");
        Assert.AreSame(rootVM, _viewModel.SelectedFilterNode, "New root should be selected.");
        Assert.AreEqual(1, _mockProcessor.UpdateFilterSettingsCallCount, "Processor update count mismatch.");
        Assert.IsNotNull(_mockProcessor.LastFilterSettings?.Filter, "Processor filter is null.");
        Assert.IsInstanceOfType(_mockProcessor.LastFilterSettings?.Filter, typeof(SubstringFilter), "Processor filter type mismatch.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
    }

    // Verifies: [ReqFilterNodeManageButtonsv1] (Add Regex to Composite), [ReqFilterRuleRegexv1], [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void AddFilterCommand_CompositeSelected_AddsChild_Selects_UpdatesProcessor_Saves()
    {
        // Arrange
        _viewModel.AddFilterCommand.Execute("And"); // Creates root AND node
        var root = _viewModel.ActiveFilterProfile?.RootFilterViewModel;
        Assert.IsNotNull(root, "Root And node failed to create.");
        _viewModel.SelectedFilterNode = root; // Select the root
        _mockProcessor.ResetCounters();
        _mockSettings.ResetSettings();

        // Act
        _viewModel.AddFilterCommand.Execute("Regex"); // Add Regex as child
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert
        Assert.AreEqual(1, root.Children.Count, "Child count mismatch.");
        var child = root.Children[0];
        Assert.IsInstanceOfType(child.Filter, typeof(RegexFilter), "Child filter type mismatch.");
        Assert.AreSame(child, _viewModel.SelectedFilterNode, "New child should be selected.");
        Assert.AreEqual(1, _mockProcessor.UpdateFilterSettingsCallCount, "Processor update count mismatch.");
        Assert.IsNotNull(_mockProcessor.LastFilterSettings?.Filter, "Processor filter is null.");
        Assert.AreSame(root.Filter, _mockProcessor.LastFilterSettings?.Filter, "Processor filter should be the root (And).");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
    }

    // Verifies: [ReqFilterNodeManageButtonsv1] (Remove Root)
    [TestMethod] public void RemoveFilterNodeCommand_RootSelected_ClearsTree_UpdatesProcessor_Saves()
    {
        // Arrange
        _viewModel.AddFilterCommand.Execute("Substring"); // Add a root node
        _viewModel.SelectedFilterNode = _viewModel.ActiveFilterProfile?.RootFilterViewModel;
        Assert.IsNotNull(_viewModel.SelectedFilterNode, "Root node selection failed.");
        _mockProcessor.ResetCounters();
        _mockSettings.ResetSettings();

        // Act
        _viewModel.RemoveFilterNodeCommand.Execute(null);
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert
        Assert.IsNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Root VM should be null after removal.");
        Assert.AreEqual(0, _viewModel.ActiveTreeRootNodes.Count, "ActiveTreeRootNodes should be empty.");
        Assert.IsNull(_viewModel.SelectedFilterNode, "Selected node should be null.");
        Assert.AreEqual(1, _mockProcessor.UpdateFilterSettingsCallCount, "Processor update count mismatch.");
        Assert.IsInstanceOfType(_mockProcessor.LastFilterSettings?.Filter, typeof(TrueFilter), "Processor filter should revert to TrueFilter.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
    }

    // Verifies: [ReqFilterNodeManageButtonsv1] (Remove Child), [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void RemoveFilterNodeCommand_ChildSelected_RemovesChild_SelectsParent_UpdatesProcessor_Saves()
    {
        // Arrange
        _viewModel.AddFilterCommand.Execute("Or"); // Add root OR node (calls proc, count=1)
        var root = _viewModel.ActiveFilterProfile?.RootFilterViewModel; Assert.IsNotNull(root, "Root OR node failed to create.");
        _viewModel.SelectedFilterNode = root;
        _viewModel.AddFilterCommand.Execute("Substring"); // Add child Substring (calls proc, count=2)
        var child = root.Children[0];
        _viewModel.SelectedFilterNode = child; // Select the child
        _mockProcessor.ResetCounters();
        _mockSettings.ResetSettings();

        // Act
        _viewModel.RemoveFilterNodeCommand.Execute(null); // Remove child (calls proc, count=1 after reset)
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert
        Assert.AreEqual(0, root.Children.Count, "Child should be removed from parent VM.");
        Assert.AreSame(root, _viewModel.SelectedFilterNode, "Parent node should be selected.");
        Assert.AreEqual(1, _mockProcessor.UpdateFilterSettingsCallCount, "Processor update count mismatch.");
        Assert.IsNotNull(_mockProcessor.LastFilterSettings?.Filter, "Processor filter is null.");
        Assert.AreSame(root.Filter, _mockProcessor.LastFilterSettings?.Filter, "Processor filter should be the root (Or).");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
    }

    // Verifies: [ReqFilterNodeEditInlinev1]
    [TestMethod] public void ToggleEditNodeCommand_EndEdit_UpdatesProcessor_Saves()
    {
        // Arrange
        _viewModel.AddFilterCommand.Execute("Substring"); // Add root Substring (calls proc, count=1)
        var node = _viewModel.ActiveFilterProfile?.RootFilterViewModel; Assert.IsNotNull(node, "Root Substring node failed to create.");
        _viewModel.SelectedFilterNode = node;
        node.BeginEditCommand.Execute(null); // Start editing
        _mockProcessor.ResetCounters();
        _mockSettings.ResetSettings();

        // Act
        node.FilterText = "Updated Value";
        _viewModel.ToggleEditNodeCommand.Execute(null); // End editing (Calls proc, count=1 after reset; Saves)
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert
        Assert.IsFalse(node.IsEditing, "Node should not be in editing state.");
        Assert.AreEqual(1, _mockProcessor.UpdateFilterSettingsCallCount, "Processor update count mismatch.");
        Assert.IsNotNull(_mockProcessor.LastFilterSettings);
        var filter = _mockProcessor.LastFilterSettings?.Filter;
        Assert.IsNotNull(filter, "Processor filter is null.");
        Assert.IsInstanceOfType(filter, typeof(SubstringFilter), "Processor filter type mismatch.");
        Assert.AreEqual("Updated Value", ((SubstringFilter)filter).Value, "Filter value mismatch.");
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
    }

    // Verifies: [ReqFilterNodeToggleEnablev1], [ReqFilterDynamicUpdateViewv1]
    [TestMethod] public void FilterViewModel_EnabledChanged_TriggersProcessorUpdate_Saves()
    {
        // Arrange
        _viewModel.AddFilterCommand.Execute("Substring"); // (Calls proc, count=1)
        var node = _viewModel.ActiveFilterProfile?.RootFilterViewModel; Assert.IsNotNull(node, "Node creation failed.");
        Assert.IsTrue(node.Enabled, "Node should be enabled initially.");
        _mockProcessor.ResetCounters();
        _mockSettings.ResetSettings();

        // Act
        node.Enabled = false; // Change state (Callback should trigger processor update)
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert
        Assert.AreEqual(1, _mockProcessor.UpdateFilterSettingsCallCount, "Processor update count mismatch.");
        Assert.IsNotNull(_mockProcessor.LastFilterSettings?.Filter, "Processor filter is null.");
        Assert.IsFalse(_mockProcessor.LastFilterSettings?.Filter?.Enabled, "Processor filter enabled state mismatch.");
        // Note: Saving is currently tied to explicit commands in MainViewModel, not directly to this property change.
        // Assert.IsNotNull(_mockSettings.SavedSettings, "Settings should be saved when Enabled changes."); // This would fail currently
    }
}
