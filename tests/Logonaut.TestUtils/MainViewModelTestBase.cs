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
        _testContext = new ImmediateSynchronizationContext();
        _backgroundScheduler = new TestScheduler();

        _mockSettings.SettingsToReturn = MockSettingsService.CreateDefaultTestSettings();
    }

    protected void SetupMainViewModel()
    {
        _viewModel = new MainViewModel(
            _mockSettings,
            _mockSourceProvider,
            _mockFileDialog,
            _testContext,
            _backgroundScheduler
        );
        // The concept of a single "_tabViewModel" is removed. Tests should access _viewModel.ActiveTabViewModel.
    }

    // --- Per-Test Teardown ---
    [TestCleanup] public virtual void TestCleanup()
    {
        RunOnSta(() =>
        {
            _viewModel?.Dispose();
        });
    }

    #region Helper Methods

    /**
     * Gets the currently active mock log source based on the MainViewModel's state.
     * This helps tests interact with the correct mock source (file or simulator).
     */
    protected MockLogSource GetActiveMockSource()
    {
        var activeTab = _viewModel.ActiveTabViewModel;
        if (activeTab == null)
        {
            throw new InvalidOperationException("ActiveTabViewModel is null. Cannot determine active mock source.");
        }

        if (activeTab.LogSourceExposeDeprecated == _mockSimulatorSource)
        {
            return _mockSimulatorSource;
        }
        if (activeTab.LogSourceExposeDeprecated == _mockFileLogSource)
        {
            return _mockFileLogSource;
        }
        
        // This case can happen if the active tab is a 'Pasted' or 'Welcome' tab with a NullLogSource.
        // The calling test needs to handle this. For many tests, we can fall back to the default file source.
        return _mockFileLogSource;
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
        var activeTab = _viewModel.ActiveTabViewModel;
        if (activeTab == null)
             throw new InvalidOperationException("ActiveTabViewModel is null. Cannot calculate offset.");

        if (targetLineIndex < 0 || targetLineIndex >= activeTab.FilteredLogLines.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(targetLineIndex), $"Target line index {targetLineIndex} is out of bounds for FilteredLogLines count {activeTab.FilteredLogLines.Count}.");
        }

        int offset = 0;
        for (int i = 0; i < targetLineIndex; i++)
        {
            offset += activeTab.FilteredLogLines[i].Text.Length + Environment.NewLine.Length;
        }
        int indexInLine = activeTab.FilteredLogLines[targetLineIndex].Text.IndexOf(searchTerm, activeTab.IsCaseSensitiveSearch ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

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
        var activeTab = _viewModel.ActiveTabViewModel;
        if (activeTab == null)
             throw new InvalidOperationException("ActiveTabViewModel is null. Cannot inject filter update.");

        activeTab.ApplyFiltersFromProfile(_viewModel.AvailableProfiles, _viewModel.ContextLines);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(350).Ticks);
    }

    #endregion // Helper Methods
}
