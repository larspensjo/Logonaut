using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.UI.ViewModels;
using Logonaut.UI.Commands;
using Logonaut.TestUtils;
using Logonaut.Core;

namespace Logonaut.UI.Tests.ViewModels;

[TestClass] public class TabViewModel_HeaderEditTests
{
    private MockCommandExecutor _executor = null!;
    private MockLogSourceProvider _provider = null!;
    private TabViewModel _viewModel = null!;
    private ImmediateSynchronizationContext _context = null!;

    [TestInitialize] public void TestInitialize()
    {
        _executor = new MockCommandExecutor();
        _provider = new MockLogSourceProvider();
        _context = new ImmediateSynchronizationContext();
        _viewModel = new TabViewModel("Tab1", "Default", SourceType.Pasted, "id", _provider, _executor, _context);
    }

    [TestMethod] public void EndEditHeaderCommand_ShouldExecuteChangeAction_WhenNameChanged()
    {
        // Arrange
        _viewModel.BeginEditHeaderCommand.Execute(null);
        _viewModel.EditingHeaderName = "NewName";
        _executor.Reset();

        // Act
        _viewModel.EndEditHeaderCommand.Execute(null);

        // Assert
        Assert.IsFalse(_viewModel.IsEditingHeader);
        Assert.IsInstanceOfType(_executor.LastExecutedAction, typeof(ChangeTabHeaderAction));
        Assert.AreEqual("NewName", _viewModel.Header);
    }

    [TestMethod] public void EndEditHeaderCommand_ShouldNotExecuteAction_WhenNameUnchanged()
    {
        // Arrange
        _viewModel.BeginEditHeaderCommand.Execute(null);
        _executor.Reset();

        // Act
        _viewModel.EndEditHeaderCommand.Execute(null);

        // Assert
        Assert.IsFalse(_viewModel.IsEditingHeader);
        Assert.IsNull(_executor.LastExecutedAction);
        Assert.AreEqual("Tab1", _viewModel.Header);
    }
}
