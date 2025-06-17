using Logonaut.Core.Commands;
using System.Collections.Generic;

namespace Logonaut.TestUtils;

public class MockCommandExecutor : ICommandExecutor
{
    public IUndoableAction? LastExecutedAction { get; private set; }
    public List<IUndoableAction> ExecutionHistory { get; } = new List<IUndoableAction>();

    public void Execute(IUndoableAction action)
    {
        // Store the action for verification
        LastExecutedAction = action;
        ExecutionHistory.Add(action);

        // Actually execute the action to test its effect on the VM/Model state
        // In a more complex scenario, you might only store it without executing,
        // but for these tests, executing helps verify the outcome.
        action.Execute();
    }

    public void Reset()
    {
        LastExecutedAction = null;
        ExecutionHistory.Clear();
    }
}
