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
[TestClass]
public class MainViewModel_FilterNodeTests : MainViewModelTestBase // Inherit from the updated base
{
    // Note: _viewModel is created in the base TestInitialize

    // Verifies: [ReqFilterNodeManageButtonsv1] (Add Substring), [ReqFilterRuleSubstringv1]
    // Verifies: [ReqFilterDynamicUpdateViewv1] (Triggered by add)
    // Verifies: [ReqPersistSettingFilterProfilesv1] (Saving after add)
    [TestMethod] public void AddFilterCommand_EmptyTree_AddsRoot_Selects_UpdatesViewModelState_Saves() // Renamed slightly
    {
        // Arrange
        Assert.IsNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Pre-condition: Root VM should be null.");
        _mockSettings.ResetSettings();

        // Act
        _viewModel.AddFilterCommand.Execute("Substring");
        _testContext.Send(_ => { }, null); // Flush context queue for update and save

        // Assert ViewModel State
        Assert.IsNotNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Root VM should be created.");
        var rootVM = _viewModel.ActiveFilterProfile.RootFilterViewModel;
        Assert.IsInstanceOfType(rootVM.Filter, typeof(SubstringFilter), "Root filter type mismatch.");
        Assert.AreEqual(1, _viewModel.ActiveTreeRootNodes.Count, "ActiveTreeRootNodes count mismatch.");
        Assert.AreSame(rootVM, _viewModel.ActiveTreeRootNodes[0], "ActiveTreeRootNodes content mismatch.");
        Assert.AreSame(rootVM, _viewModel.SelectedFilterNode, "New root should be selected.");

        // Assert Observable Effects (Check the resulting state of FilteredLogLines)
        // Since LogDoc is empty, the internal filter update should result in an empty list
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount should be 0.");

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
        Assert.IsNotNull(_mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault()?.RootFilter, "Saved root filter is null.");
        Assert.IsInstanceOfType(_mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault()?.RootFilter, typeof(SubstringFilter), "Saved filter type mismatch.");
    }

    // Verifies: [ReqFilterNodeManageButtonsv1] (Add Regex to Composite), [ReqFilterRuleRegexv1], [ReqFilterRuleTreeStructurev1]
    // Verifies: [ReqFilterDynamicUpdateViewv1] (Triggered by add)
    // Verifies: [ReqPersistSettingFilterProfilesv1] (Saving after add)
    [TestMethod] public void AddFilterCommand_CompositeSelected_AddsChild_Selects_UpdatesViewModelState_Saves() // Renamed slightly
    {
        // Arrange: Setup - Add Root AND node
        _viewModel.AddFilterCommand.Execute("And");
        _testContext.Send(_ => { }, null); // Flush
        var root = _viewModel.ActiveFilterProfile?.RootFilterViewModel;
        Assert.IsNotNull(root, "Root And node failed to create.");
        _viewModel.SelectedFilterNode = root; // Select the root
        _mockSettings.ResetSettings();

        // Act: Add Regex as child
        _viewModel.AddFilterCommand.Execute("Regex");
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert ViewModel State
        Assert.AreEqual(1, root.Children.Count, "Child count mismatch.");
        var child = root.Children[0];
        Assert.IsInstanceOfType(child.Filter, typeof(RegexFilter), "Child filter type mismatch.");
        Assert.AreSame(child, _viewModel.SelectedFilterNode, "New child should be selected.");

        // Assert Observable Effects (Check the resulting state of FilteredLogLines)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount should be 0.");

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
        var savedRoot = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault()?.RootFilter as AndFilter;
        Assert.IsNotNull(savedRoot, "Saved root filter is null or not AndFilter.");
        Assert.AreEqual(1, savedRoot.SubFilters.Count, "Saved child count mismatch.");
        Assert.IsInstanceOfType(savedRoot.SubFilters[0], typeof(RegexFilter), "Saved child filter type mismatch.");
    }

    // Verifies: [ReqFilterNodeManageButtonsv1] (Remove Root)
    // Verifies: [ReqFilterDynamicUpdateViewv1] (Triggered by remove)
    // Verifies: [ReqPersistSettingFilterProfilesv1] (Saving after remove)
    [TestMethod] public void RemoveFilterNodeCommand_RootSelected_ClearsTree_UpdatesViewModelState_Saves() // Renamed slightly
    {
        // Arrange: Setup - Add a root node
        _viewModel.AddFilterCommand.Execute("Substring");
        _testContext.Send(_ => { }, null); // Flush
        _viewModel.SelectedFilterNode = _viewModel.ActiveFilterProfile?.RootFilterViewModel;
        Assert.IsNotNull(_viewModel.SelectedFilterNode, "Root node selection failed.");
        _mockSettings.ResetSettings();

        // Act: Remove the root
        _viewModel.RemoveFilterNodeCommand.Execute(null);
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert ViewModel State
        Assert.IsNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Root VM should be null after removal.");
        Assert.AreEqual(0, _viewModel.ActiveTreeRootNodes.Count, "ActiveTreeRootNodes should be empty.");
        Assert.IsNull(_viewModel.SelectedFilterNode, "Selected node should be null.");

        // Assert Observable Effects (Check the resulting state of FilteredLogLines)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount should be 0.");

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
        Assert.IsNull(_mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault()?.RootFilter, "Saved root filter should be null.");
    }

