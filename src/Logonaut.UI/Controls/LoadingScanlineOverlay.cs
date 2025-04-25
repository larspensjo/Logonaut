using System;
using System.Windows;
using System.Windows.Media;
using System.Collections.Generic; // Required for List
using System.Windows.Controls; // Required for Control

namespace Logonaut.UI.Controls;

/// <summary>
/// A FrameworkElement that draws soft, animated vertical scanlines.
/// Intended for use as a loading overlay. Animation is driven by CompositionTarget.Rendering
/// and only active when the control is visible.
/// </summary>
public class LoadingScanlineOverlay : FrameworkElement
{
    private TimeSpan _lastRenderTime = TimeSpan.Zero;
    private bool _isSubscribedToRendering = false;
    private double _scanlineOffset = 0.0; // Controls the horizontal position of scanlines
    private const double ScanlineWidth = 80; // Width of each scanline band
    private const double ScanlineSpacing = 150; // Total space per scanline (width + gap)
    private const double PixelsPerSecond = 40.0; // Horizontal speed of scanlines

    // Cache brushes and pens for performance
    private Brush _scanlineBrush;
    private bool _useLightTheme = true; // Default to light, adjust based on actual theme

    public LoadingScanlineOverlay()
    {
        this.IsVisibleChanged += LoadingScanlineOverlay_IsVisibleChanged;
        this.Loaded += LoadingScanlineOverlay_Loaded; // Initialize brush on load
        // Initialize with default brush (will be updated on load)
        _scanlineBrush = CreateScanlineBrush();
    }

    private void LoadingScanlineOverlay_Loaded(object sender, RoutedEventArgs e)
    {
        // Attempt to determine theme based on background color when loaded
        var bgBrush = FindBackgroundBrush(this);
        if (bgBrush != null && IsDarkColor(bgBrush.Color))
        {
            _useLightTheme = false;
        }
        else
        {
            _useLightTheme = true;
        }
        // Recreate brush based on detected theme
        _scanlineBrush = CreateScanlineBrush();
        InvalidateVisual(); // Redraw with correct brush
    }

    // Helper to find the effective background of the control or its parent
    private SolidColorBrush? FindBackgroundBrush(DependencyObject? element)
    {
        while (element != null)
        {
            Brush? background = null;

            // Check common types that have a Background property
            if (element is Control control) // Window, UserControl, Button etc. derive from Control
            {
                background = control.Background;
            }
            else if (element is Panel panel) // Grid, StackPanel, etc.
            {
                background = panel.Background;
            }
            else if (element is Border border)
            {
                background = border.Background;
            }
            // Add other relevant types here if necessary (e.g., Page)

            // If we found a solid, non-transparent brush, return it
            if (background is SolidColorBrush scb && scb.Color != Colors.Transparent)
            {
                return scb;
            }

            // Move up the visual tree
            element = VisualTreeHelper.GetParent(element);
        }

        // Fallback: If no background found in ancestors, check application resources
        return Application.Current?.TryFindResource("WindowBackgroundBrush") as SolidColorBrush;
    }

    // Simple heuristic to determine if a color is "dark"
    private bool IsDarkColor(Color color)
    {
        // Calculate perceived brightness (luminance formula)
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
        return luminance < 0.5;
    }

    private Brush CreateScanlineBrush()
    {
        // Use theme-aware colors - very subtle difference from background
        // High transparency (Alpha around 15-30)
        Color baseColor = _useLightTheme ? Color.FromArgb(20, 0, 0, 0) : Color.FromArgb(25, 255, 255, 255); // Very transparent black/white

        // Create a gradient for soft edges
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5), // Horizontal gradient
            EndPoint = new Point(1, 0.5)
        };
        // Fade in, solid color, fade out
        gradient.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
        gradient.GradientStops.Add(new GradientStop(baseColor, 0.2)); // Adjust fade distance
        gradient.GradientStops.Add(new GradientStop(baseColor, 0.8)); // Adjust fade distance
        gradient.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));

        gradient.Freeze(); // Optimize
        return gradient;
    }

    private void LoadingScanlineOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        bool isVisible = (bool)e.NewValue;
        UpdateRenderingSubscription(isVisible);
    }

    private void UpdateRenderingSubscription(bool shouldBeVisible)
    {
        if (shouldBeVisible && !_isSubscribedToRendering)
        {
            _lastRenderTime = TimeSpan.Zero; // Reset time on subscribe
            CompositionTarget.Rendering += OnRendering;
            _isSubscribedToRendering = true;
        }
        else if (!shouldBeVisible && _isSubscribedToRendering)
        {
            CompositionTarget.Rendering -= OnRendering;
            _isSubscribedToRendering = false;
            this.InvalidateVisual(); // Clear visual when hiding
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_isSubscribedToRendering || !IsVisible) // Double check state
        {
            // Should not happen if IsVisibleChanged handler works correctly, but belt-and-suspenders
            if (_isSubscribedToRendering)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isSubscribedToRendering = false;
            }
            return;
        }

        var args = (RenderingEventArgs)e;
        var renderingTime = args.RenderingTime;

        if (_lastRenderTime == TimeSpan.Zero)
        {
            _lastRenderTime = renderingTime;
            return;
        }

        TimeSpan elapsed = renderingTime - _lastRenderTime;
        // Cap elapsed time to prevent large jumps if rendering was paused
        if (elapsed.TotalSeconds > 0.5) elapsed = TimeSpan.FromSeconds(1.0 / 60.0);

        // Update animation state (horizontal offset)
        _scanlineOffset += elapsed.TotalSeconds * PixelsPerSecond;
        // Keep offset within a reasonable range to avoid precision issues, wrap around
        _scanlineOffset %= ScanlineSpacing;

        _lastRenderTime = renderingTime;

        this.InvalidateVisual(); // Request redraw
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (!_isSubscribedToRendering || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return; // Don't draw if not subscribed or no size
        }

        double currentX = -ScanlineWidth + (_scanlineOffset % ScanlineSpacing);

        // Draw scanlines across the width, wrapping around
        while (currentX < ActualWidth)
        {
            var rect = new Rect(currentX, 0, ScanlineWidth, ActualHeight);
            drawingContext.DrawRectangle(_scanlineBrush, null, rect);
            currentX += ScanlineSpacing;
        }
    }

    // Ensure cleanup on unload or disposal
    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (VisualTreeHelper.GetParent(this) == null) // Detached from visual tree
        {
            UpdateRenderingSubscription(false); // Unsubscribe if detached
        }
        // If re-attached, IsVisibleChanged should handle re-subscribing
    }
}

