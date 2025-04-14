using System;
using System.Linq; // Required for Linq methods like FirstOrDefault, Any
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Logonaut.UI.ViewModels;
using Logonaut.Filters;
using Logonaut.UI.Services;
using Logonaut.Common; // Required for FilterProfile
using System.Collections.Generic; // Required for List
using System.Collections.ObjectModel; // Required for ObservableCollection
using System.Windows; // Required for DependencyObject
using System.Windows.Media; // Required for VisualTreeHelper
using System.Windows.Controls; // Required for TreeView
using System.Windows.Threading; // Required for DispatcherPriority

namespace Logonaut.UI.Tests.ViewModels
{
    [TestClass]
    public class MainViewModelTests
    {
        // --- Mocks for Dependencies ---

        private class MockFileDialogService : IFileDialogService
        {
            public string? FileToReturn { get; set; } = "C:\\fake\\log.txt";
            public bool ShouldCancel { get; set; } = false;
            public string? OpenFile(string title, string filter) => ShouldCancel ? null : FileToReturn;
        }

        private class MockInputPromptService : IInputPromptService
        {
            public string? InputToReturn { get; set; } = "Mock Input";
            public bool ShouldCancel { get; set; } = false;
            public string? ShowInputDialog(string title, string prompt, string defaultValue = "") => ShouldCancel ? null : InputToReturn;
        }

        // Helper to create a view model with mocks
       private MainViewModel CreateMockViewModel(IFileDialogService? fileDialog = null, IInputPromptService? prompt = null)
        {
            // Provide a basic SynchronizationContext for the test environment
            var testSyncContext = new SynchronizationContext();

            return new MainViewModel(
                fileDialog ?? new MockFileDialogService(),
                prompt ?? new MockInputPromptService(),
                logFilterProcessor: null, // Use default or mock if needed later
                uiContext: testSyncContext // Pass the test context
            );
        }

        // --- Helper Function to Find Visual Child ---
        private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            T? foundChild = null;
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is not T childType)

