using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading; // Not strictly needed if using CompositionTarget

namespace Logonaut.UI.Controls
{
    /// <summary>
    /// A lightweight FrameworkElement that draws a themeable, rotating arc spinner animation
    /// when its IsSpinning property is true, utilizing CompositionTarget.Rendering for updates.
    /// </summary>
    public class AnimatedSpinner : FrameworkElement
    {
        private const double DegreesPerSecond = 360.0; // Speed of one full rotation
        private double _currentAngle = 0.0;
        private TimeSpan _lastRenderTime = TimeSpan.Zero;
        private bool _isSubscribedToRendering = false;

        #region Dependency Properties

        // IsSpinning: Controls whether the animation is active.
        public static readonly DependencyProperty IsSpinningProperty =
            DependencyProperty.Register(nameof(IsSpinning), typeof(bool), typeof(AnimatedSpinner),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnIsSpinningChanged));

        public bool IsSpinning
        {
            get => (bool)GetValue(IsSpinningProperty);
            set => SetValue(IsSpinningProperty, value);
        }

        // SpinnerBrush: Defines the color of the spinner arc. Allows theme overrides.
        public static readonly DependencyProperty SpinnerBrushProperty =
            DependencyProperty.Register(nameof(SpinnerBrush), typeof(Brush), typeof(AnimatedSpinner),
                new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender)); // Default fallback

        public Brush SpinnerBrush
        {
            get => (Brush)GetValue(SpinnerBrushProperty);
            set => SetValue(SpinnerBrushProperty, value);
        }

        // SpinnerThickness: Defines the stroke thickness of the arc. Allows theme overrides.
        public static readonly DependencyProperty SpinnerThicknessProperty =
            DependencyProperty.Register(nameof(SpinnerThickness), typeof(double), typeof(AnimatedSpinner),
                new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender)); // Default thickness

        public double SpinnerThickness
        {
            get => (double)GetValue(SpinnerThicknessProperty);
            set => SetValue(SpinnerThicknessProperty, value);
        }

        // SpeedRatio: Modifies the default rotation speed.
        public static readonly DependencyProperty SpeedRatioProperty =
            DependencyProperty.Register(nameof(SpeedRatio), typeof(double), typeof(AnimatedSpinner),
                new FrameworkPropertyMetadata(1.0)); // Default speed

        public double SpeedRatio
        {
            get => (double)GetValue(SpeedRatioProperty);
            set => SetValue(SpeedRatioProperty, value);
        }

        #endregion

        // Static constructor for overriding default metadata if needed
        static AnimatedSpinner()
        {
            // Ensure the control is not focusable by default
            FocusableProperty.OverrideMetadata(typeof(AnimatedSpinner), new FrameworkPropertyMetadata(false));
        }

        // Instance constructor
        public AnimatedSpinner()
        {
            this.IsVisibleChanged += AnimatedSpinner_IsVisibleChanged;
        }

        // Handles changes to the IsSpinning property to hook/unhook the rendering event.
        private static void OnIsSpinningChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var spinner = (AnimatedSpinner)d;
            spinner.UpdateRenderingSubscription();
        }

        // Event handler for IsVisibleChanged
        private void AnimatedSpinner_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // The logic remains the same: update subscription based on new visibility
            UpdateRenderingSubscription();
        }

        // Centralizes the logic for subscribing or unsubscribing from CompositionTarget.Rendering.
        private void UpdateRenderingSubscription()
        {
            // Subscribe only if spinning, visible, and not already subscribed.
            bool shouldBeSubscribed = IsSpinning && IsVisible;

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
            }
        }

        // Event handler for CompositionTarget.Rendering. Updates animation state and triggers redraw.
        private void OnRendering(object? sender, EventArgs e)
        {
            // Ensure we should still be rendering. Redundant check for safety.
            if (!IsSpinning || !IsVisible)
            {
                // Attempt to unsubscribe if state changed unexpectedly
                if (_isSubscribedToRendering)
                {
                    CompositionTarget.Rendering -= OnRendering;
                    _isSubscribedToRendering = false;
                }
                return;
            }

            // RenderingEventArgs doesn't exist in standard EventArgs, use RenderingTime directly
            var args = e as RenderingEventArgs;
            if (args == null)
                throw new InvalidOperationException("Invalid event args type. Should not happen with CompositionTarget.Rendering.");

            var renderingTime = args.RenderingTime;
             if (_lastRenderTime == TimeSpan.Zero) // Initialize on first frame after subscribe
            {
                // Initialize or handle reset case
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
            if (!IsSpinning) return;

            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0 || SpinnerThickness <= 0) return; // Avoid drawing if size is invalid

            Point center = new Point(width / 2.0, height / 2.0);
            // Radius calculation should account for half the thickness to fit within bounds.
            double radius = Math.Min(width, height) / 2.0 - (SpinnerThickness / 2.0);
            if (radius <= 0) return;

            // Define start and end points for a 270-degree arc
            // Let's start at the top (0 degrees on unit circle is right, 90 is down, 180 left, 270 up)
            // We want to draw from, say, angle 45 degrees to angle 315 degrees (a 270 degree sweep)
            // Or more simply: Start at the right, sweep 270 clockwise to the top.

            // Start Point: Right-middle (0 degrees rotation from X-axis)
            Point startPoint = new Point(center.X + radius, center.Y);
            // End Point: Top-middle (270 degrees clockwise from start point)
            Point endPoint = new Point(center.X, center.Y - radius);
            // Size: Defines the radii of the ellipse (a circle in this case)
            Size arcSize = new Size(radius, radius);

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(
                    startPoint: startPoint,
                    isFilled: false,  // We only want the stroke
                    isClosed: false); // The arc is not a closed shape

                ctx.ArcTo(
                    point: endPoint,
                    size: arcSize,
                    rotationAngle: 0,           // No ellipse rotation needed for a circle
                    isLargeArc: true,           // YES, 270 degrees is the 'large arc' between these points
                    sweepDirection: SweepDirection.Clockwise, // Draw in the clockwise direction
                    isStroked: true,            // This segment should be stroked
                    isSmoothJoin: false);       // Not relevant for a single arc segment figure
            }
            geometry.Freeze(); // Optimize geometry performance.

            var pen = new Pen(SpinnerBrush, SpinnerThickness)
            {
                // Using rounded caps makes the arc look slightly longer than 270, which is often desired
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
