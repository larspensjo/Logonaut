using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.UI.ViewModels;
using Logonaut.Filters;
using Logonaut.UI.Services;

namespace Logonaut.UI.Tests.ViewModels
{
    [TestClass]
    public class MainViewModelTests
    {
        // A simple mock of IFileDialogService that always returns a preset file name.
        private class MockFileDialogService : IFileDialogService
        {
            public string FileToReturn { get; set; } = "C:\\fake\\log.txt";
            public string OpenFile(string title, string filter) => FileToReturn;
        }

        [TestMethod]
        public void AddSubstringFilter_NoExistingFilter_ShouldCreateRootFilter()
        {
            // Arrange
            var viewModel = new MainViewModel();

            // Act
            viewModel.AddSubstringFilterCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, viewModel.FilterProfiles.Count, "A root filter should be added.");
            Assert.IsNotNull(viewModel.SelectedFilter, "SelectedFilter should be set.");
            Assert.IsInstanceOfType(viewModel.SelectedFilter.FilterModel, typeof(SubstringFilter), "Root filter should be a SubstringFilter.");
        }

        [TestMethod]
        public void AddAndFilter_NoExistingFilter_ShouldCreateRootFilter()
        {
            // Arrange
            var viewModel = new MainViewModel();

            // Act
            viewModel.AddAndFilterCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, viewModel.FilterProfiles.Count, "A root AND filter should be added.");
            Assert.IsNotNull(viewModel.SelectedFilter, "SelectedFilter should be set.");
            Assert.IsInstanceOfType(viewModel.SelectedFilter.FilterModel, typeof(AndFilter), "Root filter should be an AndFilter.");
        }

        [TestMethod]
        public void AddOrFilter_NoExistingFilter_ShouldCreateRootFilter()
        {
            // Arrange
            var viewModel = new MainViewModel();

            // Act
            viewModel.AddOrFilterCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, viewModel.FilterProfiles.Count, "A root OR filter should be added.");
            Assert.IsNotNull(viewModel.SelectedFilter, "SelectedFilter should be set.");
            Assert.IsInstanceOfType(viewModel.SelectedFilter.FilterModel, typeof(OrFilter), "Root filter should be an OrFilter.");
        }

        [TestMethod]
        public void AddNegationFilter_NoExistingFilter_ShouldCreateRootFilter()
        {
            // Arrange
            var viewModel = new MainViewModel();

            // Act
            viewModel.AddNegationFilterCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, viewModel.FilterProfiles.Count, "A root NOT filter should be added.");
            Assert.IsNotNull(viewModel.SelectedFilter, "SelectedFilter should be set.");
            Assert.IsInstanceOfType(viewModel.SelectedFilter.FilterModel, typeof(NegationFilter), "Root filter should be a NegationFilter.");
        }

        [TestMethod]
        public void AddFilter_WithExistingCompositeFilter_ShouldAddChildFilter()
        {
            // Arrange
            var compositeModel = new AndFilter();
            var rootVM = new FilterViewModel(compositeModel);
            var viewModel = new MainViewModel();
            viewModel.FilterProfiles.Add(rootVM);
            viewModel.SelectedFilter = rootVM;

            // Act
            viewModel.AddSubstringFilterCommand.Execute(null);

            // Assert
            Assert.AreEqual(1, rootVM.Children.Count, "A child filter should be added to the AND filter.");
            Assert.IsInstanceOfType(rootVM.Children[0].FilterModel, typeof(SubstringFilter), "Child filter should be a SubstringFilter.");
        }

        [TestMethod]
        public void RemoveFilter_RootFilter_ShouldRemoveItAndClearSelection()
        {
            // Arrange
            var rootVM = new FilterViewModel(new SubstringFilter("Test"));
            var viewModel = new MainViewModel();
            viewModel.FilterProfiles.Add(rootVM);
            viewModel.SelectedFilter = rootVM;

            // Act
            viewModel.RemoveFilterCommand.Execute(null);

            // Assert
            Assert.AreEqual(0, viewModel.FilterProfiles.Count, "Root filter should be removed.");
            Assert.IsNull(viewModel.SelectedFilter, "SelectedFilter should be cleared.");
        }

        [TestMethod]
        public void RemoveFilter_ChildFilter_ShouldRemoveItFromParent()
        {
            // Arrange
            var compositeModel = new AndFilter();
            var rootVM = new FilterViewModel(compositeModel);
            var childModel = new SubstringFilter("Child");
            rootVM.AddChildFilter(childModel);
            var viewModel = new MainViewModel();
            viewModel.FilterProfiles.Add(rootVM);
            viewModel.SelectedFilter = rootVM.Children[0];

            // Act
            viewModel.RemoveFilterCommand.Execute(null);

            // Assert
            Assert.AreEqual(0, rootVM.Children.Count, "Child filter should be removed from parent.");
        }

        [TestMethod]
        public void PreviousSearch_WithNonEmptySearchText_ShouldAppendMessage()
        {
            // Arrange
            var viewModel = new MainViewModel();
            viewModel.SearchText = "test";

            // Act
            viewModel.PreviousSearchCommand.Execute(null);

            // Assert
            var combined = string.Join("\n", viewModel.VisibleLogLines);
            StringAssert.Contains(combined, "Previous search executed.", "VisibleLogLines should contain the previous search message.");
        }

        [TestMethod]
        public void NextSearch_WithNonEmptySearchText_ShouldAppendMessage()
        {
            // Arrange
            var viewModel = new MainViewModel();
            viewModel.SearchText = "test";

            // Act
            viewModel.NextSearchCommand.Execute(null);

            // Assert
            var combined = string.Join("\n", viewModel.VisibleLogLines);
            StringAssert.Contains(combined, "Next search executed.", "VisibleLogLines should contain the next search message.");
        }

#if false
        // TODO: Need a mock for ThemeViewModel
        [TestMethod]
        public void OpenLogFile_ShouldSetCurrentLogFilePath()
        {
            // Arrange
            var fakeService = new MockFileDialogService { FileToReturn = "C:\\fake\\log.txt" };
            var viewModel = new MainViewModel(fakeService);

            // Act
            viewModel.OpenLogFileCommand.Execute(null);

            // Assert
            Assert.AreEqual("C:\\fake\\log.txt", viewModel.CurrentLogFilePath, "CurrentLogFilePath should be updated.");
        }
#endif
    }
}
