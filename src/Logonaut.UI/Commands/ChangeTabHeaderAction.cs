using Logonaut.UI.ViewModels;
using Logonaut.Core.Commands;

namespace Logonaut.UI.Commands;

/// <summary>
/// Action to change the header of a TabViewModel.
/// </summary>
public class ChangeTabHeaderAction : IUndoableAction
{
    private readonly TabViewModel _tabViewModel;
    private readonly string _oldHeader;
    private readonly string _newHeader;

    public string Description => $"Change tab header from '{_oldHeader}' to '{_newHeader}'";

    public ChangeTabHeaderAction(TabViewModel tabViewModel, string oldHeader, string newHeader)
    {
        _tabViewModel = tabViewModel;
        _oldHeader = oldHeader;
        _newHeader = newHeader;
    }

    public void Execute()
    {
        _tabViewModel.Header = _newHeader;
    }

    public void Undo()
    {
        _tabViewModel.Header = _oldHeader;
    }
}
