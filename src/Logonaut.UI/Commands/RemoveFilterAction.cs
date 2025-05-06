using Logonaut.Filters;
using Logonaut.UI.ViewModels;
using System.Diagnostics;

namespace Logonaut.UI.Commands;

/// <summary>
/// Action to remove a filter node from its parent composite filter.
/// </summary>
public class RemoveFilterAction : IUndoableAction
{
    public string Description => $"Remove filter '{_removedModel.DisplayText}'";
    private readonly FilterViewModel _parentVm;
    private readonly FilterViewModel _removedVm;
    private readonly IFilter _removedModel;
    private readonly int _originalIndex; // Index in both VM Children and Model SubFilters

    public RemoveFilterAction(FilterViewModel parentVm, FilterViewModel removedVm)
    {
        if (parentVm.Filter is not CompositeFilter)
        {
            throw new ArgumentException("Parent ViewModel must wrap a CompositeFilter.", nameof(parentVm));
        }
        _parentVm = parentVm;
        _removedVm = removedVm;
        _removedModel = removedVm.Filter;

        // Store the original index BEFORE removal
        _originalIndex = _parentVm.Children.IndexOf(_removedVm);
            if (_originalIndex < 0) // Should not happen if called correctly
            {
                Debug.WriteLine($"WARNING in RemoveFilterAction Constructor: VM to remove not found in parent's children.");
                // Attempt to find model index as fallback? Might be inconsistent.
                if (_parentVm.Filter is CompositeFilter cf) {
                    _originalIndex = cf.SubFilters.IndexOf(_removedModel);
                }
                if (_originalIndex < 0) {
                    throw new InvalidOperationException("ViewModel to remove is not a child of the specified parent.");
                }
            }
    }

    public void Execute()
    {
            if (_parentVm.Filter is CompositeFilter compositeParent)
            {
                // Ensure index is still valid before removing
                if (_originalIndex >= 0 && _originalIndex < _parentVm.Children.Count && _parentVm.Children[_originalIndex] == _removedVm)
                {
                    _parentVm.Children.RemoveAt(_originalIndex);
                } else {
                    // Index is invalid or item mismatch, try removing by instance
                    if(!_parentVm.Children.Remove(_removedVm)) {
                        Debug.WriteLine($"ERROR in RemoveFilterAction Execute: Failed to remove VM.");
                        return; // Stop if VM removal failed
                    }
                }

                // Remove model using stored index or instance
                if (_originalIndex >= 0 && _originalIndex < compositeParent.SubFilters.Count && compositeParent.SubFilters[_originalIndex] == _removedModel)
                {
                compositeParent.SubFilters.RemoveAt(_originalIndex);
                } else {
                    // Fallback to removing by instance
                    if (!compositeParent.SubFilters.Remove(_removedModel)) {
                        Debug.WriteLine($"ERROR in RemoveFilterAction Execute: Failed to remove Model.");
                        // Consider if we need to undo the VM removal here? Complex state.
                    }
                }
            Debug.WriteLine($"Executed RemoveFilterAction: Removed '{_removedModel.GetType().Name}' from '{_parentVm.DisplayText}'");
            }
            else {
                Debug.WriteLine($"ERROR in RemoveFilterAction Execute: Parent VM does not wrap a CompositeFilter.");
            }
    }

    public void Undo()
    {
        if (_parentVm.Filter is CompositeFilter compositeParent)
        {
            // Re-insert at the original index
            int vmInsertIndex = Math.Min(_originalIndex, _parentVm.Children.Count);
            _parentVm.Children.Insert(vmInsertIndex, _removedVm);

            int modelInsertIndex = Math.Min(_originalIndex, compositeParent.SubFilters.Count);
            compositeParent.SubFilters.Insert(modelInsertIndex, _removedModel);

            Debug.WriteLine($"Undone RemoveFilterAction: Re-added '{_removedModel.GetType().Name}' to '{_parentVm.DisplayText}' at index {_originalIndex}");
        }
        else {
                Debug.WriteLine($"ERROR in RemoveFilterAction Undo: Parent VM does not wrap a CompositeFilter.");
        }
    }
}
