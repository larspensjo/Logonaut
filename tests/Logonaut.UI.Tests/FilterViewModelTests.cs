using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.Filters;
using Logonaut.UI.ViewModels;

namespace Logonaut.UI.Tests.ViewModels
{
    [TestClass]
    public class FilterViewModelTests
    {
        [TestMethod]
        public void DisplayText_ShouldReturnSubstringFilterText_ForSubstringFilter()
        {
            // Arrange
            var substring = "test";
            IFilter filter = new SubstringFilter(substring);
            var viewModel = new FilterViewModel(filter);

            // Act
            var displayText = viewModel.DisplayText;

            // Assert
            Assert.AreEqual($"Substring: {substring}", displayText);
        }

        [TestMethod]
        public void DisplayText_ShouldReturnAnd_ForAndFilter()
        {
            // Arrange
            IFilter filter = new AndFilter();
            var viewModel = new FilterViewModel(filter);

            // Act
            var displayText = viewModel.DisplayText;

            // Assert
            Assert.AreEqual("AND", displayText);
        }

        [TestMethod]
        public void AddChildFilter_ShouldAddChildToComposite()
        {
            // Arrange
            AndFilter compositeFilter = new AndFilter();
            var parentVM = new FilterViewModel(compositeFilter);
            int initialCount = parentVM.Children.Count;

            // Act
            IFilter childFilter = new SubstringFilter("child");
            parentVM.AddChildFilter(childFilter);

            // Assert
            Assert.AreEqual(initialCount + 1, parentVM.Children.Count);
            // Verify that the last child's FilterModel is a SubstringFilter with the expected value.
            Assert.IsInstanceOfType(parentVM.Children[parentVM.Children.Count - 1].FilterModel, typeof(SubstringFilter));
            SubstringFilter? substringFilter = parentVM.Children[parentVM.Children.Count - 1].FilterModel as SubstringFilter;
            Assert.IsNotNull(substringFilter);
            Assert.AreEqual("child", substringFilter.Value);
        }

        [TestMethod]
        public void RemoveChild_ShouldRemoveChildFromComposite()
        {
            // Arrange
            AndFilter compositeFilter = new AndFilter();
            var parentVM = new FilterViewModel(compositeFilter);
            IFilter childFilter = new SubstringFilter("child");
            parentVM.AddChildFilter(childFilter);
            var childVM = parentVM.Children[0];
            int countAfterAdd = parentVM.Children.Count;

            // Act
            parentVM.RemoveChild(childVM);

            // Assert
            Assert.AreEqual(countAfterAdd - 1, parentVM.Children.Count);
        }

        [TestMethod]
        public void EnabledProperty_ShouldReflectUnderlyingFilterEnabled()
        {
            // Arrange
            IFilter filter = new SubstringFilter("test");
            var viewModel = new FilterViewModel(filter);

            // Act & Assert
            viewModel.Enabled = false;
            Assert.IsFalse(filter.Enabled, "Filter.Enabled should be false after setting ViewModel.Enabled to false.");
            viewModel.Enabled = true;
            Assert.IsTrue(filter.Enabled, "Filter.Enabled should be true after setting ViewModel.Enabled to true.");
        }
    }
}
