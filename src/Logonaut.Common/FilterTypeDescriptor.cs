namespace Logonaut.Common;

/// <summary>
/// Describes a filter type for display in the UI, typically in a palette.
/// </summary>
/// <param name="DisplayName">User-friendly name for the filter type (e.g., "Substring", "AND Group").</param>
/// <param name="TypeIdentifier">A string identifier corresponding to IFilter.TypeText, used for creating the model and for icon conversion.</param>
public record FilterTypeDescriptor(string DisplayName, string TypeIdentifier);
