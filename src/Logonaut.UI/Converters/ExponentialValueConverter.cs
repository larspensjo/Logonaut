using System;
using System.Globalization;
using System.Windows.Data;

namespace Logonaut.UI.Converters;

// Converts between a linear slider value (e.g., 0-100) and an exponential
// target value (e.g., 1-200 LPS), providing more sensitivity at the lower end.
public class ExponentialValueConverter : IValueConverter
{
    // Keep properties as they are - they define the *intended* full range
    public double MinTargetValue { get; set; } = 0.0; // Allow setting to 0
    public double MaxTargetValue { get; set; } = 200.0;
    public double MinSliderValue { get; set; } = 0.0;
    public double MaxSliderValue { get; set; } = 100.0;

    // Define the minimum value for the *exponential part* of the range
    private const double EffectiveMinTargetForExponent = 1.0;

    /// <summary>
    /// Converts the target exponential value (LPS from ViewModel, 0-200) to the linear slider value (0-100).
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double targetValue)
            return MinSliderValue; // Invalid input

        // --- SPECIAL CASE for 0 ---
        // If the target value is at or below the minimum threshold for the exponential part, map it to the slider minimum.
        if (targetValue < EffectiveMinTargetForExponent)
        {
            return MinSliderValue; // Target 0 maps to Slider 0
        }

        // Proceed with exponential mapping for the range [EffectiveMinTargetForExponent, MaxTargetValue] -> [MinSliderValue, MaxSliderValue]
        if (MaxTargetValue <= EffectiveMinTargetForExponent) // Avoid Log issues if Max <= EffectiveMin
             return MinSliderValue; // Or handle as error/default


        try // Add try-catch for safety with Log
        {
             // Clamp targetValue within the effective exponential range before Log
            targetValue = Math.Max(EffectiveMinTargetForExponent, Math.Min(targetValue, MaxTargetValue));

            double logMin = Math.Log(EffectiveMinTargetForExponent); // Log(1) = 0
            double logMax = Math.Log(MaxTargetValue);
            double logTarget = Math.Log(targetValue);

            double logRange = logMax - logMin;
            if (Math.Abs(logRange) < 1e-9) // Avoid division by zero if Max==EffectiveMin
            {
                return MinSliderValue;
            }

            // Scale logarithmically within the 0-1 range
            double scale = (logTarget - logMin) / logRange;

            // Map to slider range
            double sliderValue = MinSliderValue + (MaxSliderValue - MinSliderValue) * scale;

            // Clamp result to slider bounds
            return Math.Max(MinSliderValue, Math.Min(sliderValue, MaxSliderValue));
        }
        catch (Exception ex) // Catch potential math errors
        {
             System.Diagnostics.Debug.WriteLine($"Error in ExponentialValueConverter.Convert: {ex.Message}");
             return MinSliderValue; // Fallback on error
        }
    }

    /// <summary>
    /// Converts the linear slider value (0-100) back to the target exponential value (LPS for ViewModel, 0-200).
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double sliderValue)
            return MinTargetValue; // Invalid input maps to Target 0

        // --- SPECIAL CASE for 0 ---
        // If the slider is at its minimum, map it to the target minimum (0)
        if (sliderValue <= MinSliderValue)
        {
            return MinTargetValue; // Slider 0 maps to Target 0
        }

        // Proceed with exponential mapping for the range [MinSliderValue, MaxSliderValue] -> [EffectiveMinTargetForExponent, MaxTargetValue]
        if (MaxSliderValue <= MinSliderValue) // Avoid division by zero
            return MinTargetValue; // Or handle as error/default

        if (MaxTargetValue <= EffectiveMinTargetForExponent) // Avoid Pow issues if Max <= EffectiveMin
             return MinTargetValue; // Or handle as error/default

        try // Add try-catch for safety with Pow
        {
            // Clamp slider value before scaling
            sliderValue = Math.Max(MinSliderValue, Math.Min(sliderValue, MaxSliderValue));

            // Scale slider value to 0-1 range relative to its own bounds
            double scale = (sliderValue - MinSliderValue) / (MaxSliderValue - MinSliderValue);
            scale = Math.Max(0, Math.Min(scale, 1)); // Clamp scale just in case

            // Calculate the ratio for the *exponential part* of the range
            double ratio = MaxTargetValue / EffectiveMinTargetForExponent;
            if (ratio <= 0) // Avoid Pow issues with non-positive base
                return MinTargetValue;

            // Apply exponential scaling starting from the effective minimum
            double targetValue = EffectiveMinTargetForExponent * Math.Pow(ratio, scale);

            // Round to nearest integer for LPS and clamp to the *original* target range (0-200)
            double roundedValue = Math.Round(targetValue);
            return Math.Max(MinTargetValue, Math.Min(roundedValue, MaxTargetValue)); // Clamp to 0-200
        }
        catch (Exception ex) // Catch potential math errors
        {
             System.Diagnostics.Debug.WriteLine($"Error in ExponentialValueConverter.ConvertBack: {ex.Message}");
             return MinTargetValue; // Fallback to Target 0 on error
        }
    }
}