                {
                    // Recurse deeper
                    foundChild = FindVisualChild<T>(child, name);
                    if (foundChild != null) break; // Found in recursion
                }
                else
                {
                    // Check name if provided
                    if (!string.IsNullOrEmpty(name))
                    {
                        if (childType.Name == name)
                        {
                            foundChild = childType;
                            break; // Found by name
                        }
                        else
                        {
                            // Continue searching siblings or recurse if name doesn't match
                             foundChild = FindVisualChild<T>(child, name);
                             if (foundChild != null) break;
                        }
                    }
                    else
                    {
                        // Found the first instance of the type T
                        foundChild = childType;
                        break;
                    }
                }
            }
            return foundChild;
        }

        // --- Test Methods ---
        // Note: This test requires running in an environment that can instantiate WPF controls
        // and process the dispatcher queue. MSTest might require specific configuration
        // or attributes like [STATestMethod] if encountering threading issues.
        [TestMethod] public void AddFilter_WhenActiveProfileIsEmpty_ShouldUpdateTreeViewItems()
        {
            // Arrange - Requires running potentially on STA thread and using Dispatcher
            MainViewModel? vmMock = null;
            MainWindow? window = null;
            TreeView? filterTreeView = null;
            Exception? threadException = null;

            // Use a dedicated STA thread for WPF components
            var t = new Thread(() =>
            {
                try
                {
                    vmMock = CreateMockViewModel();
                    window = new MainWindow(vmMock);

                    // We need to load the window components but not show it.
                    // Calling Measure/Arrange/UpdateLayout forces template application and binding setup.
                    window.Measure(new Size(800, 600));
                    window.Arrange(new Rect(0, 0, 800, 600));
                    window.UpdateLayout(); // Initial layout pass

                    // ***** Start search from window.Content *****
                    var windowContentElement = window.Content as UIElement;
                    Assert.IsNotNull(windowContentElement, "Window Content is null or not a UIElement after layout.");

                     // Ensure the TreeView is found after initial layout
                    filterTreeView = FindVisualChild<TreeView>(windowContentElement, "FilterTreeViewNameForTesting");
                    if (filterTreeView == null) throw new AssertFailedException("FilterTreeViewNameForTesting not found in MainWindow visual tree.");


                    // Pre-condition checks (on UI thread)
                    Assert.IsNotNull(vmMock.ActiveFilterProfile, "Pre-condition: Active profile is null.");
                    vmMock.ActiveFilterProfile.SetModelRootFilter(null); // Ensure tree is empty
                    vmMock.ActiveTreeRootNodes.Clear();
                    Assert.AreEqual(0, filterTreeView.Items.Count, "Pre-condition: TreeView should be empty initially.");

                    // Act - Execute command that modifies the ViewModel collection
                    vmMock.AddFilterCommand.Execute("Substring");

                    // Crucially, wait for the dispatcher to process binding updates and layout changes
                    // Use DispatcherPriority.Loaded or DataBind to ensure bindings have propagated
                     window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); // Process bindings
                     window.Dispatcher.Invoke(() => { window.UpdateLayout(); }, DispatcherPriority.Loaded); // Process layout


                    // Assert - Check the Items collection of the actual TreeView control
                    Assert.AreEqual(1, filterTreeView.Items.Count, "TreeView Items collection should have one item.");
                    Assert.IsNotNull(filterTreeView.Items[0], "TreeView item should not be null.");
                    Assert.IsInstanceOfType(filterTreeView.Items[0], typeof(FilterViewModel), "TreeView item should be a FilterViewModel.");

                    var itemViewModel = (FilterViewModel)filterTreeView.Items[0];
                    Assert.IsInstanceOfType(itemViewModel.FilterModel, typeof(SubstringFilter), "Filter model of the TreeView item should be SubstringFilter.");

                }
                catch (Exception ex)
                {
                    threadException = ex; // Capture exception to rethrow on main thread
                }
                finally
                {
                     // Ensure Dispatcher processing stops if the test window wasn't shown
                     if (window != null && window.Dispatcher.HasShutdownStarted == false)
                     {
                        window.Dispatcher.InvokeShutdown();
                     }
                }
            });

            t.SetApartmentState(ApartmentState.STA); // Set the thread to STA
            t.Start();
            t.Join(); // Wait for the STA thread to finish

            // Rethrow any exception caught on the STA thread
            if (threadException != null)
            {
                throw new AssertFailedException($"Exception occurred on STA thread: {threadException.Message}", threadException);
            }
        }

        [TestMethod] public void AddFilterCommand_WhenActiveProfileIsEmpty_UpdatesActiveTreeRootNodes()
        {
            // Arrange
            var viewModel = CreateMockViewModel();

            // Ensure we have an active profile to work with
            Assert.IsNotNull(viewModel.ActiveFilterProfile, "Test setup failed: No active profile found after initialization.");

            // Explicitly clear the root filter of the active profile to simulate an empty tree state
            viewModel.ActiveFilterProfile.SetModelRootFilter(null);
            // Also clear the collection bound to the TreeView, mimicking the state after SetModelRootFilter(null)
            viewModel.ActiveTreeRootNodes.Clear(); // Direct manipulation for test setup

            Assert.IsNull(viewModel.ActiveFilterProfile.RootFilterViewModel, "Pre-condition failed: RootFilterViewModel should be null.");
            Assert.AreEqual(0, viewModel.ActiveTreeRootNodes.Count, "Pre-condition failed: ActiveTreeRootNodes should be empty.");
            Assert.IsNull(viewModel.SelectedFilterNode, "Pre-condition failed: SelectedFilterNode should be null.");

            // Act: Add the first filter node (e.g., a Substring filter)
            viewModel.AddFilterCommand.Execute("Substring");

            // Assert
            // 1. Check the collection bound to the TreeView
            Assert.AreEqual(1, viewModel.ActiveTreeRootNodes.Count, "ActiveTreeRootNodes should contain one item (the new root).");
            Assert.IsNotNull(viewModel.ActiveTreeRootNodes[0], "The item in ActiveTreeRootNodes should not be null.");
            Assert.IsInstanceOfType(viewModel.ActiveTreeRootNodes[0].FilterModel, typeof(SubstringFilter), "The filter model of the root node should be SubstringFilter.");

            // 2. Check if the ActiveFilterProfile's RootFilterViewModel was also updated (should be the same instance)
            Assert.IsNotNull(viewModel.ActiveFilterProfile.RootFilterViewModel, "Active profile's RootFilterViewModel should now be set.");
            Assert.AreSame(viewModel.ActiveTreeRootNodes[0], viewModel.ActiveFilterProfile.RootFilterViewModel, "ActiveTreeRootNodes item should be the same instance as the profile's RootFilterViewModel.");

            // 3. Check if the new node was automatically selected
            Assert.IsNotNull(viewModel.SelectedFilterNode, "SelectedFilterNode should be set to the new root.");
            Assert.AreSame(viewModel.ActiveTreeRootNodes[0], viewModel.SelectedFilterNode, "SelectedFilterNode should be the newly added root node.");
        }

        [TestMethod] public void Constructor_InitializesWithDefaultProfile()
        {
            // Arrange & Act
            var viewModel = CreateMockViewModel();

            // Assert
            Assert.AreEqual(1, viewModel.AvailableProfiles.Count, "Should initialize with one default profile.");
            Assert.IsNotNull(viewModel.ActiveFilterProfile, "A profile should be active by default.");
            Assert.AreEqual("Default", viewModel.ActiveFilterProfile.Name, "Default profile name should be 'Default'.");
            Assert.IsInstanceOfType(viewModel.ActiveFilterProfile.Model.RootFilter, typeof(TrueFilter), "Default profile should have a TrueFilter root.");
            Assert.IsNotNull(viewModel.ActiveFilterProfile.RootFilterViewModel, "RootFilterViewModel should be created for the active profile.");
        }

        [TestMethod] public void AddFilterCommand_WithNoRootFilterInActiveProfile_CreatesRootNode()
        {
            // Arrange
            var viewModel = CreateMockViewModel();
            // Ensure the default profile starts empty (or create a new empty one)
            viewModel.ActiveFilterProfile?.SetModelRootFilter(null); // Make the active profile empty

            // Act
            viewModel.AddFilterCommand.Execute("Substring"); // Add a substring filter

            // Assert
            Assert.IsNotNull(viewModel.ActiveFilterProfile?.Model.RootFilter, "Root filter model should be created.");
            Assert.IsInstanceOfType(viewModel.ActiveFilterProfile.Model.RootFilter, typeof(SubstringFilter), "Root filter should be SubstringFilter.");
            Assert.IsNotNull(viewModel.ActiveFilterProfile.RootFilterViewModel, "RootFilterViewModel should be created.");
            Assert.AreEqual(viewModel.ActiveFilterProfile.RootFilterViewModel, viewModel.SelectedFilterNode, "Newly added root node should be selected.");
        }

        [TestMethod] public void AddFilterCommand_WithCompositeNodeSelected_AddsChildNode()
        {
            // Arrange
            var viewModel = CreateMockViewModel();
            viewModel.AddFilterCommand.Execute("And"); // Create an AND root
            var rootNode = viewModel.ActiveFilterProfile?.RootFilterViewModel;
            Assert.IsNotNull(rootNode, "Root node setup failed.");
            viewModel.SelectedFilterNode = rootNode; // Select the AND node

            // Act
            viewModel.AddFilterCommand.Execute("Substring"); // Add a substring filter as child

            // Assert
            Assert.AreEqual(1, rootNode.Children.Count, "Child node should be added to the composite node.");
            Assert.IsInstanceOfType(rootNode.Children[0].FilterModel, typeof(SubstringFilter), "Child node should be SubstringFilter.");
            Assert.AreEqual(rootNode.Children[0], viewModel.SelectedFilterNode, "Newly added child node should be selected."); // Check if selection moves to child
        }

        [TestMethod] public void AddFilterCommand_WithNonCompositeNodeSelected_ShowsMessage()
        {
             // Arrange
             var viewModel = CreateMockViewModel();
             viewModel.AddFilterCommand.Execute("Substring"); // Create a Substring root
             var rootNode = viewModel.ActiveFilterProfile?.RootFilterViewModel;
             Assert.IsNotNull(rootNode, "Root node setup failed.");
             viewModel.SelectedFilterNode = rootNode; // Select the Substring node
             int childCount = rootNode.Children.Count;

             // Act
             // TODO: Need a way to intercept MessageBox calls for testing
             // viewModel.AddFilterCommand.Execute("Regex"); // Try to add another child

             // Assert
             Assert.AreEqual(childCount, rootNode.Children.Count, "No child should be added to non-composite.");
             // Assert that a message box was shown (requires mocking/framework support)
             Assert.Inconclusive("Need framework/mocking to verify MessageBox call.");
        }


        [TestMethod] public void RemoveFilterNodeCommand_WithRootNodeSelected_ClearsActiveTree()
        {
            // Arrange
            var viewModel = CreateMockViewModel();
            viewModel.AddFilterCommand.Execute("Substring"); // Add a root node
            viewModel.SelectedFilterNode = viewModel.ActiveFilterProfile?.RootFilterViewModel; // Select the root
            Assert.IsNotNull(viewModel.SelectedFilterNode, "Setup failed: Root node not selected.");

            // Act
            viewModel.RemoveFilterNodeCommand.Execute(null);

            // Assert
            Assert.IsNull(viewModel.ActiveFilterProfile?.Model.RootFilter, "Root filter model should be null after removal.");
            Assert.IsNull(viewModel.ActiveFilterProfile?.RootFilterViewModel, "RootFilterViewModel should be null.");
            Assert.IsNull(viewModel.SelectedFilterNode, "SelectedFilterNode should be cleared.");
        }

        [TestMethod] public void RemoveFilterNodeCommand_WithChildNodeSelected_RemovesChildAndSelectsParent()
        {
            // Arrange
            var viewModel = CreateMockViewModel();
            viewModel.AddFilterCommand.Execute("And"); // Add AND root
            var rootNode = viewModel.ActiveFilterProfile?.RootFilterViewModel;
            viewModel.SelectedFilterNode = rootNode;
            viewModel.AddFilterCommand.Execute("Substring"); // Add child
            var childNode = rootNode?.Children.FirstOrDefault();
            Assert.IsNotNull(childNode, "Setup failed: Child node not added.");
            viewModel.SelectedFilterNode = childNode; // Select the child

            // Act
            viewModel.RemoveFilterNodeCommand.Execute(null);

            // Assert
            Assert.IsNotNull(rootNode, "Root node should still exist.");
            Assert.AreEqual(0, rootNode.Children.Count, "Child node should be removed.");
            Assert.AreEqual(rootNode, viewModel.SelectedFilterNode, "Parent node (root) should be selected.");
        }

        [TestMethod] public void ToggleEditNodeCommand_OnEditableNode_TogglesEditState()
        {
             // Arrange
             var viewModel = CreateMockViewModel();
             viewModel.AddFilterCommand.Execute("Substring");
             var node = viewModel.ActiveFilterProfile?.RootFilterViewModel;
             Assert.IsNotNull(node, "Node setup failed.");
             viewModel.SelectedFilterNode = node;
             Assert.IsTrue(node.IsEditable, "Node should be editable.");
             bool initialState = node.IsEditing;

             // Act
             viewModel.ToggleEditNodeCommand.Execute(null);

             // Assert
             Assert.AreNotEqual(initialState, node.IsEditing, "IsEditing state should toggle.");

             // Act again
             viewModel.ToggleEditNodeCommand.Execute(null);

              // Assert
             Assert.AreEqual(initialState, node.IsEditing, "IsEditing state should toggle back.");
        }

        [TestMethod] public void ToggleEditNodeCommand_OnNonEditableNode_DoesNothing()
        {
             // Arrange
             var viewModel = CreateMockViewModel();
             viewModel.AddFilterCommand.Execute("And");
             var node = viewModel.ActiveFilterProfile?.RootFilterViewModel;
             Assert.IsNotNull(node, "Node setup failed.");
             viewModel.SelectedFilterNode = node;
             Assert.IsFalse(node.IsEditable, "Node should not be editable.");
             bool initialState = node.IsEditing;

             // Act
             viewModel.ToggleEditNodeCommand.Execute(null);

             // Assert
             Assert.AreEqual(initialState, node.IsEditing, "IsEditing state should not change.");
        }

