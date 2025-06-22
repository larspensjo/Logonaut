using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Filters;
using Logonaut.UI.ViewModels;
using Logonaut.Core.Commands;
using Logonaut.TestUtils;
using System.Linq;
using Logonaut.UI.Commands;

namespace Logonaut.UI.Tests.ViewModels;

[TestClass] public class FilterViewModelTests
{
    private MockCommandExecutor _mockExecutor = null!; // Non-null asserted in TestInitialize

    [TestInitialize] public void TestInitialize()
    {
        // Arrange
        _mockExecutor = new MockCommandExecutor();
    }

    // Helper to create VM with mock executor
    private FilterViewModel CreateViewModel(IFilter filter, FilterViewModel? parent = null)
    {
        // Act
        return new FilterViewModel(filter, _mockExecutor, parent);
    }

    // === DisplayText Tests ===

    // Verifies: [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void DisplayText_ShouldReturnCorrectFormat_ForSubstringFilter()
    {
        // Arrange
        var filter = new SubstringFilter("test");
        var viewModel = CreateViewModel(filter);
        
        // Act & Assert
        Assert.AreEqual("\"test\"", viewModel.DisplayText);
    }

    // Verifies: [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void DisplayText_ShouldReturnCorrectFormat_ForRegexFilter()
    {
        // Arrange
        var filter = new RegexFilter("pattern");
        var viewModel = CreateViewModel(filter);
        
        // Act & Assert
        Assert.AreEqual("/pattern/", viewModel.DisplayText);
    }

    // Verifies: [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void DisplayText_ShouldReturnCorrectSymbol_ForAndFilter()
    {
        // Arrange
        var filter = new AndFilter();
        var viewModel = CreateViewModel(filter);
        
        // Act & Assert
        Assert.AreEqual("∧", viewModel.DisplayText);
    }

    // Verifies: [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void DisplayText_ShouldReturnCorrectSymbol_ForOrFilter()
    {
        // Arrange
        var filter = new OrFilter();
        var viewModel = CreateViewModel(filter);
        
        // Act & Assert
        Assert.AreEqual("∨", viewModel.DisplayText);
    }

    // Verifies: [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void DisplayText_ShouldReturnCorrectSymbol_ForNorFilter()
    {
        // Arrange
        var filter = new NorFilter();
        var viewModel = CreateViewModel(filter);
        
        // Act & Assert
        Assert.AreEqual("¬∨", viewModel.DisplayText);
    }

    // Verifies: [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void DisplayText_ShouldReturnCorrectText_ForTrueFilter()
    {
        // Arrange
        var filter = new TrueFilter();
        var viewModel = CreateViewModel(filter);
        
        // Act & Assert
        Assert.AreEqual("TRUE", viewModel.DisplayText);
    }


    // === FilterType Tests ===

    [TestMethod] public void FilterType_ShouldReturnCorrectTypeString_ForVariousFilters()
    {
        // Act & Assert
        Assert.AreEqual("SubstringType", CreateViewModel(new SubstringFilter("")).FilterType);
        Assert.AreEqual("RegexType", CreateViewModel(new RegexFilter("")).FilterType);
        Assert.AreEqual("AndType", CreateViewModel(new AndFilter()).FilterType);
        Assert.AreEqual("OrType", CreateViewModel(new OrFilter()).FilterType);
        Assert.AreEqual("NorType", CreateViewModel(new NorFilter()).FilterType);
        Assert.AreEqual("TRUE", CreateViewModel(new TrueFilter()).FilterType);
    }

    // === IsEditable Tests ===

    // Verifies: [ReqFilterRuleEditingv1]
    [TestMethod] public void IsEditable_ShouldBeTrue_ForSubstringAndRegexFilters()
    {
        // Act & Assert
        Assert.IsTrue(CreateViewModel(new SubstringFilter("")).IsEditable);
        Assert.IsTrue(CreateViewModel(new RegexFilter("")).IsEditable);
    }

    // Verifies: [ReqFilterRuleEditingv1]
    [TestMethod] public void IsEditable_ShouldBeFalse_ForCompositeAndTrueFilters()
    {
        // Act & Assert
        Assert.IsFalse(CreateViewModel(new AndFilter()).IsEditable);
        Assert.IsFalse(CreateViewModel(new OrFilter()).IsEditable);
        Assert.IsFalse(CreateViewModel(new NorFilter()).IsEditable);
        Assert.IsFalse(CreateViewModel(new TrueFilter()).IsEditable);
    }

    // Child Management Tests

    // Verifies: [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void Constructor_ShouldInitializeChildren_ForCompositeFilter()
    {
        // Arrange
        var child1 = new SubstringFilter("c1");
        var child2 = new RegexFilter("c2");
        var compositeFilter = new AndFilter();
        compositeFilter.Add(child1);
        compositeFilter.Add(child2);

        // Act
        var parentVM = CreateViewModel(compositeFilter);

        // Assert
        Assert.AreEqual(2, parentVM.Children.Count);
        Assert.AreEqual(child1, parentVM.Children[0].Filter);
        Assert.AreEqual(parentVM, parentVM.Children[0].Parent);
        Assert.AreSame(_mockExecutor, parentVM.Children[0].CommandExecutor, "Child VM should receive same executor instance.");
        Assert.AreEqual(child2, parentVM.Children[1].Filter);
        Assert.AreEqual(parentVM, parentVM.Children[1].Parent);
        Assert.AreSame(_mockExecutor, parentVM.Children[1].CommandExecutor, "Child VM should receive same executor instance.");
    }

    // Verifies: [ReqFilterAddSubNodev1], [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void AddChildFilter_ShouldExecuteAddActionAndAddChild()
    {
        // Arrange
        var compositeFilter = new AndFilter();
        var parentVM = CreateViewModel(compositeFilter);
        int initialVMCount = parentVM.Children.Count;
        var childFilter = new SubstringFilter("child");

        // Act
        parentVM.AddChildFilter(childFilter);

        // Assert
        Assert.AreEqual(initialVMCount + 1, parentVM.Children.Count, "Child ViewModel count should increase.");
        Assert.IsInstanceOfType(_mockExecutor.LastExecutedAction, typeof(AddFilterAction), "Executor should have received AddFilterAction.");
        var addedChildVM = parentVM.Children.LastOrDefault(vm => vm.Filter == childFilter);
        Assert.IsNotNull(addedChildVM, "Child VM with correct filter not found.");
        Assert.AreEqual(childFilter, addedChildVM.Filter, "ViewModel's model should match added filter.");
        Assert.AreEqual(parentVM, addedChildVM.Parent, "Child ViewModel should have correct parent reference.");
        Assert.IsTrue(compositeFilter.SubFilters.Contains(childFilter), "Child filter model should be in parent model's SubFilters.");
    }

    // Verifies: [ReqFilterRemoveNodev1], [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void RemoveChild_ShouldExecuteRemoveActionAndRemoveChild()
    {
        // Arrange
        var compositeFilter = new AndFilter();
        var parentVM = CreateViewModel(compositeFilter);
        var childFilter = new SubstringFilter("child");
        parentVM.AddChildFilter(childFilter);
        var childVM = parentVM.Children.First(vm => vm.Filter == childFilter);
        int vmCountAfterAdd = parentVM.Children.Count;
        _mockExecutor.Reset();

        // Act
        parentVM.RemoveChild(childVM);

        // Assert
        Assert.AreEqual(vmCountAfterAdd - 1, parentVM.Children.Count, "Child ViewModel count should decrease.");
        Assert.IsFalse(parentVM.Children.Contains(childVM), "Removed child VM should not be present.");
        Assert.IsInstanceOfType(_mockExecutor.LastExecutedAction, typeof(RemoveFilterAction), "Executor should have received RemoveFilterAction.");
        Assert.IsFalse(compositeFilter.SubFilters.Contains(childFilter), "Child filter model should be removed from parent model's SubFilters.");
    }

    // Enabled Property Tests

    // Verifies: [ReqFilterEnableDisablev1]
    [TestMethod] public void Enabled_Setter_ShouldExecuteToggleAction()
    {
        // Arrange
        var filter = new SubstringFilter("test") { Enabled = true };
        var viewModel = CreateViewModel(filter);
        var receivedNotifications = new List<string>();
        viewModel.PropertyChanged += (s, e) => receivedNotifications.Add(e.PropertyName ?? string.Empty);
        _mockExecutor.Reset();

        // Act
        viewModel.Enabled = false;

        // Assert
        Assert.IsFalse(filter.Enabled, "Filter.Enabled should be false after setting.");
        Assert.IsFalse(viewModel.Enabled, "ViewModel.Enabled should be false after setting.");
        Assert.IsInstanceOfType(_mockExecutor.LastExecutedAction, typeof(ToggleFilterEnabledAction));
        Assert.IsTrue(receivedNotifications.Contains("Enabled"), "PropertyChanged for Enabled not received on set false.");
        receivedNotifications.Clear();
        _mockExecutor.Reset();

        // Act
        viewModel.Enabled = true;

        // Assert
        Assert.IsTrue(filter.Enabled, "Filter.Enabled should be true after setting back.");
        Assert.IsTrue(viewModel.Enabled, "ViewModel.Enabled should be true after setting back.");
        Assert.IsInstanceOfType(_mockExecutor.LastExecutedAction, typeof(ToggleFilterEnabledAction));
        Assert.IsTrue(receivedNotifications.Contains("Enabled"), "PropertyChanged for Enabled not received on set true.");
    }

    // Verifies: [ReqFilterEnableDisablev1]
    [TestMethod] public void Enabled_Getter_ShouldReflectModel()
    {
        // Arrange
        var filter = new SubstringFilter("test");
        var viewModel = CreateViewModel(filter);

        // Act & Assert
        filter.Enabled = false;
        Assert.IsFalse(viewModel.Enabled, "ViewModel.Enabled should reflect model state false.");
        
        // Act & Assert
        filter.Enabled = true;
        Assert.IsTrue(viewModel.Enabled, "ViewModel.Enabled should reflect model state true.");
    }

    // FilterText Property Tests

    // Verifies: [ReqFilterRuleEditingv1]
    [TestMethod] public void FilterText_GetSet_ShouldWork_ForEditableFilters_DuringEdit()
    {
        // Arrange
        var substringFilter = new SubstringFilter("initialSub");
        var regexFilter = new RegexFilter("initialRegex");
        var substringVM = CreateViewModel(substringFilter);
        var regexVM = CreateViewModel(regexFilter);

        // Act & Assert
        Assert.AreEqual("initialSub", substringVM.FilterText);
        substringVM.FilterText = "newSub";
        Assert.AreEqual("newSub", substringFilter.Value);
        Assert.AreEqual("newSub", substringVM.FilterText);

        // Act & Assert
        Assert.AreEqual("initialRegex", regexVM.FilterText);
        regexVM.FilterText = "newRegex";
        Assert.AreEqual("newRegex", regexFilter.Value);
        Assert.AreEqual("newRegex", regexVM.FilterText);
    }

    // Verifies: [ReqFilterRuleEditingv1]
    [TestMethod] public void FilterText_Get_ShouldReturnEmpty_ForNonEditableFilters()
    {
        // Arrange
        var andVM = CreateViewModel(new AndFilter());

        // Act & Assert
        Assert.AreEqual(string.Empty, andVM.FilterText);
    }

    // Verifies: [ReqFilterRuleEditingv1]
    [ExpectedException(typeof(InvalidOperationException))]
    [TestMethod] public void FilterText_Set_ShouldThrow_ForNonEditableFilter()
    {
        // Arrange
        var andVM = CreateViewModel(new AndFilter());

        // Act
        andVM.FilterText = "newValue";
    }

    // Verifies: [ReqFilterRuleEditingv1]
    [TestMethod] public void FilterText_Set_ShouldRaisePropertyChanged_DuringEdit()
    {
        // Arrange
        var filter = new SubstringFilter("old");
        var viewModel = CreateViewModel(filter);
        var receivedNotifications = new List<string>();
        viewModel.PropertyChanged += (s, e) => receivedNotifications.Add(e.PropertyName ?? string.Empty);

        // Act
        viewModel.FilterText = "new";

        // Assert
        CollectionAssert.Contains(receivedNotifications, "FilterText", "FilterText notification missing.");
        CollectionAssert.Contains(receivedNotifications, "DisplayText", "DisplayText notification missing.");
    }

    // Editing Commands and State

    // Verifies: [ReqFilterRuleEditingv1]
    [TestMethod] public void BeginEditCommand_ShouldSetEditingState_ForEditableFilter()
    {
        // Arrange
        var filter = new SubstringFilter("edit me");
        var viewModel = CreateViewModel(filter);

        // Act
        viewModel.BeginEditCommand.Execute(null);

        // Assert
        Assert.IsTrue(viewModel.IsEditing);
        Assert.IsFalse(viewModel.IsNotEditing);
    }

    // Verifies: [ReqFilterRuleEditingv1]
    [TestMethod] public void BeginEditCommand_ShouldDoNothing_ForNonEditableFilter()
    {
        // Arrange
        var filter = new AndFilter();
        var viewModel = CreateViewModel(filter);

        // Act
        viewModel.BeginEditCommand.Execute(null);

        // Assert
        Assert.IsFalse(viewModel.IsEditing);
        Assert.IsTrue(viewModel.IsNotEditing);
    }

    // Verifies: [ReqFilterRuleEditingv1]
    [TestMethod] public void EndEditCommand_ShouldResetEditingState()
    {
        // Arrange
        var filter = new SubstringFilter("edit me");
        var viewModel = CreateViewModel(filter);
        viewModel.BeginEditCommand.Execute(null);

        // Act
        viewModel.EndEditCommand.Execute(null);

        // Assert
        Assert.IsFalse(viewModel.IsEditing);
        Assert.IsTrue(viewModel.IsNotEditing);
    }

    // Verifies: [ReqFilterRuleEditingv1]
    [TestMethod] public void EndEditCommand_ShouldExecuteChangeAction_WhenValueChanged()
    {
        // Arrange
        string oldValue = "old value";
        string newValue = "new value";
        var filter = new SubstringFilter(oldValue);
        var viewModel = CreateViewModel(filter);
        viewModel.BeginEditCommand.Execute(null);
        viewModel.FilterText = newValue;
        _mockExecutor.Reset();

        // Act
        viewModel.EndEditCommand.Execute(null);

        // Assert
        Assert.IsFalse(viewModel.IsEditing);
        Assert.IsTrue(viewModel.IsNotEditing);
        Assert.IsInstanceOfType(_mockExecutor.LastExecutedAction, typeof(ChangeFilterValueAction));
        var action = (ChangeFilterValueAction)_mockExecutor.LastExecutedAction!;
        Assert.AreEqual(newValue, viewModel.Filter.Value);
    }

    // Verifies: [ReqFilterRuleEditingv1]
    [TestMethod] public void EndEditCommand_ShouldNotExecuteAction_WhenValueUnchanged()
    {
        // Arrange
        string value = "old value";
        var filter = new SubstringFilter(value);
        var viewModel = CreateViewModel(filter);
        viewModel.BeginEditCommand.Execute(null);
        _mockExecutor.Reset();

        // Act
        viewModel.EndEditCommand.Execute(null);

        // Assert
        Assert.IsFalse(viewModel.IsEditing);
        Assert.IsTrue(viewModel.IsNotEditing);
        Assert.IsNull(_mockExecutor.LastExecutedAction, "No action should have been executed.");
        Assert.AreEqual(value, viewModel.Filter.Value);
    }
}
