using System;
using System.Globalization;
using System.Windows.Data;

namespace Logonaut.UI.Converters;

// Converts between a linear slider value (e.g., 0-100) and an exponential
// target value (e.g., 1-200 LPS), providing more sensitivity at the lower end.
public class ExponentialValueConverter : IValueConverter
{
    // --- Define the ranges ---
    // These could be DependencyProperties if more flexibility is needed,
    // but constants are fine for this specific use case.

    // The actual min/max values the application uses (LPS)
    public double MinTargetValue { get; set; } = 1.0;
    public double MaxTargetValue { get; set; } = 200.0;

    // The linear range of the slider control itself
    public double MinSliderValue { get; set; } = 0.0;
    public double MaxSliderValue { get; set; } = 100.0; // Using 0-100 for the slider range

    /// <summary>
    /// Converts the target exponential value (LPS from ViewModel) to the linear slider value.
    /// Uses logarithmic scaling.
    /// Formula: SliderValue = SliderRange * log(TargetValue / MinTarget) / log(MaxTarget / MinTarget) + MinSlider
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double targetValue && MinTargetValue > 0 && MaxTargetValue > MinTargetValue)
        {
            // Clamp targetValue to valid range before log
            targetValue = Math.Max(MinTargetValue, Math.Min(targetValue, MaxTargetValue));

            // Avoid Log(0) or Log(<0) if MinTargetValue is 1. Log(1) is 0.
            double logMin = Math.Log(MinTargetValue);
            double logMax = Math.Log(MaxTargetValue);
            double logTarget = Math.Log(targetValue);

            // Handle edge case where MinTargetValue is 1 (logMin becomes 0)
            if (Math.Abs(logMax - logMin) < 1e-9) // Avoid division by zero if Max=Min=1
            {
                return MinSliderValue;
            }

            // Scale logarithmically within the 0-1 range, then map to slider range
            double scale = (logTarget - logMin) / (logMax - logMin);
            double sliderValue = MinSliderValue + (MaxSliderValue - MinSliderValue) * scale;

            return Math.Max(MinSliderValue, Math.Min(sliderValue, MaxSliderValue)); // Clamp result
        }

        // Return Minimum slider value on error or invalid input
        return MinSliderValue;
    }

    /// <summary>
    /// Converts the linear slider value back to the target exponential value (LPS for ViewModel).
    /// Uses exponential scaling.
    /// Formula: TargetValue = MinTarget * (MaxTarget/MinTarget) ^ ((SliderValue - MinSlider) / SliderRange)
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double sliderValue && MaxSliderValue > MinSliderValue)
        {
            // Scale slider value to 0-1 range
            double scale = (sliderValue - MinSliderValue) / (MaxSliderValue - MinSliderValue);
            scale = Math.Max(0, Math.Min(scale, 1)); // Clamp scale just in case

            // Apply exponential scaling
            double targetValue = MinTargetValue * Math.Pow(MaxTargetValue / MinTargetValue, scale);

            // Round to nearest integer for LPS and clamp to target range
            return Math.Max(MinTargetValue, Math.Min((double)Math.Round(targetValue), MaxTargetValue));
        }

        // Return Minimum target value on error or invalid input
        return MinTargetValue;
    }
}
