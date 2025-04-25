using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Logonaut.UI.Controls
{
    /// <summary>
    /// A lightweight FrameworkElement that draws a themeable, rotating arc spinner animation
    /// when its ActiveStates collection is not empty, utilizing CompositionTarget.Rendering for updates.
    /// </summary>
    public class BusyIndicator : FrameworkElement
    {
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
            FocusableProperty.OverrideMetadata(typeof(BusyIndicator), new FrameworkPropertyMetadata(false)); // Changed owner type
        }

        // Instance constructor
        public BusyIndicator()
        {
            this.IsVisibleChanged += BusyIndicator_IsVisibleChanged;
        }

        // Callback when the ActiveStates DP *itself* changes (i.e., binding assigns a new collection)
        private static void OnActiveStatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var indicator = (BusyIndicator)d;

            // Unsubscribe from the old collection (if it existed and was INotifyCollectionChanged)
            if (e.OldValue is INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= indicator.ActiveStates_CollectionChanged;

            // Subscribe to the new collection (if it exists and is INotifyCollectionChanged)
            if (e.NewValue is INotifyCollectionChanged newCollection)
                newCollection.CollectionChanged += indicator.ActiveStates_CollectionChanged;

            // Trigger initial state check based on the *newly bound* collection's state
            indicator.UpdateRenderingBasedOnState();
        }

        // Handler for collection content changes (in the bound ViewModel's collection)
        private void ActiveStates_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateRenderingBasedOnState();
        }

        private void BusyIndicator_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Update subscription based on new visibility AND collection state
            UpdateRenderingBasedOnState();
        }

        // Central method to check state and update subscription
        private void UpdateRenderingBasedOnState()
        {
            bool shouldBeSpinning = (ActiveStates?.Any() == true); // Check if collection has items
            UpdateRenderingSubscription(shouldBeSpinning);
        }

        // Centralizes the logic for subscribing or unsubscribing from CompositionTarget.Rendering.
        // Now takes a parameter indicating if spinning should occur.
        private void UpdateRenderingSubscription(bool shouldBeSpinning)
        {
            // Subscribe only if shouldBeSpinning, visible, and not already subscribed.
            bool shouldBeSubscribed = shouldBeSpinning && IsVisible;

            if (shouldBeSubscribed && !_isSubscribedToRendering)
            {
                // Reset time when subscribing to avoid large initial jump
                _lastRenderTime = TimeSpan.Zero;
                CompositionTarget.Rendering += OnRendering;
                _isSubscribedToRendering = true;
            }
            else if (!shouldBeSubscribed && _isSubscribedToRendering)
            {
                // Unsubscribe if not spinning, not visible, or explicitly requested while subscribed.
                CompositionTarget.Rendering -= OnRendering;
                _isSubscribedToRendering = false;
                // Also explicitly invalidate visual when stopping to clear the last frame
                this.InvalidateVisual();
            }
        }

        // Event handler for CompositionTarget.Rendering. Updates animation state and triggers redraw.
        private void OnRendering(object? sender, EventArgs e)
        {
            // Ensure we should still be rendering based on subscription status
            if (!_isSubscribedToRendering)
            {
                 // This might happen if visibility/state changed between frame request and execution
                 // Try to unsubscribe again just in case, though it should already be done.
                 CompositionTarget.Rendering -= OnRendering;
                 return;
            }

            // RenderingEventArgs doesn't exist in standard EventArgs, use RenderingTime directly
            // RenderingEventArgs is the correct type, cast is needed.
            var args = e as RenderingEventArgs ?? throw new InvalidOperationException("Invalid event args type for CompositionTarget.Rendering.");
            var renderingTime = args.RenderingTime;

            if (_lastRenderTime == TimeSpan.Zero) // Initialize on first frame after subscribe
            {
                _lastRenderTime = renderingTime;
                return;
            }

            // Calculate elapsed time for frame-rate independent animation.
            TimeSpan elapsed = renderingTime - _lastRenderTime;
            // Prevent huge jumps if rendering was suspended (e.g., window minimized)
            if (elapsed.TotalSeconds > 1.0) // Cap max elapsed time
            {
                elapsed = TimeSpan.FromSeconds(1.0 / 60.0); // Assume 60fps for catch-up frame
            }

            double deltaAngle = elapsed.TotalSeconds * DegreesPerSecond * SpeedRatio;

            // Update animation state.
            _currentAngle = (_currentAngle + deltaAngle) % 360;
            _lastRenderTime = renderingTime;

            // Schedule the OnRender method to be called.
            this.InvalidateVisual();
        }

        // Draws the spinner arc based on the current angle and properties.
        protected override void OnRender(DrawingContext dc)
        {
            // Only render if we are subscribed (i.e., ActiveStates has items and control is visible)
            if (!_isSubscribedToRendering) return; // <<< MODIFIED Check

            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0 || SpinnerThickness <= 0) return; // Avoid drawing if size is invalid

            Point center = new Point(width / 2.0, height / 2.0);
            // Radius calculation should account for half the thickness to fit within bounds.
            double radius = Math.Min(width, height) / 2.0 - (SpinnerThickness / 2.0);
            if (radius <= 0) return;

            // Define start and end points for a 270-degree arc
            Point startPoint = new Point(center.X + radius, center.Y);
            Point endPoint = new Point(center.X, center.Y - radius);
            Size arcSize = new Size(radius, radius);

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(startPoint, isFilled: false, isClosed: false);
                ctx.ArcTo(endPoint, arcSize, 0, true, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze(); // Optimize geometry performance.

            var pen = new Pen(SpinnerBrush, SpinnerThickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze(); // Optimize pen performance.

            // Apply rotation transformation around the center.
            dc.PushTransform(new RotateTransform(angle: _currentAngle, centerX: center.X, centerY: center.Y));

            // Draw the single rotated arc geometry.
            dc.DrawGeometry(brush: null, pen: pen, geometry: geometry);

            // Restore the drawing context state.
            dc.Pop();
        }
    }
}
