using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Logonaut.UI.Controls;

/*
 * Implements a visual component displaying an animated spinner to indicate background activity.
 *
 * Purpose:
 * Provides user feedback that the application is performing a potentially long-running
 * operation, preventing the perception of the UI being frozen. It activates based on
 * the state of a bound collection.
 *
 * Role:
 * Acts as a self-contained UI element within the View layer. It is designed to be bound
 * to a ViewModel collection (`ActiveStates`). It internally manages its animation state
 * (Idle, Spinning, FadingOut) and controls its own Visibility and Opacity properties
 * to handle smooth appearance and disappearance, including a fade-out effect.
 *
 * Responsibilities:
 * - Monitors the bound `ActiveStates` collection.
 * - Displays a themeable rotating arc animation when `ActiveStates` is not empty.
 * - Manages transitions between internal states (Idle, Spinning, FadingOut).
 * - Controls its own `Visibility` and `Opacity` for display and fade-out.
 * - Uses `CompositionTarget.Rendering` for the spinning animation loop.
 *
 * Benefits:
 * - Provides clear visual indication of application busyness.
 * - Encapsulates animation and state logic within the control.
 * - Decouples UI feedback from the specific background tasks being performed.
 * - Themeable appearance via `SpinnerBrush` and `SpinnerThickness` properties.
 *
 * Implementation Notes:
 * Animation updates are driven by `CompositionTarget.Rendering`. State transitions
 * are triggered by changes to the `ActiveStates` collection or the control's `IsVisible` property.
 * It manages its own `Visibility` and `Opacity` internally, overriding external bindings.
 */
public class BusyIndicator : FrameworkElement
{
    // Inside BusyIndicator.cs (or a relevant namespace)
    private enum AnimationState
    {
        Idle,       // Not visible or spinning
        Spinning,   // Visible, spinning, full opacity
        FadingOut   // Visible, not spinning, opacity decreasing
    }

    // Inside BusyIndicator class
    private AnimationState _currentState = AnimationState.Idle;

    public bool DebugIsIdle => _currentState == AnimationState.Idle;
    public bool DebugIsSpinning => _currentState == AnimationState.Spinning;
    public bool DebugIsFadingOut => _currentState == AnimationState.FadingOut;

    private readonly Storyboard _fadeOutStoryboard;
    private const double FadeOutDurationSeconds = 0.5;

    private const double DegreesPerSecond = 360.0; // Speed of one full rotation
    private double _currentAngle = 0.0;
    private TimeSpan _lastRenderTime = TimeSpan.Zero;
    private bool _isSubscribedToRendering = false;

    #region Dependency Properties

    public static readonly DependencyProperty ActiveStatesProperty =
        DependencyProperty.Register(
            nameof(ActiveStates),
            typeof(ObservableCollection<object>),
            typeof(BusyIndicator),
            // Default value is null. Binding will provide the actual collection.
            // Callback is essential to hook/unhook collection changed handler.
            new FrameworkPropertyMetadata(null, OnActiveStatesChanged));

    // --- Standard get/set wrapper ---
    public ObservableCollection<object> ActiveStates
    {
        get => (ObservableCollection<object>)GetValue(ActiveStatesProperty);
        set => SetValue(ActiveStatesProperty, value); // Standard setter
    }

