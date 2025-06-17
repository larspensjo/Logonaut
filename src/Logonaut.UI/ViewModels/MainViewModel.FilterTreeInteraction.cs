using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Filters;
using Logonaut.Core.Commands;
using Logonaut.UI.Descriptors;
using Logonaut.UI.Commands;

namespace Logonaut.UI.ViewModels;

/*
 * Partial class for MainViewModel responsible for managing interactions
 * with the filter rule tree of the active filter profile. This includes
 * adding, removing, and editing filter nodes, and handling drag-and-drop operations.
 */
public partial class MainViewModel : ObservableObject, IDisposable, ICommandExecutor
{
    public ObservableCollection<PaletteItemDescriptor> FilterPaletteItems { get; } = new();
    public PaletteItemDescriptor? InitializedSubstringPaletteItem { get; private set; }

    [ObservableProperty] private string? _selectedLogTextForFilter;

    [NotifyCanExecuteChangedFor(nameof(AddFilterCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveFilterNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleEditNodeCommand))]
    [ObservableProperty] private FilterViewModel? _selectedFilterNode;

    // Collection exposed specifically for the TreeView's ItemsSource.
    // Contains only the root node of the ActiveFilterProfile's tree.
    public ObservableCollection<FilterViewModel> ActiveTreeRootNodes { get; } = new();

    partial void OnSelectedLogTextForFilterChanged(string? oldValue, string? newValue)
    {
        if (InitializedSubstringPaletteItem is null)
            throw new InvalidOperationException("InitializedSubstringPaletteItem is not initialized.");
        if (string.IsNullOrEmpty(newValue))
        {
            InitializedSubstringPaletteItem.IsEnabled = false;
            InitializedSubstringPaletteItem.DisplayName = "<Selection>";
            InitializedSubstringPaletteItem.InitialValue = null;
        }
        else
        {
            InitializedSubstringPaletteItem.InitialValue = newValue;
            string displayText = newValue.Length > MaxPaletteDisplayTextLength ? newValue.Substring(0, MaxPaletteDisplayTextLength - 3) + "..." : newValue;
            InitializedSubstringPaletteItem.DisplayName = $"Substring: \"{displayText}\"";
            InitializedSubstringPaletteItem.IsEnabled = true;
        }
    }

    private void UpdateActiveTreeRootNodes(FilterProfileViewModel? activeProfile)
    {
        ActiveTreeRootNodes.Clear();
        if (activeProfile?.RootFilterViewModel != null)
            ActiveTreeRootNodes.Add(activeProfile.RootFilterViewModel);
        OnPropertyChanged(nameof(ActiveTreeRootNodes));
    }

    private bool CanAddFilterNode()
    {
        if (ActiveFilterProfile == null) return false;
        bool isTreeEmpty = ActiveFilterProfile.RootFilterViewModel == null;
        bool isCompositeNodeSelected = SelectedFilterNode != null && SelectedFilterNode.Filter is CompositeFilter;
        bool isRootComposite = ActiveFilterProfile.RootFilterViewModel?.Filter is CompositeFilter;
        return isTreeEmpty || isCompositeNodeSelected || (ActiveFilterProfile.RootFilterViewModel != null && isRootComposite);
    }

