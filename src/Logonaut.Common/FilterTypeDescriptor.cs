using CommunityToolkit.Mvvm.ComponentModel;

namespace Logonaut.Common;

public partial class PaletteItemDescriptor : ObservableObject
{
    [ObservableProperty]
    private string _displayName;

    public string TypeIdentifier { get; } // Used to create the IFilter and find icon

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string? _initialValue; // The selected text, or null

    public bool IsDynamic { get; } // True for "Initialized Substring"

    // Constructor for static items
    public PaletteItemDescriptor(string displayName, string typeIdentifier, bool isEnabled = true)
    {
        _displayName = displayName;
        TypeIdentifier = typeIdentifier;
        _isEnabled = isEnabled;
        IsDynamic = false;
    }

    // Constructor for dynamic item
    public PaletteItemDescriptor(string initialDisplayName, string typeIdentifier, bool isDynamic, string? initialValue = null)
    {
        _displayName = initialDisplayName;
        TypeIdentifier = typeIdentifier;
        IsDynamic = isDynamic;
        _initialValue = initialValue;
        _isEnabled = false; // Dynamic item starts disabled
    }
}
