using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;
using System.Windows.Controls;
using Logonaut.UI.ViewModels;
using Logonaut.Filters;
using System.Windows.Threading;

namespace Logonaut.UI.Tests.ViewModels;

#if false
// Tests requiring STA thread and actual WPF controls
[TestClass] public class MainViewModel_WpfInteractionTests : MainViewModelTestBase
{
    [TestInitialize] public override void TestInitialize()
    {
        // Arrange
        base.TestInitialize();
        base.SetupMainAndTabViewModel();
    }

    private void DispatcherYield(DispatcherPriority priority = DispatcherPriority.Background)
    {
        // Act
        Dispatcher.CurrentDispatcher.Invoke(() => { }, priority);
    }

    // Verifies: [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void AddFilter_WhenActiveProfileIsEmpty_ShouldUpdateTreeViewItems_STA()
    {
        // Arrange
        TreeView? filterTreeView = null;

        RunOnSta(() =>
        {
            MainWindow? window = null;
            try
            {
                // Arrange
                window = new MainWindow(_viewModel);
                window.Show();
                var windowContentElement = window.Content as UIElement;
                Assert.IsNotNull(windowContentElement, "Window Content is null.");
                filterTreeView = FindVisualChild<TreeView>(windowContentElement, "FilterTreeViewNameForTesting");
                Assert.IsNotNull(filterTreeView, "FilterTreeView not found.");
                Assert.IsNotNull(_viewModel.ActiveFilterProfile, "Pre-condition: Active profile is null.");
                _viewModel.ActiveFilterProfile.SetModelRootFilter(null);
                _viewModel.ActiveTreeRootNodes.Clear();
                Assert.AreEqual(0, filterTreeView.Items.Count, "Pre-condition: TreeView should be empty.");

                // Act
                _viewModel.AddFilterCommand.Execute("Substring");
                DispatcherYield();
                window.UpdateLayout();

                // Assert
                Assert.AreEqual(1, filterTreeView.Items.Count, "TreeView Items collection should have one item.");
                Assert.IsInstanceOfType(filterTreeView.Items[0], typeof(FilterViewModel), "Item is not FilterViewModel");
                var itemViewModel = (FilterViewModel)filterTreeView.Items[0];
                Assert.IsInstanceOfType(itemViewModel.Filter, typeof(SubstringFilter), "Item Filter model is not SubstringFilter.");
            }
            finally
            {
                window?.Close();
            }
        });
    }

    // Verifies: [ReqFilterRuleTreeStructurev1]
    [TestMethod] public void RemoveFilter_ShouldUpdateTreeViewItems_STA()
    {
        // Arrange
        TreeView? filterTreeView = null;

        RunOnSta(() =>
        {
            MainWindow? window = null;
            try
            {
                // Arrange
                window = new MainWindow(_viewModel);
                window.Show();
                var windowContentElement = window.Content as UIElement;
                Assert.IsNotNull(windowContentElement);
                filterTreeView = FindVisualChild<TreeView>(windowContentElement, "FilterTreeViewNameForTesting");
                Assert.IsNotNull(filterTreeView);
                Assert.IsNotNull(_viewModel.ActiveFilterProfile);
                _viewModel.ActiveFilterProfile.SetModelRootFilter(null);
                _viewModel.ActiveTreeRootNodes.Clear();
                _viewModel.AddFilterCommand.Execute("Substring");
                DispatcherYield();
                window.UpdateLayout();
                Assert.AreEqual(1, filterTreeView.Items.Count, "Pre-Remove: TreeView should have 1 item.");
                _viewModel.SelectedFilterNode = _viewModel.ActiveFilterProfile.RootFilterViewModel;
                Assert.IsNotNull(_viewModel.SelectedFilterNode, "Pre-Remove: Node not selected.");

                // Act
                _viewModel.RemoveFilterNodeCommand.Execute(null);
                DispatcherYield();
                window.UpdateLayout();

                // Assert
                Assert.AreEqual(0, filterTreeView.Items.Count, "Post-Remove: TreeView Items collection should be empty.");
            }
            finally
            {
                window?.Close();
            }
        });

        // Assert
        Assert.AreEqual(0, _viewModel.ActiveTreeRootNodes.Count, "Post-Remove: ViewModel's ActiveTreeRootNodes should be empty.");
        Assert.IsNull(_viewModel.SelectedFilterNode, "Post-Remove: SelectedFilterNode should be null.");
        Assert.IsNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Post-Remove: Profile's RootFilterViewModel should be null.");
    }
}
#endif