    // Verifies: [ReqFilterNodeManageButtonsv1] (Remove Child), [ReqFilterRuleTreeStructurev1]
    // Verifies: [ReqFilterDynamicUpdateViewv1] (Triggered by remove)
    // Verifies: [ReqPersistSettingFilterProfilesv1] (Saving after remove)
    [TestMethod] public void RemoveFilterNodeCommand_ChildSelected_RemovesChild_SelectsParent_UpdatesViewModelState_Saves() // Renamed slightly
    {
        // Arrange: Setup - Add root OR node and child Substring
        _viewModel.AddFilterCommand.Execute("Or"); _testContext.Send(_ => { }, null);
        var root = _viewModel.ActiveFilterProfile?.RootFilterViewModel; Assert.IsNotNull(root);
        _viewModel.SelectedFilterNode = root;
        _viewModel.AddFilterCommand.Execute("Substring"); _testContext.Send(_ => { }, null);
        var child = root.Children[0];
        _viewModel.SelectedFilterNode = child; // Select the child
        _mockSettings.ResetSettings();

        // Act: Remove the child
        _viewModel.RemoveFilterNodeCommand.Execute(null);
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert ViewModel State
        Assert.AreEqual(0, root.Children.Count, "Child should be removed from parent VM.");
        Assert.AreSame(root, _viewModel.SelectedFilterNode, "Parent node should be selected.");

        // Assert Observable Effects (Check the resulting state of FilteredLogLines)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount should be 0.");

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
        var savedRoot = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault()?.RootFilter as OrFilter;
        Assert.IsNotNull(savedRoot, "Saved root filter is null or not OrFilter.");
        Assert.AreEqual(0, savedRoot.SubFilters.Count, "Saved child count should be zero.");
    }

    // Verifies: [ReqFilterNodeEditInlinev1]
    // Verifies: [ReqFilterDynamicUpdateViewv1] (Triggered by edit end)
    // Verifies: [ReqPersistSettingFilterProfilesv1] (Saving after edit)
    [TestMethod] public void ToggleEditNodeCommand_EndEdit_UpdatesViewModelState_Saves() // Renamed slightly
    {
        // Arrange: Setup - Add root Substring node
        _viewModel.AddFilterCommand.Execute("Substring"); _testContext.Send(_ => { }, null);
        var node = _viewModel.ActiveFilterProfile?.RootFilterViewModel; Assert.IsNotNull(node);
        _viewModel.SelectedFilterNode = node;
        node.BeginEditCommand.Execute(null); // Start editing
        _mockSettings.ResetSettings();
        string updatedValue = "Updated Value";

        // Act: Simulate text change and EndEdit via Toggle command
        node.FilterText = updatedValue; // Update text property
        _viewModel.ToggleEditNodeCommand.Execute(null); // End editing
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert ViewModel State
        Assert.IsFalse(node.IsEditing, "Node should not be in editing state.");
        Assert.AreEqual(updatedValue, node.Filter.Value, "Model value mismatch after edit.");

        // Assert Observable Effects (Check the resulting state of FilteredLogLines)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount should be 0.");

        // Assert Persistence
        Assert.IsNotNull(_mockSettings.SavedSettings, "Settings were not saved.");
        var savedRoot = _mockSettings.SavedSettings?.FilterProfiles.FirstOrDefault()?.RootFilter as SubstringFilter;
        Assert.IsNotNull(savedRoot, "Saved root filter is null or not SubstringFilter.");
        Assert.AreEqual(updatedValue, savedRoot.Value, "Saved filter value mismatch.");
    }

    // Verifies: [ReqFilterNodeToggleEnablev1], [ReqFilterDynamicUpdateViewv1]
    [TestMethod] public void FilterViewModel_EnabledChanged_TriggersUpdate_DoesNotSave() // Renamed slightly
    {
        // Arrange: Setup - Add a node
        _viewModel.AddFilterCommand.Execute("Substring"); _testContext.Send(_ => { }, null);
        var node = _viewModel.ActiveFilterProfile?.RootFilterViewModel; Assert.IsNotNull(node);
        Assert.IsTrue(node.Enabled, "Node should be enabled initially.");
        _mockSettings.ResetSettings(); // Reset settings to check if save happens

        // Act: Change enabled state via VM property
        node.Enabled = false;
        _testContext.Send(_ => { }, null); // Flush context queue

        // Assert Model State
        Assert.IsFalse(node.Filter.Enabled, "Model enabled state mismatch.");

        // Assert Observable Effects (Check the resulting state of FilteredLogLines)
        Assert.AreEqual(0, _viewModel.FilteredLogLines.Count, "FilteredLogLines should be empty after update on empty doc.");
        Assert.AreEqual(0, _viewModel.FilteredLogLinesCount, "FilteredLogLinesCount should be 0.");

        // Assert Persistence (Should NOT save just on Enabled change currently)
        Assert.IsNull(_mockSettings.SavedSettings, "Settings should NOT be saved just on Enabled change.");
    }
}
