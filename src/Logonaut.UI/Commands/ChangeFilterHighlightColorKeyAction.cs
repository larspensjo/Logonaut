using Logonaut.UI.ViewModels;
using System.Diagnostics;

namespace Logonaut.UI.Commands;

public class ChangeFilterHighlightColorKeyAction : IUndoableAction
{
    private readonly FilterViewModel _filterVm;
    private readonly string _oldColorKey;
    private readonly string _newColorKey;

    public string Description => $"Change filter highlight color from '{_oldColorKey}' to '{_newColorKey}' for '{_filterVm.DisplayText}'";

    public ChangeFilterHighlightColorKeyAction(FilterViewModel filterVm, string oldColorKey, string newColorKey)
    {
        _filterVm = filterVm;
        _oldColorKey = oldColorKey;
        _newColorKey = newColorKey;
    }

    public void Execute()
    {
        _filterVm.Filter.HighlightColorKey = _newColorKey;
        _filterVm.RefreshProperties(); // Notify UI of change
        Debug.WriteLine($"Executed ChangeFilterHighlightColorKeyAction: Set key to '{_newColorKey}' for '{_filterVm.DisplayText}'");
    }

    public void Undo()
    {
        _filterVm.Filter.HighlightColorKey = _oldColorKey;
        _filterVm.RefreshProperties(); // Notify UI of change
        Debug.WriteLine($"Undone ChangeFilterHighlightColorKeyAction: Restored key to '{_oldColorKey}' for '{_filterVm.DisplayText}'");
    }
}
