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
        // A simple fake IFileDialogService that always returns a preset file name.
        private class MockFileDialogService : IFileDialogService
        {
            public string FileToReturn { get; set; } = "C:\\fake\\log.txt";
            public string OpenFile(string title, string filter) => FileToReturn;
        }

        [TestMethod]
        public void AddFilter_NoExistingFilter_ShouldCreateRootFilter()
        {
            // Arrange
            var fakeService = new MockFileDialogService();
            var viewModel = new MainViewModel(fakeService);

            // Ensure FilterProfiles is empty.
            Assert.AreEqual(0, viewModel.FilterProfiles.Count);

            // Act: Execute the AddFilter command.
            viewModel.AddFilterCommand.Execute(null);

            // Assert: A root filter should have been created and selected.
            Assert.AreEqual(1, viewModel.FilterProfiles.Count, "A root filter should be added.");
            Assert.IsNotNull(viewModel.SelectedFilter, "SelectedFilter should be set.");
            Assert.IsTrue(viewModel.SelectedFilter.DisplayText.StartsWith("Substring:"), "Root filter should be a substring filter.");
        }

        [TestMethod]
        public void AddFilter_WithExistingCompositeFilter_ShouldAddChildFilter()
        {
            // Arrange: Create a composite filter (AndFilter) as the root.
            var compositeModel = new AndFilter();
            var rootVM = new FilterViewModel(compositeModel);
            var viewModel = new MainViewModel();
            viewModel.FilterProfiles.Add(rootVM);
            viewModel.SelectedFilter = rootVM;

            // Act: Execute AddFilter command.
            viewModel.AddFilterCommand.Execute(null);

            // Assert: A child filter should be added to the selected composite filter.
            Assert.IsTrue(rootVM.Children.Count > 0, "Composite filter should have a child after adding filter.");
            var child = rootVM.Children[0];
            Assert.IsTrue(child.DisplayText.StartsWith("Substring:"), "Child filter should be a substring filter.");
        }

        [TestMethod]
        public void RemoveFilter_RootFilter_ShouldRemoveItAndClearSelection()
        {
            // Arrange: Create a root filter.
            var rootVM = new FilterViewModel(new SubstringFilter("Test"));
            var viewModel = new MainViewModel();
            viewModel.FilterProfiles.Add(rootVM);
            viewModel.SelectedFilter = rootVM;

            // Act: Execute RemoveFilter command.
            viewModel.RemoveFilterCommand.Execute(null);

            // Assert: The root filter should be removed and selection cleared.
            Assert.AreEqual(0, viewModel.FilterProfiles.Count, "Root filter should be removed.");
            Assert.IsNull(viewModel.SelectedFilter, "SelectedFilter should be cleared.");
        }

        [TestMethod]
        public void RemoveFilter_ChildFilter_ShouldRemoveItFromParent()
        {
            // Arrange: Create a composite filter with one child.
            var compositeModel = new AndFilter();
            var rootVM = new FilterViewModel(compositeModel);
            var childModel = new SubstringFilter("Child");
            rootVM.AddChildFilter(childModel);
            Assert.AreEqual(1, rootVM.Children.Count, "Child filter should be added.");
            // Set up MainViewModel with the composite as root.
            var viewModel = new MainViewModel();
            viewModel.FilterProfiles.Add(rootVM);
            viewModel.SelectedFilter = rootVM.Children[0];

            // Act: Execute RemoveFilter command.
            viewModel.RemoveFilterCommand.Execute(null);

            // Assert: The child filter should be removed.
            Assert.AreEqual(0, rootVM.Children.Count, "Child filter should be removed from composite.");
        }

        [TestMethod]
        public void PreviousSearch_WithNonEmptySearchText_ShouldAppendMessage()
        {
            // Arrange
            var viewModel = new MainViewModel();
            viewModel.SearchText = "test";
            string initialLog = viewModel.LogText;

            // Act
            viewModel.PreviousSearchCommand.Execute(null);

            // Assert
            StringAssert.Contains(viewModel.LogText, "Previous search executed.", "LogText should contain the previous search message.");
        }

        [TestMethod]
        public void NextSearch_WithNonEmptySearchText_ShouldAppendMessage()
        {
            // Arrange
            var viewModel = new MainViewModel();
            viewModel.SearchText = "test";
            string initialLog = viewModel.LogText;

            // Act
            viewModel.NextSearchCommand.Execute(null);

            // Assert
            StringAssert.Contains(viewModel.LogText, "Next search executed.", "LogText should contain the next search message.");
        }

        /* TODO: Probably need to mock the LogManager to test this.
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
        */
    }
}
