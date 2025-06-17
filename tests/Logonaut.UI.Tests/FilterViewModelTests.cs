using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Filters;
using Logonaut.UI.ViewModels;
using Logonaut.Core.Commands; // Added
using Logonaut.TestUtils; // Added
using System.Linq;
using Logonaut.UI.Commands;

namespace Logonaut.UI.Tests.ViewModels;

[TestClass] public class FilterViewModelTests
{
    private MockCommandExecutor _mockExecutor = null!; // Non-null asserted in TestInitialize

    [TestInitialize] public void TestInitialize()
    {
        // Create a fresh mock executor for each test
        _mockExecutor = new MockCommandExecutor();
    }

    // Helper to create VM with mock executor
    private FilterViewModel CreateViewModel(IFilter filter, FilterViewModel? parent = null)
    {
        // Pass the mock executor instance
        return new FilterViewModel(filter, _mockExecutor, parent);
    }

    // === DisplayText Tests ===
    // These tests remain unchanged as they only read properties

    [TestMethod] public void DisplayText_ShouldReturnCorrectFormat_ForSubstringFilter()
    {
        var filter = new SubstringFilter("test");
        var viewModel = CreateViewModel(filter); // Use helper
        Assert.AreEqual("\"test\"", viewModel.DisplayText);
    }

    [TestMethod] public void DisplayText_ShouldReturnCorrectFormat_ForRegexFilter()
    {
        var filter = new RegexFilter("pattern");
        var viewModel = CreateViewModel(filter); // Use helper
        Assert.AreEqual("/pattern/", viewModel.DisplayText);
    }

    [TestMethod] public void DisplayText_ShouldReturnCorrectSymbol_ForAndFilter()
    {
        var filter = new AndFilter();
        var viewModel = CreateViewModel(filter); // Use helper
        Assert.AreEqual("∧", viewModel.DisplayText);
    }

    [TestMethod] public void DisplayText_ShouldReturnCorrectSymbol_ForOrFilter()
    {
        var filter = new OrFilter();
        var viewModel = CreateViewModel(filter); // Use helper
        Assert.AreEqual("∨", viewModel.DisplayText);
    }

     [TestMethod] public void DisplayText_ShouldReturnCorrectSymbol_ForNorFilter()
    {
        var filter = new NorFilter();
        var viewModel = CreateViewModel(filter); // Use helper
        Assert.AreEqual("¬∨", viewModel.DisplayText);
    }

    [TestMethod] public void DisplayText_ShouldReturnCorrectText_ForTrueFilter()
    {
        var filter = new TrueFilter();
        var viewModel = CreateViewModel(filter); // Use helper
        Assert.AreEqual("TRUE", viewModel.DisplayText);
    }


    // === FilterType Tests ===
    // These tests remain unchanged

    [TestMethod] public void FilterType_ShouldReturnCorrectTypeString_ForVariousFilters()
    {
        Assert.AreEqual("SubstringType", CreateViewModel(new SubstringFilter("")).FilterType);
        Assert.AreEqual("RegexType", CreateViewModel(new RegexFilter("")).FilterType);
        Assert.AreEqual("AndType", CreateViewModel(new AndFilter()).FilterType);
        Assert.AreEqual("OrType", CreateViewModel(new OrFilter()).FilterType);
        Assert.AreEqual("NorType", CreateViewModel(new NorFilter()).FilterType);
        Assert.AreEqual("TRUE", CreateViewModel(new TrueFilter()).FilterType);
    }

    // === IsEditable Tests ===
    // These tests remain unchanged

    [TestMethod] public void IsEditable_ShouldBeTrue_ForSubstringAndRegexFilters()
    {
        Assert.IsTrue(CreateViewModel(new SubstringFilter("")).IsEditable);
        Assert.IsTrue(CreateViewModel(new RegexFilter("")).IsEditable);
    }

    [TestMethod] public void IsEditable_ShouldBeFalse_ForCompositeAndTrueFilters()
    {
        Assert.IsFalse(CreateViewModel(new AndFilter()).IsEditable);
        Assert.IsFalse(CreateViewModel(new OrFilter()).IsEditable);
        Assert.IsFalse(CreateViewModel(new NorFilter()).IsEditable);
        Assert.IsFalse(CreateViewModel(new TrueFilter()).IsEditable);
    }

    // Child Management Tests

    [TestMethod] public void Constructor_ShouldInitializeChildren_ForCompositeFilter()
    {
        // Arrange
        var child1 = new SubstringFilter("c1");
        var child2 = new RegexFilter("c2");
        var compositeFilter = new AndFilter();
        compositeFilter.Add(child1);
        compositeFilter.Add(child2);

        // Act
        var parentVM = CreateViewModel(compositeFilter); // Use helper

        // Assert
        Assert.AreEqual(2, parentVM.Children.Count);
        Assert.AreEqual(child1, parentVM.Children[0].Filter);
        Assert.AreEqual(parentVM, parentVM.Children[0].Parent);
        Assert.AreSame(_mockExecutor, parentVM.Children[0].CommandExecutor, "Child VM should receive same executor instance."); // Verify executor propagation
        Assert.AreEqual(child2, parentVM.Children[1].Filter);
        Assert.AreEqual(parentVM, parentVM.Children[1].Parent);
        Assert.AreSame(_mockExecutor, parentVM.Children[1].CommandExecutor, "Child VM should receive same executor instance.");
    }

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
        // Verify model was also updated because mock executor runs Execute()
        Assert.IsTrue(compositeFilter.SubFilters.Contains(childFilter), "Child filter model should be in parent model's SubFilters.");
    }

    [TestMethod] public void RemoveChild_ShouldExecuteRemoveActionAndRemoveChild()
    {
        // Arrange
        var compositeFilter = new AndFilter();
        var parentVM = CreateViewModel(compositeFilter);
        var childFilter = new SubstringFilter("child");
        parentVM.AddChildFilter(childFilter); // Use the method to add, ensuring command pattern consistency
        var childVM = parentVM.Children.First(vm => vm.Filter == childFilter); // Get the specific VM added
        int vmCountAfterAdd = parentVM.Children.Count;
        _mockExecutor.Reset(); // Reset executor before the action under test

        // Act
        parentVM.RemoveChild(childVM);

        // Assert
        Assert.AreEqual(vmCountAfterAdd - 1, parentVM.Children.Count, "Child ViewModel count should decrease.");
        Assert.IsFalse(parentVM.Children.Contains(childVM), "Removed child VM should not be present.");
        Assert.IsInstanceOfType(_mockExecutor.LastExecutedAction, typeof(RemoveFilterAction), "Executor should have received RemoveFilterAction.");
        // Verify model was also updated
        Assert.IsFalse(compositeFilter.SubFilters.Contains(childFilter), "Child filter model should be removed from parent model's SubFilters.");
    }

    // Enabled Property Tests

    [TestMethod] public void Enabled_Setter_ShouldExecuteToggleAction()
    {
        // Arrange
        var filter = new SubstringFilter("test") { Enabled = true }; // Start enabled
        var viewModel = CreateViewModel(filter);
        var receivedNotifications = new List<string>();
        viewModel.PropertyChanged += (s, e) => receivedNotifications.Add(e.PropertyName ?? string.Empty);
        _mockExecutor.Reset();

        // Act: Set to false
        viewModel.Enabled = false;

        // Assert
        Assert.IsFalse(filter.Enabled, "Filter.Enabled should be false after setting.");
        Assert.IsFalse(viewModel.Enabled, "ViewModel.Enabled should be false after setting.");
        Assert.IsInstanceOfType(_mockExecutor.LastExecutedAction, typeof(ToggleFilterEnabledAction));
        Assert.IsTrue(receivedNotifications.Contains("Enabled"), "PropertyChanged for Enabled not received on set false.");
        receivedNotifications.Clear();
        _mockExecutor.Reset();

        // Act: Set back to true
        viewModel.Enabled = true;

        // Assert
        Assert.IsTrue(filter.Enabled, "Filter.Enabled should be true after setting back.");
        Assert.IsTrue(viewModel.Enabled, "ViewModel.Enabled should be true after setting back.");
        Assert.IsInstanceOfType(_mockExecutor.LastExecutedAction, typeof(ToggleFilterEnabledAction));
        Assert.IsTrue(receivedNotifications.Contains("Enabled"), "PropertyChanged for Enabled not received on set true.");
    }

    [TestMethod] public void Enabled_Getter_ShouldReflectModel()
    {
        // Arrange
        var filter = new SubstringFilter("test");
        var viewModel = CreateViewModel(filter);

        // Act & Assert
        filter.Enabled = false;
        Assert.IsFalse(viewModel.Enabled, "ViewModel.Enabled should reflect model state false.");
        filter.Enabled = true;
        Assert.IsTrue(viewModel.Enabled, "ViewModel.Enabled should reflect model state true.");
    }

    // FilterText Property Tests

    [TestMethod] public void FilterText_GetSet_ShouldWork_ForEditableFilters_DuringEdit()
    {
        // Arrange
        var substringFilter = new SubstringFilter("initialSub");
        var regexFilter = new RegexFilter("initialRegex");
        var substringVM = CreateViewModel(substringFilter);
        var regexVM = CreateViewModel(regexFilter);

        // Act & Assert Substring (No BeginEdit needed for direct property access)
        Assert.AreEqual("initialSub", substringVM.FilterText);
        substringVM.FilterText = "newSub"; // Directly sets model during edit typing
        Assert.AreEqual("newSub", substringFilter.Value);
        Assert.AreEqual("newSub", substringVM.FilterText);

        // Act & Assert Regex
        Assert.AreEqual("initialRegex", regexVM.FilterText);
        regexVM.FilterText = "newRegex";
        Assert.AreEqual("newRegex", regexFilter.Value);
        Assert.AreEqual("newRegex", regexVM.FilterText);
    }

    [TestMethod] public void FilterText_Get_ShouldReturnEmpty_ForNonEditableFilters()
    {
        var andVM = CreateViewModel(new AndFilter());
        Assert.AreEqual(string.Empty, andVM.FilterText);
    }

    [ExpectedException(typeof(InvalidOperationException))]
    [TestMethod] public void FilterText_Set_ShouldThrow_ForNonEditableFilter()
    {
        var andVM = CreateViewModel(new AndFilter());
        andVM.FilterText = "newValue"; // Should throw
    }

    [TestMethod] public void FilterText_Set_ShouldRaisePropertyChanged_DuringEdit()
    {
        // Arrange
        var filter = new SubstringFilter("old");
        var viewModel = CreateViewModel(filter);
        var receivedNotifications = new List<string>();
        viewModel.PropertyChanged += (s, e) => receivedNotifications.Add(e.PropertyName ?? string.Empty);

        // Act
        viewModel.FilterText = "new"; // Simulate typing

        // Assert
        CollectionAssert.Contains(receivedNotifications, "FilterText", "FilterText notification missing.");
        CollectionAssert.Contains(receivedNotifications, "DisplayText", "DisplayText notification missing.");
    }

    // Editing Commands and State

    [TestMethod] public void BeginEditCommand_ShouldSetEditingState_ForEditableFilter()
    {
        var filter = new SubstringFilter("edit me");
        var viewModel = CreateViewModel(filter);

        viewModel.BeginEditCommand.Execute(null);

        Assert.IsTrue(viewModel.IsEditing);
        Assert.IsFalse(viewModel.IsNotEditing);
    }

    [TestMethod] public void BeginEditCommand_ShouldDoNothing_ForNonEditableFilter()
    {
        var filter = new AndFilter();
        var viewModel = CreateViewModel(filter);

        viewModel.BeginEditCommand.Execute(null);

        Assert.IsFalse(viewModel.IsEditing); // Should remain false
        Assert.IsTrue(viewModel.IsNotEditing);
    }

    [TestMethod] public void EndEditCommand_ShouldResetEditingState()
    {
        var filter = new SubstringFilter("edit me");
        var viewModel = CreateViewModel(filter);
        viewModel.BeginEditCommand.Execute(null); // Enter editing

        viewModel.EndEditCommand.Execute(null); // Act

        Assert.IsFalse(viewModel.IsEditing);
        Assert.IsTrue(viewModel.IsNotEditing);
    }

    [TestMethod] public void EndEditCommand_ShouldExecuteChangeAction_WhenValueChanged()
    {
        // Arrange
        string oldValue = "old value";
        string newValue = "new value";
        var filter = new SubstringFilter(oldValue);
        var viewModel = CreateViewModel(filter);
        viewModel.BeginEditCommand.Execute(null); // Enter editing
        viewModel.FilterText = newValue; // Change the text
        _mockExecutor.Reset();

        // Act
        viewModel.EndEditCommand.Execute(null);

        // Assert
        Assert.IsFalse(viewModel.IsEditing);
        Assert.IsTrue(viewModel.IsNotEditing);
        Assert.IsInstanceOfType(_mockExecutor.LastExecutedAction, typeof(ChangeFilterValueAction));
        var action = (ChangeFilterValueAction)_mockExecutor.LastExecutedAction!;
        // Note: Action stores values *passed to it*, not necessarily current model state if Execute failed
        // But since our mock runs Execute, the model should be updated.
        Assert.AreEqual(newValue, viewModel.Filter.Value);
    }

     [TestMethod] public void EndEditCommand_ShouldNotExecuteAction_WhenValueUnchanged()
    {
        // Arrange
        string value = "old value";
        var filter = new SubstringFilter(value);
        var viewModel = CreateViewModel(filter);
        viewModel.BeginEditCommand.Execute(null); // Enter editing
        // DO NOT change FilterText
        _mockExecutor.Reset();

        // Act
        viewModel.EndEditCommand.Execute(null);

        // Assert
        Assert.IsFalse(viewModel.IsEditing);
        Assert.IsTrue(viewModel.IsNotEditing);
        Assert.IsNull(_mockExecutor.LastExecutedAction, "No action should have been executed.");
        Assert.AreEqual(value, viewModel.Filter.Value); // Verify value didn't change
    }

    // === Callback/Notification Tests (REMOVED) ===
    // Tests like Enabled_Set_ShouldInvokeCallback are removed because
    // the notification/update logic is now centralized in ICommandExecutor.Execute.
    // We now test that the correct *Action* is created and executed instead.
}