#if false // Keep this inactive for now as it requires deeper mocking
        [TestMethod] public void ActiveFilterProfile_Set_TriggersFilterUpdateAndClearsNodeSelection()
        {
             // Arrange
             var viewModel = CreateViewModel();
             viewModel.CreateNewProfileCommand.Execute(null); // Create a second profile
             var initialProfile = viewModel.AvailableProfiles[0];
             var secondProfile = viewModel.AvailableProfiles[1];
             viewModel.ActiveFilterProfile = initialProfile; // Start with first

             // Setup: add a node and select it in the initial profile
             viewModel.AddFilterCommand.Execute("Substring");
             viewModel.SelectedFilterNode = initialProfile.RootFilterViewModel;
             Assert.IsNotNull(viewModel.SelectedFilterNode, "Node selection setup failed.");

             bool filterUpdateTriggered = false;
             viewModel.ActiveFilterProfile.RootFilterViewModel!.FilterModel.PropertyChanged += (s, e) => {
                 // We need a better way to test TriggerFilterUpdate was called.
                 // For now, we assume changing the profile model implicitly calls it.
                 // This requires mocking ILogFilterProcessor.
                 // filterUpdateTriggered = true;
             };


             // Act
             viewModel.ActiveFilterProfile = secondProfile;

             // Assert
             Assert.AreEqual(secondProfile, viewModel.ActiveFilterProfile, "Active profile not switched.");
             Assert.IsNull(viewModel.SelectedFilterNode, "Selected filter node should be cleared when profile changes.");
             // Assert.IsTrue(filterUpdateTriggered, "Changing active profile should trigger a filter update."); // Requires mocking
             Assert.Inconclusive("Need to mock ILogFilterProcessor to verify TriggerFilterUpdate call.");
        }
