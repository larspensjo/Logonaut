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
using Logonaut.TestUtils; // For shared mocks
using System.Threading;
using System;
using System.Linq;
using System.Reactive.Concurrency;
using Microsoft.Reactive.Testing; // Essential for TestScheduler
using System.Reflection; // For accessing internal members for testing if necessary

namespace Logonaut.TestUtils;

/**
 * Base class for setting up common testing infrastructure for ViewModels,
 * including mocks, a TestScheduler for Rx.NET, and an STA thread for UI-related tests.
 */
[TestClass] public abstract class MainViewModelTestBase
{
    // --- Shared Mocks & Context ---
    protected MockSettingsService _mockSettings = null!;
    protected MockLogSourceProvider _mockSourceProvider = null!;
    protected MockLogSource _mockFileLogSource = null!;
    protected MockFileDialogService _mockFileDialog = null!;
    protected MockLogSource _mockSimulatorSource = null!;
    protected SynchronizationContext _testContext = null!;
    protected TestScheduler _backgroundScheduler = null!;

    // --- Specific to MainViewModel testing, can be ignored by other derived classes ---
    protected MainViewModel _viewModel = null!;
    protected TabViewModel _tabViewModel = null!; // To hold the internal TabViewModel instance

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

    [ClassCleanup(Microsoft.VisualStudio.TestTools.UnitTesting.InheritanceBehavior.BeforeEachDerivedClass)]
    public static void ClassCleanup()
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
        _testContext = new ImmediateSynchronizationContext(); // No need for _testContext.Send
        _backgroundScheduler = new TestScheduler();

        _mockSettings.SettingsToReturn = MockSettingsService.CreateDefaultTestSettings();

        _requestScrollToEndEventFired = false;
    }

    protected void SetupMainAndTabViewModel()
    {
        _viewModel = new MainViewModel(
            _mockSettings,
            _mockSourceProvider,
            _mockFileDialog,
            _testContext,
            _backgroundScheduler
        );
        _tabViewModel = GetInternalTabViewModel(_viewModel);
        _tabViewModel.RequestScrollToEnd += ViewModel_RequestScrollToEndHandler;
    }

    // --- Per-Test Teardown ---
    [TestCleanup] public virtual void TestCleanup()
    {
        RunOnSta(() =>
        {
            if (_viewModel != null && _tabViewModel != null) // Check if _tabViewModel was initialized
            {
                _tabViewModel.RequestScrollToEnd -= ViewModel_RequestScrollToEndHandler;
            }
            _viewModel?.Dispose();
        });
    }

    private void ViewModel_RequestScrollToEndHandler(object? sender, EventArgs e)
    {
        _requestScrollToEndEventFired = true;
    }

    #region Helper Methods

    /**
     * Retrieves the internal TabViewModel instance from a MainViewModel using reflection.
     * This is intended for testing purposes to access an internal component.
     * TODO: Isn't this available in `mainVm.ActiveTabViewModel`?
     */
    protected static TabViewModel GetInternalTabViewModel(MainViewModel mainVm)
    {
        var fieldInfo = typeof(MainViewModel).GetField("_activeTabViewModel", BindingFlags.NonPublic | BindingFlags.Instance);
        if (fieldInfo == null)
        {
            throw new InvalidOperationException("_activeTabViewModel field not found in MainViewModel. Test setup might be outdated.");
        }
        var tabVm = fieldInfo.GetValue(mainVm) as TabViewModel;
        if (tabVm == null)
        {
            throw new InvalidOperationException("_activeTabViewModel is null. MainViewModel might not have initialized it correctly.");
        }
        return tabVm;
    }

    /**
     * Gets the currently active mock log source based on the MainViewModel's state.
     * This helps tests interact with the correct mock source (file or simulator).
     */
    protected MockLogSource GetActiveMockSource()
    {
        if (_tabViewModel == null)
        {
            // If _tabViewModel isn't set up (e.g., in tests not focusing on MainViewModel),
            // we might need a different way to determine the active source, or this method shouldn't be called.
            // For now, assume it's called in a context where _tabViewModel is valid.
            throw new InvalidOperationException("_tabViewModel is not initialized. Cannot determine active mock source.");
        }

        if (_tabViewModel.LogSourceExposeDeprecated == _mockSimulatorSource)
        {
            return _mockSimulatorSource;
        }
        if (_tabViewModel.LogSourceExposeDeprecated == _mockFileLogSource)
        {
            return _mockFileLogSource;
        }
        // Fallback or if using NullLogSource etc.
        return _mockFileLogSource; // Default to file source if no specific simulator is active
    }

    /**
     * Finds a visual child of a specific type within a DependencyObject's visual tree.
     * Can optionally filter by name.
     */
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
                        // If name is specified, but current child doesn't match,
                        // still need to search its children.
                        foundChild = FindVisualChild<T>(childType, name);
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

    /**
     * Calculates the expected character offset of a search term within a specific line of the filtered log.
     * Used for verifying search result positions.
     */
    protected int CalculateExpectedOffset(int targetLineIndex, string searchTerm)
    {
        if (_tabViewModel == null)
             throw new InvalidOperationException("_tabViewModel is not initialized. Cannot calculate offset.");

        if (targetLineIndex < 0 || targetLineIndex >= _tabViewModel.FilteredLogLines.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLineIndex), $"Target line index {targetLineIndex} is out of bounds for FilteredLogLines count {_tabViewModel.FilteredLogLines.Count}.");
        }

        int offset = 0;
        for (int i = 0; i < targetLineIndex; i++)
        {
            offset += _tabViewModel.FilteredLogLines[i].Text.Length + Environment.NewLine.Length;
        }
        int indexInLine = _tabViewModel.FilteredLogLines[targetLineIndex].Text.IndexOf(searchTerm, _tabViewModel.IsCaseSensitiveSearch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        return indexInLine == -1 ? -1 : offset + indexInLine;
    }

    /**
     * Executes an action on the STA thread, ensuring UI-related operations are performed safely.
     * Propagates any exceptions from the action.
     */
    protected static void RunOnSta(Action action)
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

    /**
     * Helper method for MainViewModel tests to simulate the filter update trigger.
     * Advances the TestScheduler to allow debounced operations to complete.
     */
    protected void InjectTriggerFilterUpdate()
    {
        if (_tabViewModel == null)
             throw new InvalidOperationException("_tabViewModel is not initialized. Cannot inject filter update.");

        _tabViewModel.ApplyFiltersFromProfile(_viewModel.AvailableProfiles, _viewModel.ContextLines);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
    }

    #endregion // Helper Methods
}