    // SpinnerBrush: Defines the color of the spinner arc. Allows theme overrides.
    public static readonly DependencyProperty SpinnerBrushProperty =
        DependencyProperty.Register(nameof(SpinnerBrush), typeof(Brush), typeof(BusyIndicator),
            new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush SpinnerBrush
    {
        get => (Brush)GetValue(SpinnerBrushProperty);
        set => SetValue(SpinnerBrushProperty, value);
    }

    // SpinnerThickness: Defines the stroke thickness of the arc. Allows theme overrides.
    public static readonly DependencyProperty SpinnerThicknessProperty =
        DependencyProperty.Register(nameof(SpinnerThickness), typeof(double), typeof(BusyIndicator),
            new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double SpinnerThickness
    {
        get => (double)GetValue(SpinnerThicknessProperty);
        set => SetValue(SpinnerThicknessProperty, value);
    }

    // SpeedRatio: Modifies the default rotation speed.
    public static readonly DependencyProperty SpeedRatioProperty =
        DependencyProperty.Register(nameof(SpeedRatio), typeof(double), typeof(BusyIndicator),
            new FrameworkPropertyMetadata(1.0));

    public double SpeedRatio
    {
        get => (double)GetValue(SpeedRatioProperty);
        set => SetValue(SpeedRatioProperty, value);
    }

    #endregion

    // Static constructor for overriding default metadata if needed
    static BusyIndicator()
    {
        // Ensure the control is not focusable by default
        FocusableProperty.OverrideMetadata(typeof(BusyIndicator), new FrameworkPropertyMetadata(false));
    }

    // Instance constructor
    public BusyIndicator()
    {
        Debug.WriteLine($"[BI {this.GetHashCode():X}] Constructor Start. Initial State: {_currentState}");
        IsVisibleChanged += BusyIndicator_IsVisibleChanged;

        // --- Create Fade Out Animation ---
        var fadeOutAnimation = new DoubleAnimation
        {
            To = 0.0, // Fade to fully transparent
            Duration = TimeSpan.FromSeconds(FadeOutDurationSeconds),
            FillBehavior = FillBehavior.Stop // Stop holding the value after completion
        };
        Storyboard.SetTarget(fadeOutAnimation, this);
        Storyboard.SetTargetProperty(fadeOutAnimation, new PropertyPath(FrameworkElement.OpacityProperty));

        _fadeOutStoryboard = new Storyboard();
        _fadeOutStoryboard.Children.Add(fadeOutAnimation);

        // --- Handle Animation Completion ---
        _fadeOutStoryboard.Completed += FadeOut_Completed;

        // --- Explicitly set initial visual state to match Idle ---
        Visibility = Visibility.Collapsed;
        Opacity = 0.0;
        Debug.WriteLine($"[BI {this.GetHashCode():X}] Constructor End. Opacity={Opacity}, Visibility={Visibility}");
    }

    // Storyboard completed handler
    private void FadeOut_Completed(object? sender, EventArgs e)
    {
        Debug.WriteLine($"[BI {this.GetHashCode():X}] FadeOut_Completed FIRED. CurrentState={_currentState}");
        // Only transition to Idle if we are still logically FadingOut.
        if (_currentState == AnimationState.FadingOut)
        {
            Debug.WriteLine($"[BI {this.GetHashCode():X}] FadeOut_Completed - Transitioning to Idle.");
            TransitionToState(AnimationState.Idle);
        }
        else
        {
            Debug.WriteLine($"[BI {this.GetHashCode():X}] FadeOut_Completed - Ignored (State is {_currentState}).");
        }
    }

    // Central method to check state and update animation/visibility
    private void UpdateAnimationState()
    {
        bool isActive = ActiveStates?.Any() ?? false;
        // Determine target state SOLELY based on activity
        AnimationState targetState = isActive ? AnimationState.Spinning : AnimationState.Idle;

        Debug.WriteLine($"[BI {this.GetHashCode():X}] UpdateAnimationState - isActive={isActive}, CurrentState={_currentState}, TargetStateBasedOnActivity={targetState}");

        // Calculate the next state based on current and target
        AnimationState nextState = _currentState;
        switch (_currentState)
        {
            case AnimationState.Idle:
                if (targetState == AnimationState.Spinning) nextState = AnimationState.Spinning;
                break;
            case AnimationState.Spinning:
                if (targetState == AnimationState.Idle) nextState = AnimationState.FadingOut; // Start fade when becoming inactive
                break;
            case AnimationState.FadingOut:
                if (targetState == AnimationState.Spinning) nextState = AnimationState.Spinning; // Interrupt fade
                // Stay FadingOut if target is Idle (let Completed handler finish)
                break;
        }

        // Trigger the transition if needed
        if (nextState != _currentState)
        {
            Debug.WriteLine($"[BI {this.GetHashCode():X}] UpdateAnimationState - Requesting transition from {_currentState} to {nextState}");
            TransitionToState(nextState);
        }
        else
        {
            Debug.WriteLine($"[BI {this.GetHashCode():X}] UpdateAnimationState - No state change needed from {_currentState}.");
        }
    }

    // --- State Transition Logic ---
    private void TransitionToState(AnimationState newState)
    {
        if (_currentState == newState) return; // No change

        var previousState = _currentState;
        Debug.WriteLine($"[BI {this.GetHashCode():X}] TransitionToState START - From: {previousState}, To: {newState}");
        _currentState = newState; // Set the new state immediately

        // Stop animations/subscriptions of the PREVIOUS state
        if (previousState == AnimationState.Spinning) StopSpinningAnimation();
        else if (previousState == AnimationState.FadingOut) StopFadeOutAnimation();

        // Configure the NEW state
        switch (newState)
        {
            case AnimationState.Idle:
                Debug.WriteLine($"[BI {this.GetHashCode():X}] TransitionToState(Idle) - Setting Opacity=0.0, Visibility=Collapsed. Stopping animations.");
                StopSpinningAnimation();
                StopFadeOutAnimation();
                Opacity = 0.0;
                Visibility = Visibility.Collapsed; // <<< SET VISIBILITY TO COLLAPSED
                break;

            case AnimationState.Spinning:
                Debug.WriteLine($"[BI {this.GetHashCode():X}] TransitionToState(Spinning) - Setting Opacity=1.0, Visibility=Visible and starting spin.");
                StopFadeOutAnimation(); // Stop fade if interrupting
                Opacity = 1.0;
                Visibility = Visibility.Visible;   // <<< SET VISIBILITY TO VISIBLE
                StartSpinningAnimation(); // Start the render loop
                break;

            case AnimationState.FadingOut:
                Debug.WriteLine($"[BI {this.GetHashCode():X}] TransitionToState(FadingOut) - Setting Visibility=Visible. Starting fade storyboard.");
                StopSpinningAnimation(); // Ensure render loop is stopped first
                Visibility = Visibility.Visible;   // <<< SET VISIBILITY TO VISIBLE (Must be visible for fade)
                // Opacity should be 1.0 from Spinning state. StartFade will ensure it.
                StartFadeOutAnimation(); // Start the storyboard
                break;
        }
        Debug.WriteLine($"[BI {this.GetHashCode():X}] TransitionToState END - Current State: {_currentState}. Opacity={Opacity}, Visibility={Visibility}");
    }

    // --- Helper methods to manage animations ---
    private void StartSpinningAnimation()
    {
        if (!_isSubscribedToRendering)
        {
            Debug.WriteLine($"[BI {this.GetHashCode():X}] StartSpinningAnimation - Subscribing to CompositionTarget.Rendering.");
            _lastRenderTime = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
            _isSubscribedToRendering = true;
        } else {
            Debug.WriteLine($"[BI {this.GetHashCode():X}] StartSpinningAnimation - Already subscribed.");
        }
    }

    private void StopSpinningAnimation()
    {
        if (_isSubscribedToRendering)
        {
            Debug.WriteLine($"[BI {this.GetHashCode():X}] StopSpinningAnimation - Unsubscribing from CompositionTarget.Rendering.");
            CompositionTarget.Rendering -= OnRendering;
            _isSubscribedToRendering = false;
        } else {
            Debug.WriteLine($"[BI {this.GetHashCode():X}] StopSpinningAnimation - Already unsubscribed.");
        }
    }

    private void StartFadeOutAnimation()
    {
        // Ensure Opacity starts at 1.0 for the fade, regardless of current actual value
        // This helps if the state transition was interrupted.
        Opacity = 1.0;
        Debug.WriteLine($"[BI {this.GetHashCode():X}] StartFadeOutAnimation - Set Opacity={Opacity}. Beginning storyboard...");
        _fadeOutStoryboard.Begin(this, true); // Use HandoffBehavior.SnapshotAndReplace
    }

    private void StopFadeOutAnimation()
    {
        // Check if the OpacityProperty has an animation clock associated with it.
        if (this.HasAnimatedProperties)
        {
            var clock = this.ReadLocalValue(OpacityProperty) as AnimationClock;
            if (clock != null && clock.Controller != null) // Check clock and controller exist
            {
                Debug.WriteLine($"[BI {this.GetHashCode():X}] StopFadeOutAnimation - Found active clock. Stopping storyboard.");
                _fadeOutStoryboard.Stop(this);
                 // It's often useful to immediately set the property to the animation's
                 // target value when stopping prematurely, especially if FillBehavior=Stop.
                 // Opacity = 0.0; // Explicitly set to final value on stop? Or let TransitionToState handle it? Let's let Transition handle it.
            } else {
                 Debug.WriteLine($"[BI {this.GetHashCode():X}] StopFadeOutAnimation - Opacity has clock but no controller? Clock State: {clock?.CurrentState}");
            }
        } else {
            Debug.WriteLine($"[BI {this.GetHashCode():X}] StopFadeOutAnimation - No animated properties found.");
        }
    }

    // Callback when the ActiveStates DP *itself* changes (i.e., binding assigns a new collection)
    private static void OnActiveStatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var indicator = (BusyIndicator)d;
        Debug.WriteLine($"[BI {indicator.GetHashCode():X}] OnActiveStatesChanged - NewValue type: {e.NewValue?.GetType().Name ?? "null"}");
        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= indicator.ActiveStates_CollectionChanged;
             Debug.WriteLine($"[BI {indicator.GetHashCode():X}] OnActiveStatesChanged - Unsubscribed from old collection.");
        }
        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += indicator.ActiveStates_CollectionChanged;
             Debug.WriteLine($"[BI {indicator.GetHashCode():X}] OnActiveStatesChanged - Subscribed to new collection.");
        }
        indicator.UpdateAnimationState();
    }

    // Handler for collection content changes (in the bound ViewModel's collection)
    private void ActiveStates_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Debug.WriteLine($"[BI {this.GetHashCode():X}] ActiveStates_CollectionChanged FIRED. Action={e.Action}");
        UpdateAnimationState();
    }

    private void BusyIndicator_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        Debug.WriteLine($"[BI {this.GetHashCode():X}] IsVisibleChanged FIRED. NewValue={e.NewValue}, CurrentState={_currentState}");
        bool isNowVisible = (bool)e.NewValue == true;

        if (!isNowVisible && _currentState != AnimationState.Idle)
        {
            // If hidden externally, force the Idle state immediately.
            Debug.WriteLine($"[BI {this.GetHashCode():X}] IsVisibleChanged - Became Hidden. Forcing transition to Idle from {_currentState}.");
            TransitionToState(AnimationState.Idle);
        }
        else if (isNowVisible && _currentState == AnimationState.Spinning)
        {
             // *** If we became visible AND we are supposed to be spinning, ensure rendering starts ***
             Debug.WriteLine($"[BI {this.GetHashCode():X}] IsVisibleChanged - Became Visible while logically Spinning. Ensuring render loop is active.");
             StartSpinningAnimation(); // Make sure rendering is subscribed
             this.InvalidateVisual(); // Force a redraw check
        }
         else if (isNowVisible && _currentState == AnimationState.FadingOut)
        {
             // *** If we became visible AND we are supposed to be fading, ensure storyboard starts ***
              Debug.WriteLine($"[BI {this.GetHashCode():X}] IsVisibleChanged - Became Visible while logically FadingOut. Ensuring fade storyboard is active.");
              StartFadeOutAnimation(); // Make sure fade runs
        }
         // No action needed if became visible and state is Idle (UpdateAnimationState handles this if ActiveStates changes later)
    }

    // Event handler for CompositionTarget.Rendering. Updates animation state and triggers redraw.
    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_isSubscribedToRendering)
        {
            Debug.WriteLine($"[BI {this.GetHashCode():X}] OnRendering - EXITING (Not subscribed).");
             // Defensive unsubscribe
            CompositionTarget.Rendering -= OnRendering;
            return;
        }
        // Debug.WriteLineIf(_isSubscribedToRendering, $"[BI {this.GetHashCode():X}] OnRendering TICK."); // Can be very noisy

        var args = e as RenderingEventArgs ?? throw new InvalidOperationException("Invalid event args type for CompositionTarget.Rendering.");
        var renderingTime = args.RenderingTime;
        if (_lastRenderTime == TimeSpan.Zero) { _lastRenderTime = renderingTime; return; }

        TimeSpan elapsed = renderingTime - _lastRenderTime;
        if (elapsed.TotalSeconds > 1.0) { elapsed = TimeSpan.FromSeconds(1.0 / 60.0); }

        double deltaAngle = elapsed.TotalSeconds * DegreesPerSecond * SpeedRatio;
        _currentAngle = (_currentAngle + deltaAngle) % 360;
        _lastRenderTime = renderingTime;

        InvalidateVisual(); // Schedule OnRender
    }

    // Draws the spinner arc based on the current angle and properties.
    protected override void OnRender(DrawingContext dc)
    {
        if (!IsVisible || _currentState != AnimationState.Spinning)
        {
            // Debug.WriteLine($"[BI {this.GetHashCode():X}] OnRender - SKIPPING (State: {_currentState})");
            return; // Only render when spinning
        }
        // Debug.WriteLine($"[BI {this.GetHashCode():X}] OnRender - DRAWING (State: {_currentState})");

        // --- Drawing logic remains the same ---
        double width = ActualWidth;
        double height = ActualHeight;
        if (width <= 0 || height <= 0 || SpinnerThickness <= 0) return;
        Point center = new Point(width / 2.0, height / 2.0);
        double radius = Math.Min(width, height) / 2.0 - (SpinnerThickness / 2.0);
        if (radius <= 0) return;
        Point startPoint = new Point(center.X + radius, center.Y);
        Point endPoint = new Point(center.X, center.Y - radius);
        Size arcSize = new Size(radius, radius);
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open()) { ctx.BeginFigure(startPoint, false, false); ctx.ArcTo(endPoint, arcSize, 0, true, SweepDirection.Clockwise, true, false); }
        geometry.Freeze();
        var pen = new Pen(SpinnerBrush, SpinnerThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        dc.PushTransform(new RotateTransform(angle: _currentAngle, centerX: center.X, centerY: center.Y));
        dc.DrawGeometry(brush: null, pen: pen, geometry: geometry);
        dc.Pop();
    }
}
