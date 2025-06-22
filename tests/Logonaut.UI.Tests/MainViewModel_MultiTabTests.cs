using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Logonaut.Common;
using System.Collections.Generic;
using System;
using Logonaut.UI.ViewModels;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using Logonaut.TestUtils;

namespace Logonaut.UI.Tests.ViewModels;

/*
 * Unit tests for the MainViewModel focusing on multi-tab management functionality.
 * This includes creating, closing, and switching between tabs in response to user
 * actions like opening files or pasting content. It also verifies specific behaviors
 * like handling duplicate file opens and file resets.
 */
[TestClass] public class MainViewModel_MultiTabTests : MainViewModelTestBase
{
    [TestInitialize] public override void TestInitialize()
    {
        // Arrange
        base.TestInitialize();
        base.SetupMainViewModel(); // This sets up a VM with a single "Welcome" tab.
    }

    // Verifies: [ReqOpenLogFilev1], [ReqTabbedInterfaceV1]
    [TestMethod] public async Task OpenLogFileCommand_WithNewPath_CreatesNewTabAndActivatesIt()
    {
        // Arrange
        string newFilePath = "C:\\logs\\new_log.txt";
        _mockFileDialog.FileToReturn = newFilePath;
        _mockFileLogSource.LinesForInitialRead.AddRange(new[] { "line1", "line2" });
        int initialTabCount = _viewModel.TabViewModels.Count;

        // Act
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks); // Allow activation to complete

        // Assert
        Assert.AreEqual(initialTabCount, _viewModel.TabViewModels.Count, "A new tab should replace the initial welcome tab.");
        var newTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(newTab, "A new tab should be active.");
        Assert.AreEqual(SourceType.File, newTab.SourceType, "New tab's source type should be File.");
        Assert.AreEqual(newFilePath, newTab.SourceIdentifier, "New tab's source identifier should be the file path.");
        Assert.IsTrue(_mockFileLogSource.IsRunning, "The underlying log source should be running.");
    }

    // Verifies: [ReqTabbedInterfaceV1]
    [TestMethod] public async Task OpenLogFileCommand_WithExistingPath_ActivatesExistingTab()
    {
        // Arrange
        string filePath1 = "C:\\logs\\log1.txt";
        string filePath2 = "C:\\logs\\log2.txt";

        // Open first file
        _mockFileDialog.FileToReturn = filePath1;
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        var tab1 = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(tab1);

        // Open second file to make it active
        _mockFileDialog.FileToReturn = filePath2;
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        var tab2 = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(tab2);
        Assert.AreNotSame(tab1, tab2, "Second tab should be active.");
        int tabCount = _viewModel.TabViewModels.Count;

        // Act
        // Set the dialog to return the path of the first, now-inactive tab
        _mockFileDialog.FileToReturn = filePath1;
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);

        // Assert
        Assert.AreEqual(tabCount, _viewModel.TabViewModels.Count, "No new tab should have been created.");
        Assert.AreSame(tab1, _viewModel.ActiveTabViewModel, "The existing tab for the file should now be active.");
    }

    // Verifies: [ReqPasteFromClipboardv1], [ReqTabbedInterfaceV1]
    [TestMethod] public void PasteCommand_AlwaysCreatesNewUniqueTab()
    {
        // Arrange
        int initialTabCount = _viewModel.TabViewModels.Count;
        string pastedContent = "Pasted log content.";
        RunOnSta(() => Clipboard.SetText(pastedContent)); // Clipboard access requires STA thread

        // Act
        _viewModel.PasteCommand.Execute(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);
        
        // Assert
        Assert.AreEqual(initialTabCount, _viewModel.TabViewModels.Count, "A new tab should replace the initial welcome tab.");
        var newTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(newTab, "A new tab should be active.");
        Assert.AreEqual(SourceType.Pasted, newTab.SourceType, "New tab's source type should be Pasted.");
        StringAssert.Contains(newTab.SourceIdentifier ?? "", "pasted_", "Pasted tab should have a unique identifier.");
        Assert.AreEqual(1, newTab.FilteredLogLines.Count);
        Assert.AreEqual(pastedContent, newTab.FilteredLogLines[0].Text);
    }

    // Verifies: [ReqTabCloseButtonV1]
    [TestMethod] public async Task CloseTabCommand_RemovesTabAndActivatesPrevious()
    {
        // Arrange
        // Open two files to get a total of three tabs (Welcome, log1, log2)
        _mockFileDialog.FileToReturn = "C:\\logs\\log1.txt";
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        var tab1 = _viewModel.TabViewModels.First(t => t.SourceIdentifier == "C:\\logs\\log1.txt");

        _mockFileDialog.FileToReturn = "C:\\logs\\log2.txt";
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        var tab2 = _viewModel.TabViewModels.First(t => t.SourceIdentifier == "C:\\logs\\log2.txt");

        _viewModel.ActiveTabViewModel = tab2; // Ensure tab2 is active
        int initialCount = _viewModel.TabViewModels.Count;

        // Act
        tab2.CloseTabCommand.Execute(null);

        // Assert
        Assert.AreEqual(initialCount - 1, _viewModel.TabViewModels.Count, "Tab count should decrease by one.");
        CollectionAssert.DoesNotContain(_viewModel.TabViewModels, tab2, "Closed tab should be removed from the collection.");
        Assert.AreSame(tab1, _viewModel.ActiveTabViewModel, "The previous tab should become active.");
    }

    // Verifies: [ReqTabCloseButtonV1]
    [TestMethod] public void CloseTabCommand_OnLastTab_CreatesNewEmptyTab()
    {
        // Arrange
        Assert.AreEqual(1, _viewModel.TabViewModels.Count, "Arrange failed: Should start with one tab.");
        var lastTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(lastTab);

        // Act
        lastTab.CloseTabCommand.Execute(null);

        // Assert
        Assert.AreEqual(1, _viewModel.TabViewModels.Count, "Tab count should remain one.");
        var newTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(newTab, "A new active tab should exist.");
        Assert.AreNotSame(lastTab, newTab, "The new active tab should be a new instance.");
        Assert.AreEqual("New Tab", newTab.Header, "The new tab should have a default header.");
    }

    // Verifies: [ReqFileResetHandlingv1]
    [TestMethod] public async Task FileReset_OnActiveTab_TransitionsTabToSnapshotState()
    {
        // Arrange
        string filePath = "C:\\logs\\resettable.log";
        _mockFileDialog.FileToReturn = filePath;
        await _viewModel.OpenLogFileCommand.ExecuteAsync(null);
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(500).Ticks);

        var fileTab = _viewModel.ActiveTabViewModel;
        Assert.IsNotNull(fileTab);
        Assert.AreEqual(filePath, fileTab.SourceIdentifier);
        string originalHeader = fileTab.Header;
        var source = fileTab.LogSourceExposeDeprecated as MockLogSource;
        Assert.IsNotNull(source, "Failed to get mock source from tab.");

        // Act
        source.SimulateFileResetCallback();
        _backgroundScheduler.AdvanceBy(TimeSpan.FromMilliseconds(100).Ticks);

        // Assert
        Assert.AreEqual(SourceType.Snapshot, fileTab.SourceType, "Tab's SourceType should change to Snapshot.");
        StringAssert.Contains(fileTab.Header, originalHeader, "Snapshot tab header should contain original part.");
        StringAssert.Contains(fileTab.Header, "(Snapshot", "Snapshot tab header should indicate it's a snapshot.");
        Assert.IsFalse(fileTab.IsActive, "Snapshot tab should be deactivated.");
        Assert.AreNotEqual(filePath, fileTab.SourceIdentifier, "Snapshot tab identifier should be changed to a unique value.");
    }
}
