using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.UI.ViewModels;

namespace Logonaut.UI.Tests
{
    [TestClass]
    public class MainViewModelTests
    {
        [TestMethod]
        public void AddFilterCommand_Increases_FilterProfiles_Count()
        {
            // Arrange
            var vm = new MainViewModel();
            int initialCount = vm.FilterProfiles.Count;

            // Act
            vm.AddFilterCommand.Execute(null);

            // Assert
            Assert.AreEqual(initialCount + 1, vm.FilterProfiles.Count);
        }

        [TestMethod]
        public void RemoveFilterCommand_Decreases_FilterProfiles_Count()
        {
            // Arrange
            var vm = new MainViewModel();
            vm.AddFilterCommand.Execute(null); // Ensure there is at least one filter.
            int countAfterAdd = vm.FilterProfiles.Count;

            // Act
            vm.RemoveFilterCommand.Execute(null);

            // Assert
            Assert.AreEqual(countAfterAdd - 1, vm.FilterProfiles.Count);
        }

        [TestMethod]
        public void SearchCommands_Are_Enabled_When_SearchText_Is_NotEmpty()
        {
            // Arrange
            var vm = new MainViewModel();
            vm.SearchText = "test";

            // Act & Assert
            Assert.IsTrue(vm.PreviousSearchCommand.CanExecute(null));
            Assert.IsTrue(vm.NextSearchCommand.CanExecute(null));
        }

        [TestMethod]
        public void SearchCommands_Are_Disabled_When_SearchText_Is_Empty()
        {
            // Arrange
            var vm = new MainViewModel();
            vm.SearchText = "";

            // Act & Assert
            Assert.IsFalse(vm.PreviousSearchCommand.CanExecute(null));
            Assert.IsFalse(vm.NextSearchCommand.CanExecute(null));
        }
    }
}
