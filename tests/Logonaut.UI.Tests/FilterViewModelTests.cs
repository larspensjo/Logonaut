using System;
using System.Collections.Generic; // For List<>
using System.ComponentModel; // For PropertyChangedEventArgs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Filters;
using Logonaut.UI.ViewModels;
using System.Linq; // For Linq methods like FirstOrDefault

namespace Logonaut.UI.Tests.ViewModels
{
    [TestClass]
    public class FilterViewModelTests
    {
        // === DisplayText Tests ===

        [TestMethod]
        public void DisplayText_ShouldReturnCorrectFormat_ForSubstringFilter()
        {
            var filter = new SubstringFilter("test");
            var viewModel = new FilterViewModel(filter);
            Assert.AreEqual("\"test\"", viewModel.DisplayText); // Updated expected value
        }

        [TestMethod]
        public void DisplayText_ShouldReturnCorrectFormat_ForRegexFilter()
        {
            var filter = new RegexFilter("pattern");
            var viewModel = new FilterViewModel(filter);
            Assert.AreEqual("/pattern/", viewModel.DisplayText); // Updated expected value
        }

        [TestMethod]
        public void DisplayText_ShouldReturnCorrectSymbol_ForAndFilter()
        {
            var filter = new AndFilter();
            var viewModel = new FilterViewModel(filter);
            Assert.AreEqual("∧", viewModel.DisplayText); // Updated expected value
        }

        [TestMethod]
        public void DisplayText_ShouldReturnCorrectSymbol_ForOrFilter()
        {
            var filter = new OrFilter();
            var viewModel = new FilterViewModel(filter);
            Assert.AreEqual("∨", viewModel.DisplayText); // Updated expected value
        }

        [TestMethod]
        public void DisplayText_ShouldReturnCorrectSymbol_ForNorFilter()
        {
            var filter = new NorFilter();
            var viewModel = new FilterViewModel(filter);
            Assert.AreEqual("¬∨", viewModel.DisplayText); // Updated expected value
        }

         [TestMethod]
        public void DisplayText_ShouldReturnCorrectText_ForTrueFilter()
        {
            var filter = new TrueFilter();
            var viewModel = new FilterViewModel(filter);
            Assert.AreEqual("TRUE", viewModel.DisplayText); // Updated expected value
        }

        // === FilterType Tests ===

        [TestMethod]
        public void FilterType_ShouldReturnCorrectTypeString_ForVariousFilters()
        {
            Assert.AreEqual("SubstringType", new FilterViewModel(new SubstringFilter("")).FilterType);
            Assert.AreEqual("RegexType", new FilterViewModel(new RegexFilter("")).FilterType);
            Assert.AreEqual("AndType", new FilterViewModel(new AndFilter()).FilterType);
            Assert.AreEqual("OrType", new FilterViewModel(new OrFilter()).FilterType);
            Assert.AreEqual("NorType", new FilterViewModel(new NorFilter()).FilterType);
            Assert.AreEqual("TRUE", new FilterViewModel(new TrueFilter()).FilterType);
        }

        // === IsEditable Tests ===

        [TestMethod]
        public void IsEditable_ShouldBeTrue_ForSubstringAndRegexFilters()
        {
            Assert.IsTrue(new FilterViewModel(new SubstringFilter("")).IsEditable);
            Assert.IsTrue(new FilterViewModel(new RegexFilter("")).IsEditable);
        }

        [TestMethod]
        public void IsEditable_ShouldBeFalse_ForCompositeAndTrueFilters()
        {
            Assert.IsFalse(new FilterViewModel(new AndFilter()).IsEditable);
            Assert.IsFalse(new FilterViewModel(new OrFilter()).IsEditable);
            Assert.IsFalse(new FilterViewModel(new NorFilter()).IsEditable);
            Assert.IsFalse(new FilterViewModel(new TrueFilter()).IsEditable);
        }

        // === Child Management Tests ===

