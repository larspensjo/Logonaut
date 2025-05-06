using Logonaut.UI.ViewModels;
using System.Diagnostics;

namespace Logonaut.UI.Commands;

/// <summary>
/// Action to toggle the Enabled state of a filter node.
/// </summary>
public class ToggleFilterEnabledAction : IUndoableAction
{
    public string Description => $"Toggle filter enabled state for '{_filterVm.DisplayText}' to {_newState}";
    private readonly FilterViewModel _filterVm;
    private readonly bool _newState; // Store the state *after* the toggle

    public ToggleFilterEnabledAction(FilterViewModel filterVm)
    {
        _filterVm = filterVm;
        // Determine the state *after* the toggle will happen
        _newState = !_filterVm.Enabled;
    }

    public void Execute()
    {
        _filterVm.Filter.Enabled = _newState;
        // Manually trigger property changed on VM
        _filterVm.RefreshProperties();
        Debug.WriteLine($"Executed ToggleFilterEnabledAction: Set Enabled to {_newState} for '{_filterVm.DisplayText}'");
    }

    public void Undo()
    {
        // Toggle back to the previous state
        _filterVm.Filter.Enabled = !_newState;
            // Manually trigger property changed on VM
        _filterVm.RefreshProperties();
        Debug.WriteLine($"Undone ToggleFilterEnabledAction: Restored Enabled to {!_newState} for '{_filterVm.DisplayText}'");
    }
}
