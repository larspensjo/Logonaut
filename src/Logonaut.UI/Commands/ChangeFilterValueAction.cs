using Logonaut.UI.ViewModels;
using System.Diagnostics;
using Logonaut.Core.Commands;

namespace Logonaut.UI.Commands;

/// <summary>
/// Action to change the Value property of an editable filter node.
/// </summary>
public class ChangeFilterValueAction : IUndoableAction
{
    public string Description => $"Change filter value from '{_oldValue}' to '{_newValue}' for '{_filterVm.DisplayText}'";
    private readonly FilterViewModel _filterVm;
    private readonly string _oldValue;
    private readonly string _newValue;

    public ChangeFilterValueAction(FilterViewModel filterVm, string oldValue, string newValue)
    {
        if (!filterVm.IsEditable)
            throw new ArgumentException("Filter ViewModel must be editable.", nameof(filterVm));
        _filterVm = filterVm;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public void Execute()
    {
        _filterVm.Filter.Value = _newValue;
        // Important: Manually trigger property changed on the VM if the model change doesn't automatically
        // This ensures the UI updates if binding relies on the VM property.
        // Using ObservableObject's SetProperty in the VM setter handles this, but direct model mutation bypasses it.
        _filterVm.RefreshProperties();
        Debug.WriteLine($"Executed ChangeFilterValueAction: Set value to '{_newValue}' for '{_filterVm.DisplayText}'");
    }

    public void Undo()
    {
        _filterVm.Filter.Value = _oldValue;
        // Manually trigger property changed on VM
        _filterVm.RefreshProperties();
        Debug.WriteLine($"Undone ChangeFilterValueAction: Restored value to '{_oldValue}' for '{_filterVm.DisplayText}'");
    }
}