        [TestMethod]
        public void AddChildFilter_ShouldAddChildToComposite()
        {
            var compositeFilter = new AndFilter();
            var parentVM = new FilterViewModel(compositeFilter);
            int initialModelCount = compositeFilter.SubFilters.Count;
            int initialVMCount = parentVM.Children.Count;

            var childFilter = new SubstringFilter("child");
            parentVM.AddChildFilter(childFilter); // Act

            Assert.AreEqual(initialModelCount + 1, compositeFilter.SubFilters.Count, "Child filter should be added to the model.");
            Assert.AreEqual(initialVMCount + 1, parentVM.Children.Count, "Child ViewModel should be added.");
            Assert.AreEqual(childFilter, parentVM.Children.Last().FilterModel, "ViewModel's model should match added filter.");
            Assert.AreEqual(parentVM, parentVM.Children.Last().Parent, "Child ViewModel should have correct parent reference.");
        }

        [TestMethod]
        public void AddChildFilter_ShouldDoNothing_ForNonCompositeFilter()
        {
            var nonCompositeFilter = new SubstringFilter("parent");
            var parentVM = new FilterViewModel(nonCompositeFilter);
            int initialVMCount = parentVM.Children.Count;

            var childFilter = new RegexFilter("child");
            parentVM.AddChildFilter(childFilter); // Act

            // Assert: No changes expected
            Assert.AreEqual(initialVMCount, parentVM.Children.Count, "Should not add child ViewModel to non-composite.");
            // No SubFilters property on SubstringFilter to check model count
        }


        [TestMethod]
        public void RemoveChild_ShouldRemoveChildFromComposite()
        {
            var compositeFilter = new AndFilter();
            var parentVM = new FilterViewModel(compositeFilter);
            var childFilter = new SubstringFilter("child");
            parentVM.AddChildFilter(childFilter);
            var childVM = parentVM.Children.First(); // Assuming only one child was added
            int modelCountAfterAdd = compositeFilter.SubFilters.Count;
            int vmCountAfterAdd = parentVM.Children.Count;

            parentVM.RemoveChild(childVM); // Act

            Assert.AreEqual(modelCountAfterAdd - 1, compositeFilter.SubFilters.Count, "Child filter should be removed from model.");
            Assert.AreEqual(vmCountAfterAdd - 1, parentVM.Children.Count, "Child ViewModel should be removed.");
        }

         [TestMethod]
        public void Constructor_ShouldInitializeChildren_ForCompositeFilter()
        {
            // Arrange
            var child1 = new SubstringFilter("c1");
            var child2 = new RegexFilter("c2");
            var compositeFilter = new AndFilter();
            compositeFilter.Add(child1);
            compositeFilter.Add(child2);

            // Act
            var parentVM = new FilterViewModel(compositeFilter);

            // Assert
            Assert.AreEqual(2, parentVM.Children.Count, "ViewModel should initialize with correct number of children.");
            Assert.AreEqual(child1, parentVM.Children[0].FilterModel);
            Assert.AreEqual(parentVM, parentVM.Children[0].Parent);
            Assert.AreEqual(child2, parentVM.Children[1].FilterModel);
            Assert.AreEqual(parentVM, parentVM.Children[1].Parent);
        }


        // === Enabled Property Tests ===

        [TestMethod]
        public void EnabledProperty_ShouldReflectAndSet_UnderlyingFilterEnabled()
        {
            var filter = new SubstringFilter("test");
            var viewModel = new FilterViewModel(filter);
            var receivedNotifications = new List<string>();
            viewModel.PropertyChanged += (s, e) => receivedNotifications.Add(e.PropertyName);

            // Act & Assert: Set to false
            viewModel.Enabled = false;
            Assert.IsFalse(filter.Enabled, "Filter.Enabled should be false after setting ViewModel.Enabled to false.");
            Assert.IsTrue(receivedNotifications.Contains("Enabled"), "PropertyChanged for Enabled not received on set false.");
            receivedNotifications.Clear();

            // Act & Assert: Set back to true
            viewModel.Enabled = true;
            Assert.IsTrue(filter.Enabled, "Filter.Enabled should be true after setting ViewModel.Enabled to true.");
            Assert.IsTrue(receivedNotifications.Contains("Enabled"), "PropertyChanged for Enabled not received on set true.");

            // Act & Assert: Get should reflect model
            filter.Enabled = false; // Change model directly
            Assert.IsFalse(viewModel.Enabled, "ViewModel.Enabled should reflect model state false.");
            filter.Enabled = true;
            Assert.IsTrue(viewModel.Enabled, "ViewModel.Enabled should reflect model state true.");
        }

