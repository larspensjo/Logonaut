namespace Logonaut.Core.Commands;
/// <summary>
/// Defines the contract for an action that can be executed and undone.
/// </summary>
public interface IUndoableAction
{
    /// <summary>
    /// Executes the action.
    /// </summary>
    void Execute();

    /// <summary>
    /// Undoes the action, restoring the previous state.
    /// </summary>
    void Undo();

    string Description { get; }
}

/*
* Defines the contract for a service that executes undoable actions.
*
* Purpose:
* To provide a centralized mechanism for executing operations that support
* undo/redo functionality, managing the underlying command history stacks.
*
* Role:
* Decouples the initiators of actions (e.g., ViewModels reacting to UI events)
* from the implementation details of undo/redo stack management. Ensures
* consistent handling of command execution and history.
*
* Benefits:
* Centralizes undo/redo logic, promotes testability of actions, decouples
* action execution from history management.
*
* Implementation Notes:
* Implementers (like MainViewModel) typically maintain internal undo and redo
* stacks and update them when Execute is called.
*/
public interface ICommandExecutor
{
    /// <summary>
    /// Executes the specified action and adds it to the undo stack.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    void Execute(IUndoableAction action);
}