    [RelayCommand(CanExecute = nameof(CanAddFilterNode))]
    private void AddFilter(object? filterTypeParam)
    {
        if (ActiveFilterProfile == null) throw new InvalidOperationException("No active profile");
        IFilter newFilterNodeModel = CreateFilterModelFromType(filterTypeParam as string ?? string.Empty);
        FilterViewModel? targetParentVM = null;
        int? targetIndex = null;

        if (ActiveFilterProfile.RootFilterViewModel == null)
        {
            ActiveFilterProfile.SetModelRootFilter(newFilterNodeModel);
            UpdateActiveTreeRootNodes(ActiveFilterProfile);
            SelectedFilterNode = ActiveFilterProfile.RootFilterViewModel;
            TriggerFilterUpdate(); 
            MarkSettingsAsDirty(); // Settings changed
            Debug.WriteLine("AddFilter: Set new root node (outside Undo system).");
            if (SelectedFilterNode != null && SelectedFilterNode.IsEditable)
                SelectedFilterNode.BeginEditCommand.Execute(null);
            return;
        }
        else if (SelectedFilterNode != null && SelectedFilterNode.Filter is CompositeFilter)
        {
            targetParentVM = SelectedFilterNode;
            targetIndex = targetParentVM.Children.Count;
        }
        else if (ActiveFilterProfile.RootFilterViewModel != null && ActiveFilterProfile.RootFilterViewModel.Filter is CompositeFilter)
        {
            targetParentVM = ActiveFilterProfile.RootFilterViewModel;
            targetIndex = targetParentVM.Children.Count;
        }
        else { Debug.WriteLine("AddFilter: Cannot add filter. Select a composite node or ensure root is composite."); return; }

        if (targetParentVM == null) throw new InvalidOperationException("Failed to determine a valid parent for the new filter node.");
        var action = new AddFilterAction(targetParentVM, newFilterNodeModel, targetIndex);
        Execute(action);
        var addedVM = targetParentVM.Children.LastOrDefault(vm => vm.Filter == newFilterNodeModel);
        if (addedVM is null) throw new InvalidOperationException("Failed to find the newly added VM in the parent's children.");
        targetParentVM.IsExpanded = true;
        SelectedFilterNode = addedVM;
        if (SelectedFilterNode.IsEditable) SelectedFilterNode.BeginEditCommand.Execute(null);
    }

    private bool CanRemoveFilterNode() => SelectedFilterNode != null && ActiveFilterProfile != null;
    [RelayCommand(CanExecute = nameof(CanRemoveFilterNode))]
    private void RemoveFilterNode()
    {
        if (SelectedFilterNode == null || ActiveFilterProfile?.RootFilterViewModel == null) return;
        FilterViewModel nodeToRemove = SelectedFilterNode;
        FilterViewModel? parent = nodeToRemove.Parent;
        if (nodeToRemove == ActiveFilterProfile.RootFilterViewModel)
        {
            ActiveFilterProfile.SetModelRootFilter(null);
            UpdateActiveTreeRootNodes(ActiveFilterProfile);
            SelectedFilterNode = null;
            TriggerFilterUpdate();
            MarkSettingsAsDirty();
            Debug.WriteLine("RemoveFilterNode: Removed root node (outside Undo system).");
        }
        else if (parent != null)
        {
            var action = new RemoveFilterAction(parent, nodeToRemove);
            Execute(action);
            SelectedFilterNode = parent;
        }
    }

