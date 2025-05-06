using Logonaut.Filters;
using Logonaut.UI.ViewModels;
using System.Diagnostics;

namespace Logonaut.UI.Commands;

/// <summary>
/// Action to add a filter node to a parent composite filter.
/// </summary>
public class AddFilterAction : IUndoableAction
{
    private readonly FilterViewModel _parentVm;
    private readonly IFilter _newFilterModel;
    private FilterViewModel? _newFilterVm; // Created during Execute
    private readonly int _targetIndex; // Where to insert for precise Undo

    public string Description => $"Add '{_newFilterModel.GetType().Name}' to '{_parentVm.DisplayText}' at index {_targetIndex}";

    // Note: Takes the MODEL to add, creates the VM during Execute.
    public AddFilterAction(FilterViewModel parentVm, IFilter newFilterModel, int? targetIndex = null)
    {
        // Ensure parent is composite (caller should validate, but double-check)
        if (parentVm.Filter is not CompositeFilter)
        {
            throw new ArgumentException("Parent ViewModel must wrap a CompositeFilter.", nameof(parentVm));
        }
        _parentVm = parentVm;
        _newFilterModel = newFilterModel;
        // If no index specified, add to the end. Store count for Undo.
        _targetIndex = targetIndex ?? _parentVm.Children.Count;
    }

    public void Execute()
    {
        if (_parentVm.Filter is CompositeFilter compositeParent)
        {
            // 1. Add model to parent model
            // Ensure index is valid for insertion
            int modelInsertIndex = Math.Min(_targetIndex, compositeParent.SubFilters.Count);
            compositeParent.SubFilters.Insert(modelInsertIndex, _newFilterModel);

            // 2. Create and add VM to parent VM
            // Need to get the CommandExecutor from the parent (or somewhere accessible)
            if (_parentVm is not ICommandExecutorProvider provider) // Assuming we add this interface to FilterViewModel
            {
                    // Fallback: Try finding it via a static service locator or MainViewModel instance if desperate
                    // This highlights a potential dependency management challenge.
                    // Let's assume FilterViewModel can provide it for now.
                    throw new InvalidOperationException("Cannot get CommandExecutor from parent ViewModel.");
            }
            var commandExecutor = provider.CommandExecutor; // Get executor from parent
            _newFilterVm = new FilterViewModel(_newFilterModel, commandExecutor, _parentVm); // Pass executor

            // Ensure index is valid for VM insertion
            int vmInsertIndex = Math.Min(_targetIndex, _parentVm.Children.Count);
            _parentVm.Children.Insert(vmInsertIndex, _newFilterVm);

            Debug.WriteLine($"Executed AddFilterAction: Added '{_newFilterModel.GetType().Name}' to '{_parentVm.DisplayText}' at index {_targetIndex}");
        }
        else
        {
                throw new InvalidOperationException("Parent ViewModel must wrap a CompositeFilter.");
        }
    }

    public void Undo()
    {
        if (_parentVm.Filter is CompositeFilter compositeParent && _newFilterVm != null)
        {
                // Use the stored VM and index to remove precisely
            int vmRemoveIndex = _parentVm.Children.IndexOf(_newFilterVm);
                if (vmRemoveIndex >= 0) // Should always be found if Execute succeeded
                {
                    // 1. Remove VM from parent VM Children
                    _parentVm.Children.RemoveAt(vmRemoveIndex);

                    // 2. Remove Model from parent Model SubFilters
                    // Find by instance or potentially re-calculate index if needed, but using model index is safer
                    int modelRemoveIndex = compositeParent.SubFilters.IndexOf(_newFilterModel);
                    if (modelRemoveIndex >= 0)
                    {
                        compositeParent.SubFilters.RemoveAt(modelRemoveIndex);
                        Debug.WriteLine($"Undone AddFilterAction: Removed '{_newFilterModel.GetType().Name}' from '{_parentVm.DisplayText}'");
                    } else {
                        Debug.WriteLine($"ERROR in AddFilterAction Undo: Model not found in parent's SubFilters.");
                    }
                } else {
                    Debug.WriteLine($"ERROR in AddFilterAction Undo: VM not found in parent's Children.");
                }

            _newFilterVm = null; // Clear reference after undo
        }
            else
        {
                Debug.WriteLine($"ERROR in AddFilterAction Undo: Parent not composite or new VM is null.");
        }
    }
}

// Helper Interface to allow actions to get the executor
public interface ICommandExecutorProvider
{
    ICommandExecutor CommandExecutor { get; }
}