        // === FilterText Property Tests ===

        [TestMethod]
        public void FilterText_GetSet_ShouldWork_ForEditableFilters()
        {
            // Arrange
            var substringFilter = new SubstringFilter("initialSub");
            var regexFilter = new RegexFilter("initialRegex");
            var substringVM = new FilterViewModel(substringFilter);
            var regexVM = new FilterViewModel(regexFilter);

            // Act & Assert Substring
            Assert.AreEqual("initialSub", substringVM.FilterText);
            substringVM.FilterText = "newSub";
            Assert.AreEqual("newSub", substringFilter.Value);
            Assert.AreEqual("newSub", substringVM.FilterText);

            // Act & Assert Regex
            Assert.AreEqual("initialRegex", regexVM.FilterText);
            regexVM.FilterText = "newRegex";
            Assert.AreEqual("newRegex", regexFilter.Value);
            Assert.AreEqual("newRegex", regexVM.FilterText);
        }

        [TestMethod]
        public void FilterText_Get_ShouldReturnEmpty_ForNonEditableFilters()
        {
            var andVM = new FilterViewModel(new AndFilter());
            Assert.AreEqual(string.Empty, andVM.FilterText);
        }

        [TestMethod]
        [ExpectedException(typeof(NotSupportedException))]
        public void FilterText_Set_ShouldThrow_ForNonEditableFilter()
        {
            var andVM = new FilterViewModel(new AndFilter());
            andVM.FilterText = "newValue"; // Should throw
        }

        [TestMethod]
        public void FilterText_Set_ShouldRaisePropertyChanged()
        {
            var filter = new SubstringFilter("old");
            var viewModel = new FilterViewModel(filter);
            var receivedNotifications = new List<string>();
            viewModel.PropertyChanged += (s, e) => receivedNotifications.Add(e.PropertyName);

            viewModel.FilterText = "new"; // Act

            // Assert
            CollectionAssert.Contains(receivedNotifications, "FilterText", "FilterText notification missing.");
            CollectionAssert.Contains(receivedNotifications, "DisplayText", "DisplayText notification missing.");
        }

        // === Editing Commands and State ===

        [TestMethod]
        public void BeginEditCommand_ShouldSetEditingState_ForEditableFilter()
        {
            var filter = new SubstringFilter("edit me");
            var viewModel = new FilterViewModel(filter);

            viewModel.BeginEditCommand.Execute(null); // Act

            Assert.IsTrue(viewModel.IsEditing);
            Assert.IsFalse(viewModel.IsNotEditing);
        }

        [TestMethod]
        public void BeginEditCommand_ShouldDoNothing_ForNonEditableFilter()
        {
            var filter = new AndFilter();
            var viewModel = new FilterViewModel(filter);
            bool initialEditing = viewModel.IsEditing;
            bool initialNotEditing = viewModel.IsNotEditing;

            viewModel.BeginEditCommand.Execute(null); // Act

            Assert.AreEqual(initialEditing, viewModel.IsEditing);
            Assert.AreEqual(initialNotEditing, viewModel.IsNotEditing);
        }

        [TestMethod]
        public void EndEditCommand_ShouldResetEditingState()
        {
            var filter = new SubstringFilter("edit me");
            var viewModel = new FilterViewModel(filter);
            viewModel.BeginEditCommand.Execute(null); // Ensure we are editing

            viewModel.EndEditCommand.Execute(null); // Act

            Assert.IsFalse(viewModel.IsEditing);
            Assert.IsTrue(viewModel.IsNotEditing);
        }

        // --- Test for NotifyFilterTextChanged (commented out due to App.Current dependency) ---
        /*
        [TestMethod]
        public void FilterText_Set_ShouldTriggerNotification()
        {
            // Arrange: Requires mocking App.Current or refactoring FilterViewModel
            //          to use messaging or events.
            Assert.Inconclusive("Cannot directly test notification due to App.Current dependency.");
        }

        [TestMethod]
        public void EndEditCommand_ShouldTriggerNotification()
        {
            // Arrange: Requires mocking App.Current or refactoring FilterViewModel
            //          to use messaging or events.
            Assert.Inconclusive("Cannot directly test notification due to App.Current dependency.");
        }
        */
    }
}