    private bool CanToggleEditNode() => SelectedFilterNode?.IsEditable ?? false;
    [RelayCommand(CanExecute = nameof(CanToggleEditNode))]
    private void ToggleEditNode()
    {
        if (SelectedFilterNode?.IsEditable ?? false)
        {
            if (SelectedFilterNode.IsNotEditing)
                SelectedFilterNode.BeginEditCommand.Execute(null);
            else
                SelectedFilterNode.EndEditCommand.Execute(null);
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

        if (actualTargetParentVM == null)
        {
            if (ActiveFilterProfile.RootFilterViewModel == null)
            {
                ActiveFilterProfile.SetModelRootFilter(newFilterNodeModel);
                ActiveFilterProfile.RefreshRootViewModel();
                UpdateActiveTreeRootNodes(ActiveFilterProfile);
                SelectedFilterNode = ActiveFilterProfile.RootFilterViewModel;
                TriggerFilterUpdate();
                MarkSettingsAsDirty(); // Settings changed
                Debug.WriteLine("ExecuteAddFilterFromDrop: Set new root node (outside Undo system).");
                if (SelectedFilterNode != null && SelectedFilterNode.IsEditable)
                    SelectedFilterNode.BeginEditCommand.Execute(null);
                return;
            }
            else if (ActiveFilterProfile.RootFilterViewModel.Filter is CompositeFilter)
            {
                actualTargetParentVM = ActiveFilterProfile.RootFilterViewModel;
                actualDropIndex = actualTargetParentVM.Children.Count;
            }
            else
            {
                Debug.WriteLine("ExecuteAddFilterFromDrop: Cannot add. Root exists but is not composite, and drop was not on a composite item.");
                return;
            }
        }
        if (actualTargetParentVM == null || !(actualTargetParentVM.Filter is CompositeFilter))
        {
            Debug.WriteLine("ExecuteAddFilterFromDrop: No valid composite parent found for adding the filter.");
            return;
        }

        int finalIndex = actualDropIndex ?? actualTargetParentVM.Children.Count;
        var action = new AddFilterAction(actualTargetParentVM, newFilterNodeModel, finalIndex);
        Execute(action);
        var addedVM = actualTargetParentVM.Children.FirstOrDefault(vm => vm.Filter == newFilterNodeModel) ??
                      (finalIndex < actualTargetParentVM.Children.Count && actualTargetParentVM.Children[finalIndex].Filter == newFilterNodeModel ? actualTargetParentVM.Children[finalIndex] : null) ??
                      actualTargetParentVM.Children.LastOrDefault(vm => vm.Filter == newFilterNodeModel);
        if (addedVM != null)
        {
            actualTargetParentVM.IsExpanded = true;
            SelectedFilterNode = addedVM;
            if (SelectedFilterNode.IsEditable)
                SelectedFilterNode.BeginEditCommand.Execute(null);
        }
        else
        {
            throw new InvalidOperationException("Failed to find the newly added VM in the parent's children after drop.");
        }
    }

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

    private void TraverseFilterTreeForHighlighting(FilterViewModel filterViewModel, ObservableCollection<IFilter> models)
    {
        if (!filterViewModel.Enabled) return;
        if ((filterViewModel.Filter is SubstringFilter || filterViewModel.Filter is RegexFilter) &&
            !string.IsNullOrEmpty(filterViewModel.Filter.Value))
        {
            if (!models.Contains(filterViewModel.Filter)) models.Add(filterViewModel.Filter);
        }
        foreach (var childFilterVM in filterViewModel.Children)
            TraverseFilterTreeForHighlighting(childFilterVM, models);
    }

    private void UpdateActiveFilterMatchingStatus()
    {
        if (ActiveFilterProfile?.RootFilterViewModel == null) return;
        // Get matching texts from the _internalTabViewModel
        var directMatchTexts = _internalTabViewModel.FilteredLogLines
            .Where(fl => !fl.IsContextLine)
            .Select(fl => fl.Text)
            .ToList();
        UpdateMatchingStatusInternal(ActiveFilterProfile.RootFilterViewModel, directMatchTexts);
    }

    private void UpdateMatchingStatusInternal(FilterViewModel fvm, List<string> directMatchTexts)
    {
        bool isContributing = false;
        if (fvm.Enabled)
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

    private void PopulateFilterPalette()
    {
        InitializedSubstringPaletteItem = new PaletteItemDescriptor("<Selection>", "SubstringType", isDynamic: true);
        FilterPaletteItems.Add(InitializedSubstringPaletteItem);
        FilterPaletteItems.Add(new PaletteItemDescriptor("Substring: \"\"", "SubstringType"));
        FilterPaletteItems.Add(new PaletteItemDescriptor("Regex", "RegexType"));
        FilterPaletteItems.Add(new PaletteItemDescriptor("AND Group", "AndType"));
        FilterPaletteItems.Add(new PaletteItemDescriptor("OR Group", "OrType"));
        FilterPaletteItems.Add(new PaletteItemDescriptor("NOR Group", "NorType"));
    }
}
