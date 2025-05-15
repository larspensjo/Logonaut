using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.UI.Commands;

namespace Logonaut.UI.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{
    // --- Undo/Redo Stacks ---
    private readonly Stack<IUndoableAction> _undoStack = new();
    private readonly Stack<IUndoableAction> _redoStack = new();

    // --- Undo/Redo Commands ---
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }

    // --- ICommandExecutor Implementation ---
    public void Execute(IUndoableAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        action.Execute();
        _undoStack.Push(action);
        _redoStack.Clear(); // Clear redo stack on new action

        // Update CanExecute state for UI buttons
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();

        // Trigger necessary updates AFTER the action is executed
        TriggerFilterUpdate(); // Re-filter based on the new state
        SaveCurrentSettingsDelayed(); // Save the new state
    }

    private void Undo()
    {
        if (_undoStack.TryPop(out var action))
        {
            action.Undo();
            _redoStack.Push(action);
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();

            TriggerFilterUpdate(); // Re-filter based on the restored state
            SaveCurrentSettingsDelayed(); // Save the restored state
        }
    }
    private bool CanUndo() => _undoStack.Count > 0;

    private void Redo()
    {
        if (_redoStack.TryPop(out var action))
        {
            action.Execute(); // Re-execute the action
            _undoStack.Push(action);
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();

            TriggerFilterUpdate(); // Re-filter based on the re-applied state
            SaveCurrentSettingsDelayed(); // Save the re-applied state
        }
    }
    private bool CanRedo() => _redoStack.Count > 0;
}
