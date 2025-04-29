using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows; // For DispatcherPriority, Size, Rect
using System.Windows.Controls; // For TreeView
using System.Windows.Media; // For VisualTreeHelper
using System.Windows.Threading; // For Dispatcher
using System.Collections.Generic; // For List
using Logonaut.Common;
using Logonaut.Core;
using Logonaut.Filters;
using Logonaut.UI.ViewModels;
using Logonaut.TestUtils;
using System.Threading; // For SynchronizationContext, AutoResetEvent
using System; // For Environment, Exception
using System.Linq; // For LINQ methods

namespace Logonaut.UI.Tests.ViewModels;

/// <summary>
/// Base class for MainViewModel tests, providing common setup, teardown, mocks,
/// helper methods, and STA thread management for WPF control interaction tests.
/// </summary>
[TestClass] // Still needs TestClass attribute for ClassInitialize/Cleanup
public abstract class MainViewModelTestBase
{
    // --- Shared Mocks & Context ---
    protected MockSettingsService _mockSettings = null!;
    protected MockLogSource _mockLogSource = null!;
    protected MockFileDialogService _mockFileDialog = null!;
    protected MockLogFilterProcessor _mockProcessor = null!;
    protected SynchronizationContext _testContext = null!;
    protected MainViewModel _viewModel = null!;

    // --- WPF Control Testing Helper Fields ---
    private static Dispatcher? _dispatcher; // For STA thread tests
    private static AutoResetEvent _initEvent = new AutoResetEvent(false); // Synchronization for STA thread
    protected bool _requestScrollToEndEventFired = false;

    // --- STA Thread Setup/Teardown for WPF Control Tests ---
    [ClassInitialize(Microsoft.VisualStudio.TestTools.UnitTesting.InheritanceBehavior.BeforeEachDerivedClass)] // Ensure STA thread is ready before derived classes run
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

    [ClassCleanup] // Runs once after all tests in derived classes inheriting from here
    public static void ClassCleanup()
    {
        _dispatcher?.InvokeShutdown();
        _dispatcher = null; // Allow re-initialization if run again
        _initEvent.Dispose(); // Dispose the event handle
    }

    // --- Per-Test Setup ---
    [TestInitialize] public virtual void TestInitialize() // Make virtual if derived classes need specific additions
    {
        _mockSettings = new MockSettingsService();
        _mockLogSource = new MockLogSource();
        _mockFileDialog = new MockFileDialogService();
        _mockProcessor = new MockLogFilterProcessor();
        _testContext = new ImmediateSynchronizationContext();

        _mockSettings.SettingsToReturn = MockSettingsService.CreateDefaultTestSettings();

        _viewModel = new MainViewModel(
            _mockSettings,
            _mockFileDialog,
            _mockProcessor,
            _mockLogSource,
            _testContext
        );

        _requestScrollToEndEventFired = false;
        _viewModel.RequestScrollToEnd += (s, e) => _requestScrollToEndEventFired = true;
    }

    // --- Per-Test Teardown ---
    [TestCleanup] public virtual void TestCleanup() // Make virtual if derived classes need specific additions
    {
        Action cleanupAction = () =>
        {
            _viewModel?.Dispose(); // Dispose VM first
        };

        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA && _dispatcher != null && !_dispatcher.HasShutdownStarted)
        {
            try
            {
                _dispatcher.Invoke(cleanupAction);
            } catch (Exception ex) {
                 // Log or handle dispatcher invoke exception during cleanup if necessary
                 System.Diagnostics.Debug.WriteLine($"Exception during STA cleanup invoke: {ex.Message}");
            }
        }
        else
        {
            cleanupAction();
        }
    }

    #region Helper Methods

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


    #endregion // Helper Methods
}
