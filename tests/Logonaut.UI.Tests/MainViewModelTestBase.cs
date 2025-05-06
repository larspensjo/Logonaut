// tests/Logonaut.UI.Tests/MainViewModelTestBase.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.Generic; // Keep for other potential uses if needed
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.UI.ViewModels;
using Logonaut.TestUtils;
using System.Threading;
using System;
using System.Linq;
using System.Reactive.Concurrency;
using Microsoft.Reactive.Testing; // Essential for TestScheduler

namespace Logonaut.UI.Tests.ViewModels;

[TestClass] public abstract class MainViewModelTestBase
{
    // --- Shared Mocks & Context ---
    protected MockSettingsService _mockSettings = null!;
    protected MockLogSourceProvider _mockSourceProvider = null!;
    protected MockLogSource _mockFileLogSource = null!;
    protected MockLogSource _mockSimulatorLogSource = null!;
    protected MockFileDialogService _mockFileDialog = null!;
    protected MockLogSource _mockSimulatorSource = null!;
    protected SynchronizationContext _testContext = null!;
    protected MainViewModel _viewModel = null!;
    protected TestScheduler _backgroundScheduler = null!;

    // --- Test State ---
    protected bool _requestScrollToEndEventFired = false;

    // --- WPF Control Testing Helper Fields ---
    private static Dispatcher? _dispatcher;
    private static AutoResetEvent _initEvent = new AutoResetEvent(false);

    // --- STA Thread Setup/Teardown ---
    [ClassInitialize(Microsoft.VisualStudio.TestTools.UnitTesting.InheritanceBehavior.BeforeEachDerivedClass)]
    public static void ClassInitialize(TestContext context)
    {
        // Prevent re-initialization if already done by another derived class test run
        if (_dispatcher != null) return;

        var staThread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _initEvent.Set(); // Signal that the dispatcher is ready
            Dispatcher.Run(); // Start the message pump
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = true;
        staThread.Start();
        _initEvent.WaitOne(); // Wait for dispatcher to be ready
    }

    [ClassCleanup] public static void ClassCleanup()
    {
        _dispatcher?.InvokeShutdown();
        _dispatcher = null;
        _initEvent?.Dispose();
    }

    // --- Per-Test Setup ---
    [TestInitialize] public virtual void TestInitialize()
    {
        // Instantiate mocks
        _mockSettings = new MockSettingsService();
        _mockSourceProvider = new MockLogSourceProvider();
        _mockFileLogSource = _mockSourceProvider.MockFileSource;
        _mockSimulatorSource = _mockSourceProvider.MockSimulatorSource;
        _mockFileDialog = new MockFileDialogService();
        _testContext = new ImmediateSynchronizationContext(); // Use the mock context for background tasks
        _backgroundScheduler = new TestScheduler(); // Use Rx.NET Immediate scheduler for background tasks. For more advanced scenarios, consider using TestScheduler.

        _mockSettings.SettingsToReturn = MockSettingsService.CreateDefaultTestSettings();

        // Instantiate ViewModel using the provider
        _viewModel = new MainViewModel(
            _mockSettings,
            _mockSourceProvider,
            _mockFileDialog,
            _testContext,
            _backgroundScheduler
        );

        // Reset test state variables
        _requestScrollToEndEventFired = false;

        // Subscribe to other relevant VM events if needed
        _viewModel.RequestScrollToEnd += ViewModel_RequestScrollToEndHandler;
    }

    // --- Per-Test Teardown ---
    [TestCleanup]
    public virtual void TestCleanup()
    {
        RunOnSta(() => {
            if (_viewModel != null)
            {
                _viewModel.RequestScrollToEnd -= ViewModel_RequestScrollToEndHandler;
            }
             _viewModel?.Dispose();
            _mockFileLogSource?.Dispose();
            _mockSimulatorSource?.Dispose();
        });
    }

    // Define the handler method to match the event signature for proper removal
    private void ViewModel_RequestScrollToEndHandler(object? sender, EventArgs e)
    {
        _requestScrollToEndEventFired = true;
    }

    #region Helper Methods

    /// <summary>
    /// Helper to get the currently active mock source based on VM state.
    /// </summary>
    protected MockLogSource GetActiveMockSource()
    {
        // Use the IsSimulatorRunning property to determine which mock the VM is likely using
        return _viewModel.IsSimulatorRunning ? _mockSimulatorSource : _mockFileLogSource;
    }

    // Static helper, doesn't rely on instance state
    protected static T? FindVisualChild<T>(DependencyObject parent, string name = "") where T : FrameworkElement
    {
         T? foundChild = null;
         int childrenCount = VisualTreeHelper.GetChildrenCount(parent);

         for (int i = 0; i < childrenCount; i++)
         {
             var child = VisualTreeHelper.GetChild(parent, i);

             if (child is not T childType)
             {
                 foundChild = FindVisualChild<T>(child, name);
                 if (foundChild != null) break;
             }
             else
             {
                 if (!string.IsNullOrEmpty(name))
                 {
                     if (childType.Name == name)
                     {
                         foundChild = childType;
                         break;
                     }
                     else
                     {
                          foundChild = FindVisualChild<T>(child, name);
                          if (foundChild != null) break;
                     }
                 }
                 else
                 {
                     foundChild = childType;
                     break;
                 }
             }
         }
         return foundChild;
    }

    // Instance helper, relies on _viewModel state
    protected int CalculateExpectedOffset(int targetLineIndex, string searchTerm)
    {
        // Ensure index is valid before accessing
        if (targetLineIndex < 0 || targetLineIndex >= _viewModel.FilteredLogLines.Count)
        {
             throw new ArgumentOutOfRangeException(nameof(targetLineIndex), $"Target line index {targetLineIndex} is out of bounds for FilteredLogLines count {_viewModel.FilteredLogLines.Count}.");
        }

        int offset = 0;
        for (int i = 0; i < targetLineIndex; i++)
        {
            offset += _viewModel.FilteredLogLines[i].Text.Length + Environment.NewLine.Length;
        }
        // Find the term within the target line
        int indexInLine = _viewModel.FilteredLogLines[targetLineIndex].Text.IndexOf(searchTerm, _viewModel.IsCaseSensitiveSearch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        if (indexInLine == -1)
        {
             // Term not found in the specified line, handle appropriately.
             // Maybe return -1 or throw? Returning -1 is safer for tests.
             return -1;
        }

        return offset + indexInLine;
    }

    /// <summary>
    /// Runs an action on the STA thread managed by this base class.
    /// Useful for tests involving WPF UI elements or operations requiring STA.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    protected void RunOnSta(Action action)
    {
        Exception? threadException = null;
        _dispatcher?.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });
        if (threadException != null) throw threadException; // Re-throw exception from STA thread
    }

    protected void InjectTriggerFilterUpdate()
    {
        // --- Trigger update indirectly ---
        var oldContext = _viewModel.ContextLines;
        // This is a hack. Changing number of conext lines will call OnContextLinesChanged -> TriggerFilterUpdate. Maybe twie, but that is fine
        _viewModel.ContextLines = oldContext+1;
        _viewModel.ContextLines = oldContext;
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow debounced filter to run
        _testContext.Send(_ => { }, null); // Process the filter result update posted back. This is probably not really needed for a mock context.
    }

    #endregion // Helper Methods
}
