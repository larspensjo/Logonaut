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
using System.Reflection; // For accessing internal members for testing if necessary

namespace Logonaut.UI.Tests.ViewModels;

[TestClass] public abstract class MainViewModelTestBase
{
    protected TabViewModel _tabViewModel = null!; // To hold the internal TabViewModel instance

    // --- Shared Mocks & Context ---
    protected MockSettingsService _mockSettings = null!;
    protected MockLogSourceProvider _mockSourceProvider = null!;
    protected MockLogSource _mockFileLogSource = null!;
    protected MockFileDialogService _mockFileDialog = null!;
    protected MockLogSource _mockSimulatorSource = null!; // Use the correct mock type
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
        _mockSourceProvider = new MockLogSourceProvider(); // This creates MockFileLogSource and MockSimulatorLogSource
        _mockFileLogSource = _mockSourceProvider.MockFileSource;
        _mockSimulatorSource = _mockSourceProvider.MockSimulatorSource; // Get the simulator mock
        _mockFileDialog = new MockFileDialogService();
        // The use of our ImmediateSynchronizationContext means that there is no need for _testContext.Send(_ => { }, null);
        _testContext = new ImmediateSynchronizationContext();
        _backgroundScheduler = new TestScheduler();

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

        _tabViewModel = GetInternalTabViewModel(_viewModel); // Get the TabViewModel instance
        _tabViewModel.RequestScrollToEnd += ViewModel_RequestScrollToEndHandler;
    }

    // --- Per-Test Teardown ---
    [TestCleanup] public virtual void TestCleanup()
    {
        RunOnSta(() =>
        {
            if (_viewModel != null)
            {
                _tabViewModel.RequestScrollToEnd -= ViewModel_RequestScrollToEndHandler;
            }
            _viewModel?.Dispose(); // This will dispose the internal TabViewModel
            // Mocks are managed by _mockSourceProvider; their disposal is handled if necessary
            // _mockFileLogSource?.Dispose(); // Disposed by TabViewModel -> LogSource.Dispose()
            // _mockSimulatorSource?.Dispose(); // Disposed by TabViewModel -> LogSource.Dispose()
        });
    }

    private void ViewModel_RequestScrollToEndHandler(object? sender, EventArgs e)
    {
        _requestScrollToEndEventFired = true;
    }

    #region Helper Methods

    // Helper to get the _internalTabViewModel instance from MainViewModel using reflection
    // This is for testing purposes. In a real scenario, you'd test through public APIs.
    protected TabViewModel GetInternalTabViewModel(MainViewModel mainVm)
    {
        var fieldInfo = typeof(MainViewModel).GetField("_internalTabViewModel", BindingFlags.NonPublic | BindingFlags.Instance);
        if (fieldInfo == null)
        {
            throw new InvalidOperationException("_internalTabViewModel field not found in MainViewModel. Test setup might be outdated.");
        }
        var tabVm = fieldInfo.GetValue(mainVm) as TabViewModel;
        if (tabVm == null)
        {
            throw new InvalidOperationException("_internalTabViewModel is null. MainViewModel might not have initialized it correctly.");
        }
        return tabVm;
    }


    protected MockLogSource GetActiveMockSource()
    {
        // The LogSource on TabViewModel is the actual ILogSource instance.
        // We expect this to be one of our mocks.
        if (_tabViewModel.LogSourceExposeDeprecated == _mockSimulatorSource)
        {
            return _mockSimulatorSource;
        }
        if (_tabViewModel.LogSourceExposeDeprecated == _mockFileLogSource)
        {
            return _mockFileLogSource;
        }
        // If it's null or some other type, it means the tab's source isn't one of the primary mocks.
        // This could happen if the tab is for pasted content (NullLogSource) or hasn't been activated properly.
        // For tests specifically targeting file/simulator, ensure the tab is configured for that.
        // If the current LogSource is null, it implies the tab might not be fully active or is misconfigured.
        // For many tests, we might want to default to _mockFileLogSource if the simulator isn't explicitly running.
        // However, _viewModel.IsSimulatorRunning directly queries the tab's source type.
        return _viewModel.IsSimulatorRunning ? _mockSimulatorSource : _mockFileLogSource;
    }


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

    protected int CalculateExpectedOffset(int targetLineIndex, string searchTerm)
    {
        // Ensure index is valid before accessing
        if (targetLineIndex < 0 || targetLineIndex >= _tabViewModel.FilteredLogLines.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLineIndex), $"Target line index {targetLineIndex} is out of bounds for FilteredLogLines count {_tabViewModel.FilteredLogLines.Count}.");
        }

        int offset = 0;
        for (int i = 0; i < targetLineIndex; i++)
        {
            offset += _tabViewModel.FilteredLogLines[i].Text.Length + Environment.NewLine.Length;
        }
        // Find the term within the target line
        int indexInLine = _tabViewModel.FilteredLogLines[targetLineIndex].Text.IndexOf(searchTerm, _tabViewModel.IsCaseSensitiveSearch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        if (indexInLine == -1)
        {
            return -1;
        }

        return offset + indexInLine;
    }

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
        if (threadException != null) throw threadException;
    }

    protected void InjectTriggerFilterUpdate()
    {
        // Access _internalTabViewModel to directly call its filter update logic
        _tabViewModel.ApplyFiltersFromProfile(_viewModel.AvailableProfiles, _viewModel.ContextLines);

        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks); // Allow debounced filter to run
    }

    #endregion // Helper Methods
}
