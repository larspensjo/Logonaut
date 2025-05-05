// tests/Logonaut.UI.Tests/BusyIndicatorTests.cs
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Threading;
using Logonaut.UI.Controls;
using System;
using System.Reflection; // Keep for potential future debugging if needed
using System.Diagnostics; // Required for Debug.WriteLine
using System.Collections.Specialized; // Required for NotifyCollectionChangedAction

namespace Logonaut.UI.Tests.Controls;

[TestClass] public class BusyIndicatorTests
{
    // Helper to run actions on an STA thread.
    private static void RunOnStaThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try { action(); } catch (Exception ex) { exception = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (exception != null)
        {
            throw new AssertFailedException($"Exception occurred on STA thread: {exception.Message}", exception);
        }
    }

    // --- Helper to simulate ActiveStates collection changing ---
    private void SimulateCollectionChange(BusyIndicator indicator, NotifyCollectionChangedAction action)
    {
        MethodInfo? collectionChangedMethod = typeof(BusyIndicator).GetMethod("ActiveStates_CollectionChanged", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(collectionChangedMethod, "Could not find ActiveStates_CollectionChanged method.");

        NotifyCollectionChangedEventArgs args;

        // Use the appropriate constructor based on the action
        switch (action)
        {
            case NotifyCollectionChangedAction.Add:
                // Need to provide a dummy item for Add
                args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, new object());
                break;
            case NotifyCollectionChangedAction.Remove:
                 // Need to provide a dummy item for Remove (index doesn't matter for this test)
                args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, new object(), 0);
                break;
            case NotifyCollectionChangedAction.Reset:
                // This constructor is valid for Reset
                args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
                break;
            case NotifyCollectionChangedAction.Replace:
                 // Need old and new dummy items for Replace
                args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, new object(), new object(), 0);
                 break;
            case NotifyCollectionChangedAction.Move:
                 // Need dummy item and indices for Move
                 args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, new object(), 0, 0);
                 break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action), "Unsupported collection change action for simulation.");
        }

        collectionChangedMethod.Invoke(indicator, new object?[] { null, args });
    }

    // --- Helper to simulate IsVisible property changing ---
    private void SimulateVisibilityChange(BusyIndicator indicator, bool newIsVisibleValue, bool oldIsVisibleValue = false)
    {
         MethodInfo? isVisibleChangedMethod = typeof(BusyIndicator).GetMethod("BusyIndicator_IsVisibleChanged", BindingFlags.NonPublic | BindingFlags.Instance);
         Assert.IsNotNull(isVisibleChangedMethod, "Could not find BusyIndicator_IsVisibleChanged method.");
         var args = new DependencyPropertyChangedEventArgs(UIElement.IsVisibleProperty, oldIsVisibleValue, newIsVisibleValue);
         isVisibleChangedMethod.Invoke(indicator, new object[] { indicator, args });
    }

    // --- Tests ---

    [TestMethod]
    public void Constructor_InitialState_IsIdleAndHidden()
    {
        RunOnStaThread(() =>
        {
            // Arrange & Act
            var indicator = new BusyIndicator();

            // Assert using Debug flags
            Assert.IsTrue(indicator.DebugIsIdle, "Initial state: Idle");
            Assert.IsFalse(indicator.DebugIsSpinning, "Initial state: Not Spinning");
            Assert.IsFalse(indicator.DebugIsFadingOut, "Initial state: Not FadingOut");
            // Assert visual state set by constructor
            Assert.AreEqual(Visibility.Collapsed, indicator.Visibility, "Initial visibility");
            Assert.AreEqual(0.0, indicator.Opacity, "Initial opacity");
        });
    }

    [TestMethod]
    public void Idle_WhenActiveStatesNotEmpty_TransitionsToSpinning()
    {
        RunOnStaThread(() =>
        {
            // Arrange
            var indicator = new BusyIndicator();
            var activeStates = new ObservableCollection<object>();
            indicator.SetValue(BusyIndicator.ActiveStatesProperty, activeStates);
            Assert.IsTrue(indicator.DebugIsIdle, "Precondition: Should start Idle.");

            // Act: Add item and simulate collection change event
            activeStates.Add(new object());
            SimulateCollectionChange(indicator, NotifyCollectionChangedAction.Add);

            // Assert using Debug flags
            Assert.IsFalse(indicator.DebugIsIdle, "State check: Not Idle");
            Assert.IsTrue(indicator.DebugIsSpinning, "State check: Spinning");
            Assert.IsFalse(indicator.DebugIsFadingOut, "State check: Not FadingOut");
            // Assert visual state
            Assert.AreEqual(Visibility.Visible, indicator.Visibility, "Visibility check");
            Assert.AreEqual(1.0, indicator.Opacity, "Opacity check");
        });
    }

    [TestMethod]
    public void Spinning_WhenActiveStatesBecomesEmpty_TransitionsToFadingOut()
    {
        RunOnStaThread(() =>
        {
            // Arrange: Force into Spinning state
            var indicator = new BusyIndicator();
            var activeStates = new ObservableCollection<object>() { new object() };
            indicator.SetValue(BusyIndicator.ActiveStatesProperty, activeStates);
            SimulateCollectionChange(indicator, NotifyCollectionChangedAction.Add); // Force Spinning
            Assert.IsTrue(indicator.DebugIsSpinning, "Precondition: Should be Spinning.");

            // Act: Clear collection and simulate change event
            activeStates.Clear();
            SimulateCollectionChange(indicator, NotifyCollectionChangedAction.Reset);

            // Assert using Debug flags
            Assert.IsFalse(indicator.DebugIsIdle, "State check: Not Idle");
            Assert.IsFalse(indicator.DebugIsSpinning, "State check: Not Spinning");
            Assert.IsTrue(indicator.DebugIsFadingOut, "State check: FadingOut");
            // Assert visual state (should remain visible for fade)
            Assert.AreEqual(Visibility.Visible, indicator.Visibility, "Visibility check");
        });
    }

    [TestMethod]
    public void Spinning_WhenHidden_TransitionsToIdleImmediately()
    {
        RunOnStaThread(() =>
        {
            // Arrange: Force into Spinning state and ensure IsVisible is true
            var indicator = new BusyIndicator();
            var activeStates = new ObservableCollection<object>() { new object() };
            indicator.SetValue(BusyIndicator.ActiveStatesProperty, activeStates);
            SimulateCollectionChange(indicator, NotifyCollectionChangedAction.Add); // Force Spinning
            Assert.IsTrue(indicator.DebugIsSpinning, "Precondition: Should be Spinning.");
            Assert.AreEqual(Visibility.Visible, indicator.Visibility, "Precondition: Visibility should be Visible"); // Ensure starting visible

            // Act: Simulate IsVisible becoming false by invoking the handler
            SimulateVisibilityChange(indicator, newIsVisibleValue: false, oldIsVisibleValue: true);

            // Assert using Debug flags
            Assert.IsTrue(indicator.DebugIsIdle, "State check: Idle");
            Assert.IsFalse(indicator.DebugIsSpinning, "State check: Not Spinning");
            Assert.IsFalse(indicator.DebugIsFadingOut, "State check: Not FadingOut");
            // Assert visual state
            Assert.AreEqual(Visibility.Collapsed, indicator.Visibility, "Visibility check");
            Assert.AreEqual(0.0, indicator.Opacity, "Opacity check");
        });
    }

    [TestMethod]
    public void FadingOut_WhenActiveStatesNotEmpty_TransitionsBackToSpinning()
    {
        RunOnStaThread(() =>
        {
            // Arrange: Force into FadingOut state
            var indicator = new BusyIndicator();
            var activeStates = new ObservableCollection<object>() { new object() };
            indicator.SetValue(BusyIndicator.ActiveStatesProperty, activeStates);
            SimulateCollectionChange(indicator, NotifyCollectionChangedAction.Add); // -> Spinning
            activeStates.Clear();
            SimulateCollectionChange(indicator, NotifyCollectionChangedAction.Reset); // -> FadingOut
            Assert.IsTrue(indicator.DebugIsFadingOut, "Precondition: Should be FadingOut.");

            // Act: Add item back and simulate change event
            activeStates.Add(new object());
            SimulateCollectionChange(indicator, NotifyCollectionChangedAction.Add);

            // Assert using Debug flags
            Assert.IsFalse(indicator.DebugIsIdle, "State check: Not Idle");
            Assert.IsTrue(indicator.DebugIsSpinning, "State check: Spinning");
            Assert.IsFalse(indicator.DebugIsFadingOut, "State check: Not FadingOut");
            // Assert visual state
            Assert.AreEqual(Visibility.Visible, indicator.Visibility, "Visibility check");
            Assert.AreEqual(1.0, indicator.Opacity, "Opacity check");
        });
    }

    [TestMethod]
    public void FadingOut_WhenHidden_TransitionsToIdleImmediately()
    {
        RunOnStaThread(() =>
        {
            // Arrange: Force into FadingOut state and ensure IsVisible is true
            var indicator = new BusyIndicator();
            var activeStates = new ObservableCollection<object>() { new object() };
            indicator.SetValue(BusyIndicator.ActiveStatesProperty, activeStates);
            SimulateCollectionChange(indicator, NotifyCollectionChangedAction.Add); // -> Spinning
            activeStates.Clear();
            SimulateCollectionChange(indicator, NotifyCollectionChangedAction.Reset); // -> FadingOut
            Assert.IsTrue(indicator.DebugIsFadingOut, "Precondition: Should be FadingOut.");
            Assert.AreEqual(Visibility.Visible, indicator.Visibility, "Precondition: Visibility should be Visible");

            // Act: Simulate IsVisible becoming false by invoking the handler
            SimulateVisibilityChange(indicator, newIsVisibleValue: false, oldIsVisibleValue: true);

            // Assert using Debug flags
            Assert.IsTrue(indicator.DebugIsIdle, "State check: Idle");
            Assert.IsFalse(indicator.DebugIsSpinning, "State check: Not Spinning");
            Assert.IsFalse(indicator.DebugIsFadingOut, "State check: Not FadingOut");
            // Assert visual state
            Assert.AreEqual(Visibility.Collapsed, indicator.Visibility, "Visibility check");
            Assert.AreEqual(0.0, indicator.Opacity, "Opacity check");
        });
    }
}
