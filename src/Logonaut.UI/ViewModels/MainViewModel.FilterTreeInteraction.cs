using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Filters;
using Logonaut.UI.Commands;

namespace Logonaut.UI.ViewModels;

// Groups interactions specifically related to building and manipulating the filter rule tree for the active profile
public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{

    // The filter node currently selected within the TreeView of the ActiveFilterProfile.
    [NotifyCanExecuteChangedFor(nameof(AddFilterCommand))] // Enable adding if composite selected
    [NotifyCanExecuteChangedFor(nameof(RemoveFilterNodeCommand))] // Enable removing selected node
    [NotifyCanExecuteChangedFor(nameof(ToggleEditNodeCommand))] // Enable editing selected node
    [ObservableProperty] private FilterViewModel? _selectedFilterNode;

    // Collection exposed specifically for the TreeView's ItemsSource.
    // Contains only the root node of the ActiveFilterProfile's tree.
    public ObservableCollection<FilterViewModel> ActiveTreeRootNodes { get; } = new();

    // Collection of filter patterns (substrings/regex) for highlighting.
    // Note: This state is derived by traversing the *active* FilterProfile.
    [ObservableProperty] private ObservableCollection<IFilter> _filterHighlightModels = new();

    private void UpdateActiveTreeRootNodes(FilterProfileViewModel? activeProfile)
    {
        ActiveTreeRootNodes.Clear();
        if (activeProfile?.RootFilterViewModel != null)
            ActiveTreeRootNodes.Add(activeProfile.RootFilterViewModel);
        OnPropertyChanged(nameof(ActiveTreeRootNodes)); // Keep for safety, though collection changes might suffice
    }

    private bool CanAddFilterNode()
    {
        if (ActiveFilterProfile == null) return false;
        bool isTreeEmpty = ActiveFilterProfile.RootFilterViewModel == null;
        bool isCompositeNodeSelected = SelectedFilterNode != null && SelectedFilterNode.Filter is CompositeFilter;
        // Allow adding if tree is empty OR composite selected OR root is composite (allows adding to root when nothing/leaf selected)
        bool isRootComposite = ActiveFilterProfile.RootFilterViewModel?.Filter is CompositeFilter;
        return isTreeEmpty || isCompositeNodeSelected || (ActiveFilterProfile.RootFilterViewModel != null && isRootComposite);
    }

    // Combined Add Filter command - type determined by parameter
    // TODO: This will be replaced by the DnD functionality in the future.
    [RelayCommand(CanExecute = nameof(CanAddFilterNode))]
    private void AddFilter(object? filterTypeParam) // Parameter likely string like "Substring", "And", etc.
    {
        if (ActiveFilterProfile == null) throw new InvalidOperationException("No active profile");

        IFilter newFilterNodeModel = CreateFilterModelFromType(filterTypeParam as string ?? string.Empty);
        FilterViewModel? targetParentVM = null;
        int? targetIndex = null; // Use null to add at the end by default

        // Case 1: Active profile's tree is currently empty
        if (ActiveFilterProfile.RootFilterViewModel == null)
        {
            // Cannot add to null root via AddFilterAction directly.
            // We need to set the root first, which isn't easily undoable in the current structure.
            // Let's handle setting the root OUTSIDE the undo system for now, or create a dedicated SetRootFilterAction.
            // Simple approach: Set root directly, bypass Undo for this specific case.
            ActiveFilterProfile.SetModelRootFilter(newFilterNodeModel);
            UpdateActiveTreeRootNodes(ActiveFilterProfile); // Update TreeView source
            SelectedFilterNode = ActiveFilterProfile.RootFilterViewModel; // Select the new root
            TriggerFilterUpdate(); // Explicitly trigger updates since ExecuteAction wasn't called
            SaveCurrentSettingsDelayed();
            Debug.WriteLine("AddFilter: Set new root node (outside Undo system).");
            if (SelectedFilterNode != null && SelectedFilterNode.IsEditable)
                SelectedFilterNode.BeginEditCommand.Execute(null);
            return;
        }
        // Case 2: A composite node is selected - add as child
        else if (SelectedFilterNode != null && SelectedFilterNode.Filter is CompositeFilter)
        {
            targetParentVM = SelectedFilterNode;
            targetIndex = targetParentVM.Children.Count; // Add at end
        }
        // Case 3: No node selected (but tree exists), or non-composite selected - Add to the root if it's composite
        else if (ActiveFilterProfile.RootFilterViewModel != null && ActiveFilterProfile.RootFilterViewModel.Filter is CompositeFilter)
        {
            targetParentVM = ActiveFilterProfile.RootFilterViewModel;
            targetIndex = targetParentVM.Children.Count; // Add at end of root
            Debug.WriteLine("AddFilter: No selection or non-composite selected, adding to root.");
        }
        else // Root exists but isn't composite, and nothing valid is selected
        {
            Debug.WriteLine("AddFilter: Cannot add filter. Select a composite node or ensure root is composite.");
            // Optionally show a message to the user
            // MessageBox.Show("Please select a composite filter node (AND, OR, NOR) to add a child to.", "Add Filter", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (targetParentVM == null)
            throw new InvalidOperationException("Failed to determine a valid parent for the new filter node.");

        var action = new AddFilterAction(targetParentVM, newFilterNodeModel, targetIndex);
        Execute(action); // Use the ICommandExecutor method

        // Try to select the newly added node AFTER execution
        // AddFilterAction's Execute should have added the VM
        var addedVM = targetParentVM.Children.LastOrDefault(vm => vm.Filter == newFilterNodeModel);
        if (addedVM is null)
            throw new InvalidOperationException("Failed to find the newly added VM in the parent's children.");

        targetParentVM.IsExpanded = true;
        SelectedFilterNode = addedVM; // Update selection

        // Editing logic: If the new node is editable, start editing
        if (SelectedFilterNode.IsEditable)
        {
            // Execute BeginEdit directly on the selected VM
            SelectedFilterNode.BeginEditCommand.Execute(null);
        }
    }

    private bool CanRemoveFilterNode() => SelectedFilterNode != null && ActiveFilterProfile != null;
    [RelayCommand(CanExecute = nameof(CanRemoveFilterNode))]
    private void RemoveFilterNode()
    {
        if (SelectedFilterNode == null || ActiveFilterProfile?.RootFilterViewModel == null) return;

        FilterViewModel nodeToRemove = SelectedFilterNode;
        FilterViewModel? parent = nodeToRemove.Parent;

        // Case 1: Removing the root node
        if (nodeToRemove == ActiveFilterProfile.RootFilterViewModel)
        {
            // This action is hard to undo cleanly with the current structure.
            // Bypass Undo system for root removal for now.
            ActiveFilterProfile.SetModelRootFilter(null);
            UpdateActiveTreeRootNodes(ActiveFilterProfile);
            SelectedFilterNode = null;
            TriggerFilterUpdate();
            SaveCurrentSettingsDelayed();
            Debug.WriteLine("RemoveFilterNode: Removed root node (outside Undo system).");
        }
        // Case 2: Removing a child node
        else if (parent != null)
        {
            var action = new RemoveFilterAction(parent, nodeToRemove);
            Execute(action); // Use the ICommandExecutor method
            SelectedFilterNode = parent; // Select the parent after removal
        }
    }

    private bool CanToggleEditNode() => SelectedFilterNode?.IsEditable ?? false;

    [RelayCommand(CanExecute = nameof(CanToggleEditNode))]
    private void ToggleEditNode()
    {
        if (SelectedFilterNode?.IsEditable ?? false)
        {
            if (SelectedFilterNode.IsNotEditing)
            {
                SelectedFilterNode.BeginEditCommand.Execute(null);
            }
            else
            {
                SelectedFilterNode.EndEditCommand.Execute(null);
                // ExecuteAction called within EndEdit will handle save/update
            }
        }
    }

    // Central method to handle adding a new filter node, typically initiated by a Drag-and-Drop operation.
    // This method determines the correct placement (root or child) and uses the Undo/Redo system.
    public void ExecuteAddFilterFromDrop(string filterTypeIdentifier, FilterViewModel? targetParentInDrop, int? dropIndexInTarget, string? initialValue = null)
    {
        IFilter newFilterNodeModel = CreateFilterModelFromType(filterTypeIdentifier, initialValue);

        FilterViewModel? actualTargetParentVM = targetParentInDrop;
        int? actualDropIndex = dropIndexInTarget;

        if (ActiveFilterProfile == null) throw new InvalidOperationException("No active profile for drop.");

        if (actualTargetParentVM == null) // Dropped on empty space in TreeView, not on a specific item
        {
            if (ActiveFilterProfile.RootFilterViewModel == null) // Case 1: Tree is empty, set as root
            {
                ActiveFilterProfile.SetModelRootFilter(newFilterNodeModel); // This updates the Model
                ActiveFilterProfile.RefreshRootViewModel(); // This creates the VM for the new root
                UpdateActiveTreeRootNodes(ActiveFilterProfile); // This updates the collection bound to the TreeView
                SelectedFilterNode = ActiveFilterProfile.RootFilterViewModel;
                // No "Action" executed for undo stack in this specific case (matches old button logic for setting initial root)
                TriggerFilterUpdate();
                SaveCurrentSettingsDelayed();
                Debug.WriteLine("ExecuteAddFilterFromDrop: Set new root node (outside Undo system).");
                if (SelectedFilterNode != null && SelectedFilterNode.IsEditable)
                {
                    SelectedFilterNode.BeginEditCommand.Execute(null);
                }
                return; // Action complete for setting root
            }
            else if (ActiveFilterProfile.RootFilterViewModel.Filter is CompositeFilter) // Case 2: Tree not empty, root is composite, add to root
            {
                actualTargetParentVM = ActiveFilterProfile.RootFilterViewModel;
                actualDropIndex = actualTargetParentVM.Children.Count; // Add to end of root
            }
            else // Case 3: Tree not empty, root is not composite - invalid drop for adding to root
            {
                Debug.WriteLine("ExecuteAddFilterFromDrop: Cannot add. Root exists but is not composite, and drop was not on a composite item.");
                return;
            }
        }
        // If actualTargetParentVM was provided from drop, it should have already been validated as composite by the drop handler.

        if (actualTargetParentVM == null || !(actualTargetParentVM.Filter is CompositeFilter))
        {
            Debug.WriteLine("ExecuteAddFilterFromDrop: No valid composite parent found for adding the filter.");
            return;
        }

        int finalIndex = actualDropIndex ?? actualTargetParentVM.Children.Count;

        var action = new AddFilterAction(actualTargetParentVM, newFilterNodeModel, finalIndex);
        Execute(action); // Use the ICommandExecutor method (adds to undo stack, triggers save/update)

        // Try to find the added VM more robustly
        var addedVM = actualTargetParentVM.Children.FirstOrDefault(vm => vm.Filter == newFilterNodeModel);
        if (addedVM == null && finalIndex < actualTargetParentVM.Children.Count) // Check specific index if not last
        {
            addedVM = actualTargetParentVM.Children[finalIndex];
            if (addedVM.Filter != newFilterNodeModel) addedVM = null; // double check it's the one we added
        }
        // Fallback to LastOrDefault if specific index check failed or it was added at the end
        if (addedVM == null) addedVM = actualTargetParentVM.Children.LastOrDefault(vm => vm.Filter == newFilterNodeModel);


        if (addedVM != null)
        {
            actualTargetParentVM.IsExpanded = true;
            SelectedFilterNode = addedVM;

            if (SelectedFilterNode.IsEditable)
            {
                SelectedFilterNode.BeginEditCommand.Execute(null);
            }
        }
        else
        {
            throw new InvalidOperationException("Failed to find the newly added VM in the parent's children.");
            // This might happen if the filter type already existed and was somehow merged, or an error in AddFilterAction.
            // For now, just log. If it becomes a persistent issue, further investigation into AddFilterAction's interaction with collections is needed.
        }
    }

    // Helper method to create an IFilter model instance from a type identifier string.
    private IFilter CreateFilterModelFromType(string typeIdentifier, string? initialValue = null)
    {
        return typeIdentifier switch
        {
            "SubstringType" => new SubstringFilter(initialValue ?? ""),
            "RegexType" => new RegexFilter(initialValue ?? ".*"),
            "AndType" => new AndFilter(),
            "OrType" => new OrFilter(),
            "NorType" => new NorFilter(),
            // "TRUE" filter type isn't typically added from a palette.
            _ => throw new ArgumentException($"Unknown filter type identifier: {typeIdentifier} in CreateFilterModelFromType"),
        };
    }

    private void UpdateFilterSubstrings() // Triggered by TriggerFilterUpdate
    {
        var newFilterModels = new ObservableCollection<IFilter>();
        if (ActiveFilterProfile?.RootFilterViewModel != null)
        {
            // Collect IFilter models instead of strings
            TraverseFilterTreeForHighlighting(ActiveFilterProfile.RootFilterViewModel, newFilterModels);
        }
        FilterHighlightModels = newFilterModels; // Update the renamed property
    }

    // --- Highlighting Configuration ---
    private void TraverseFilterTreeForHighlighting(FilterViewModel filterViewModel, ObservableCollection<IFilter> models)
    {
        if (!filterViewModel.Enabled) return;

        // Add the IFilter model itself if it's a SubstringFilter or RegexFilter and has a value
        if ((filterViewModel.Filter is SubstringFilter || filterViewModel.Filter is RegexFilter) &&
            !string.IsNullOrEmpty(filterViewModel.Filter.Value))
        {
            // Add the model, not just its value
            if (!models.Contains(filterViewModel.Filter)) // Avoid duplicates if same filter instance appears multiple times (unlikely with tree)
            {
                models.Add(filterViewModel.Filter);
            }
        }
        // Recursively traverse children
        foreach (var childFilterVM in filterViewModel.Children)
        {
            TraverseFilterTreeForHighlighting(childFilterVM, models);
        }
    }

    private void UpdateActiveFilterMatchingStatus()
    {
        if (ActiveFilterProfile?.RootFilterViewModel == null) return;

        var directMatchTexts = FilteredLogLines
            .Where(fl => !fl.IsContextLine)
            .Select(fl => fl.Text)
            .ToList(); // Materialize for multiple iterations

        UpdateMatchingStatusInternal(ActiveFilterProfile.RootFilterViewModel, directMatchTexts);
    }

    private void UpdateMatchingStatusInternal(FilterViewModel fvm, List<string> directMatchTexts)
    {
        bool isContributing = false;
        if (fvm.Enabled) // Only consider enabled filters
        {
            foreach (var text in directMatchTexts)
            {
                if (fvm.Filter.IsMatch(text))
                {
                    isContributing = true;
                    break;
                }
            }
        }
        fvm.IsActivelyMatching = isContributing;

        foreach (var child in fvm.Children)
        {
            UpdateMatchingStatusInternal(child, directMatchTexts);
        }
    }

    private void ClearActiveFilterMatchingStatusRecursive(FilterViewModel fvm)
    {
        fvm.IsActivelyMatching = false;
        foreach (var child in fvm.Children)
        {
            ClearActiveFilterMatchingStatusRecursive(child);
        }
    }
}
