using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows; // For DispatcherPriority, Size, Rect
using System.Windows.Controls; // For TreeView
using Logonaut.UI.ViewModels;
using Logonaut.Filters;
using System.Windows.Threading; // For Dispatcher

namespace Logonaut.UI.Tests.ViewModels;

#if false
// Tests requiring STA thread and actual WPF controls
[TestClass] public class MainViewModel_WpfInteractionTests : MainViewModelTestBase // Inherits STA setup
{
    [TestInitialize] public override void TestInitialize()
    {
        base.TestInitialize();
        base.SetupMainAndTabViewModel();
    }

    // Helper to yield execution to the dispatcher
    private void DispatcherYield(DispatcherPriority priority = DispatcherPriority.Background)
    {
        // Sending an empty action to the dispatcher effectively yields control,
        // allowing pending operations at the specified priority (or higher) to execute.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, priority);
    }

    // Verifies: [ReqFilterRuleTreeStructurev1] (UI Update)
    [TestMethod] public void AddFilter_WhenActiveProfileIsEmpty_ShouldUpdateTreeViewItems_STA()
    {
        TreeView? filterTreeView = null; // Capture for assertion outside Action

        RunOnSta(() => // Use helper method from base class
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow(_viewModel);
                window.Show(); // Show the window to ensure it's fully initialized and in the visual tree

                var windowContentElement = window.Content as UIElement;
                Assert.IsNotNull(windowContentElement, "Window Content is null.");
                filterTreeView = FindVisualChild<TreeView>(windowContentElement, "FilterTreeViewNameForTesting");
                Assert.IsNotNull(filterTreeView, "FilterTreeView not found.");

                Assert.IsNotNull(_viewModel.ActiveFilterProfile, "Pre-condition: Active profile is null.");
                _viewModel.ActiveFilterProfile.SetModelRootFilter(null);
                _viewModel.ActiveTreeRootNodes.Clear();
                Assert.AreEqual(0, filterTreeView.Items.Count, "Pre-condition: TreeView should be empty.");

                _viewModel.AddFilterCommand.Execute("Substring");

                // --- Added Dispatcher Yield ---
                DispatcherYield(); // Allow data binding to update TreeView ItemsSource
                window.UpdateLayout(); // Force layout after yield

                 // Assert TreeView state inside STA *after* yielding
                Assert.AreEqual(1, filterTreeView.Items.Count, "TreeView Items collection should have one item.");
                Assert.IsInstanceOfType(filterTreeView.Items[0], typeof(FilterViewModel), "Item is not FilterViewModel");
                var itemViewModel = (FilterViewModel)filterTreeView.Items[0];
                Assert.IsInstanceOfType(itemViewModel.Filter, typeof(SubstringFilter), "Item Filter model is not SubstringFilter.");
            }
            finally
            {
                window?.Close(); // Ensure window is closed even on failure
            }
        });
        // No assertions needed outside RunOnSta for this test
    }

    // Verifies: [ReqFilterRuleTreeStructurev1] (UI Update)
    [TestMethod] public void RemoveFilter_ShouldUpdateTreeViewItems_STA()
    {
        TreeView? filterTreeView = null; // Capture for assertion

        RunOnSta(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow(_viewModel);
                window.Show(); // Show the window

                var windowContentElement = window.Content as UIElement;
                Assert.IsNotNull(windowContentElement);
                filterTreeView = FindVisualChild<TreeView>(windowContentElement, "FilterTreeViewNameForTesting");
                Assert.IsNotNull(filterTreeView);

                // Arrange: Add a filter first
                Assert.IsNotNull(_viewModel.ActiveFilterProfile);
                _viewModel.ActiveFilterProfile.SetModelRootFilter(null);
                _viewModel.ActiveTreeRootNodes.Clear();
                _viewModel.AddFilterCommand.Execute("Substring");

                // --- Added Dispatcher Yield ---
                DispatcherYield(); // Allow binding after Add
                window.UpdateLayout();
                Assert.AreEqual(1, filterTreeView.Items.Count, "Pre-Remove: TreeView should have 1 item.");
                _viewModel.SelectedFilterNode = _viewModel.ActiveFilterProfile.RootFilterViewModel; // Select node
                Assert.IsNotNull(_viewModel.SelectedFilterNode, "Pre-Remove: Node not selected.");

                // Act
                _viewModel.RemoveFilterNodeCommand.Execute(null);

                // --- Added Dispatcher Yield ---
                DispatcherYield(); // Allow binding after Remove
                window.UpdateLayout();

                // Assert TreeView state inside STA *after* yielding
                Assert.AreEqual(0, filterTreeView.Items.Count, "Post-Remove: TreeView Items collection should be empty.");
            }
            finally
            {
                window?.Close();
            }
        });

        // Assert ViewModel state (can be done outside Invoke)
        Assert.AreEqual(0, _viewModel.ActiveTreeRootNodes.Count, "Post-Remove: ViewModel's ActiveTreeRootNodes should be empty.");
        Assert.IsNull(_viewModel.SelectedFilterNode, "Post-Remove: SelectedFilterNode should be null.");
        Assert.IsNull(_viewModel.ActiveFilterProfile?.RootFilterViewModel, "Post-Remove: Profile's RootFilterViewModel should be null.");
    }
}
#endif