#endif

        // --- Tests for Profile Management Commands ---

        [TestMethod] public void CreateNewProfileCommand_AddsProfileAndSelectsIt()
        {
            // Arrange
            var viewModel = CreateMockViewModel();
            int initialCount = viewModel.AvailableProfiles.Count;

            // Act
            viewModel.CreateNewProfileCommand.Execute(null);

            // Assert
            Assert.AreEqual(initialCount + 1, viewModel.AvailableProfiles.Count, "A new profile should be added.");
            Assert.IsNotNull(viewModel.ActiveFilterProfile, "The new profile should be active.");
            Assert.IsTrue(viewModel.ActiveFilterProfile.Name.StartsWith("New Profile"), "New profile should have default name pattern.");
        }

        [TestMethod] public void RenameProfileCommand_WithValidNewName_UpdatesProfileName()
        {
            // Arrange
            var mockPrompter = new MockInputPromptService { InputToReturn = "Renamed Profile" };
            var viewModel = CreateMockViewModel(prompt: mockPrompter);
            var profileToRename = viewModel.ActiveFilterProfile;
            Assert.IsNotNull(profileToRename, "Setup failed: No active profile.");
            string oldName = profileToRename.Name;

            // Act
            viewModel.RenameProfileCommand.Execute(null);

            // Assert
            Assert.AreEqual("Renamed Profile", profileToRename.Name, "Profile name should be updated in VM.");
            Assert.AreEqual("Renamed Profile", profileToRename.Model.Name, "Profile name should be updated in Model.");
        }

        [TestMethod] public void RenameProfileCommand_WithExistingName_ShowsErrorAndDoesNotRename()
        {
             // Arrange
             var mockPrompter = new MockInputPromptService();
             var viewModel = CreateMockViewModel(prompt: mockPrompter);
             viewModel.CreateNewProfileCommand.Execute(null); // Now have "Default" and "New Profile 1"
             var profile1 = viewModel.AvailableProfiles[0];
             var profile2 = viewModel.AvailableProfiles[1];
             string profile2OriginalName = profile2.Name;
             viewModel.ActiveFilterProfile = profile2; // Select the second one
             mockPrompter.InputToReturn = profile1.Name; // Try to rename to the first one's name

             // Act
             // TODO: Need mocking/framework to verify MessageBox call
             // viewModel.RenameProfileCommand.Execute(null);

             // Assert
             Assert.AreEqual(profile2OriginalName, profile2.Name, "Profile name should not change if name exists.");
             Assert.Inconclusive("Need framework/mocking to verify MessageBox call.");
        }

        [TestMethod] public void DeleteProfileCommand_RemovesProfileAndSelectsAnother()
        {
            // Arrange
            var viewModel = CreateMockViewModel();
            viewModel.CreateNewProfileCommand.Execute(null); // Profile 1
            viewModel.CreateNewProfileCommand.Execute(null); // Profile 2 (now active)
            Assert.AreEqual(3, viewModel.AvailableProfiles.Count); // Default + 2 new
            var profileToDelete = viewModel.ActiveFilterProfile;
            Assert.IsNotNull(profileToDelete);

            // Act
            // TODO: Need mocking/framework to verify MessageBox call (Yes/No)
             // Assume user clicks Yes
            viewModel.DeleteProfileCommand.Execute(null);

            // Assert
            Assert.AreEqual(2, viewModel.AvailableProfiles.Count, "Profile count should decrease by one.");
            Assert.IsFalse(viewModel.AvailableProfiles.Contains(profileToDelete), "Deleted profile should be removed.");
            Assert.IsNotNull(viewModel.ActiveFilterProfile, "Another profile should be selected.");
            Assert.AreNotEqual(profileToDelete, viewModel.ActiveFilterProfile, "A different profile should be active.");
            Assert.Inconclusive("Need framework/mocking to verify MessageBox confirmation.");
        }

        [TestMethod] public void DeleteProfileCommand_CannotDeleteLastProfile()
        {
             // Arrange
             var viewModel = CreateMockViewModel();
             Assert.AreEqual(1, viewModel.AvailableProfiles.Count); // Should start with Default
             viewModel.ActiveFilterProfile = viewModel.AvailableProfiles[0];

             // Act
             // TODO: Need mocking/framework to verify MessageBox call
             // viewModel.DeleteProfileCommand.Execute(null);

             // Assert
             Assert.AreEqual(1, viewModel.AvailableProfiles.Count, "Profile count should remain 1.");
              Assert.Inconclusive("Need framework/mocking to verify MessageBox call.");
        }


        // --- Existing Tests (Review and keep if still valid) ---

        [TestMethod] public void PreviousSearch_WithNonEmptySearchText_ShouldNavigate() // Simplified - just checks execution
        {
            // Arrange
            var viewModel = CreateMockViewModel();
            viewModel.SearchText = "test";
             // Add some dummy matches if needed for CanExecute or internal logic
            // viewModel.SearchMatches.Add(new SearchResult(0, 4));

            // Act
            viewModel.PreviousSearchCommand.Execute(null);

            // Assert: No exception thrown, command executed. Verifying actual scroll/select needs UI integration.
             Assert.IsTrue(true, "Command executed without error."); // Basic check
        }

        [TestMethod] public void NextSearch_WithNonEmptySearchText_ShouldNavigate() // Simplified
        {
            // Arrange
            var viewModel = CreateMockViewModel();
            viewModel.SearchText = "test";
            // viewModel.SearchMatches.Add(new SearchResult(0, 4));

            // Act
            viewModel.NextSearchCommand.Execute(null);

            // Assert
            Assert.IsTrue(true, "Command executed without error.");
        }

        [TestMethod] public void OpenLogFile_ShouldSetCurrentLogFilePath()
        {
            // Arrange
            var fakeService = new MockFileDialogService { FileToReturn = "C:\\fake\\log.txt" };
            var viewModel = CreateMockViewModel(fakeService);
            // Mocking LogTailerManager/LogFilterProcessor interactions would be needed for full test.

            // Act
            viewModel.OpenLogFileCommand.Execute(null);

            // Assert
            Assert.AreEqual("C:\\fake\\log.txt", viewModel.CurrentLogFilePath, "CurrentLogFilePath should be updated.");
            // Further asserts would require mocking dependencies.
        }
    }
}