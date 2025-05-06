using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Filters;
using Logonaut.UI.Commands;
using System.Diagnostics;

namespace Logonaut.UI.ViewModels;

/*
 * ViewModel wrapper for an IFilter node, representing it in the filter tree UI.
 *
 * Purpose:
 * Provides UI-specific state (selection, editing, expansion) and data binding
 * for an individual filter rule (IFilter model). Facilitates user interaction
 * with the filter tree.
 *
 * Role:
 * Acts as the bridge between the filter rule model (IFilter) and the View
 * (TreeView). Exposes filter properties and UI state for binding. Manages child
 * ViewModels for composite filters. Initiates undoable changes via ICommandExecutor.
 *
 * Benefits:
 * Decouples View from Model, enables data binding, supports hierarchy, integrates
 * with Undo/Redo.
 */
public partial class FilterViewModel : ObservableObject, ICommandExecutorProvider
{
    public IFilter Filter { get; }
    public FilterViewModel? Parent { get; }
    public ObservableCollection<FilterViewModel> Children { get; } = new();

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isEditing = false;
    [ObservableProperty] private bool _isNotEditing = true;

    [ObservableProperty] private bool _isExpanded; // Relevant for CompositeFilters

    public bool IsEditable => Filter.IsEditable;

    // Store the executor
    private readonly ICommandExecutor _commandExecutor;
    public ICommandExecutor CommandExecutor => _commandExecutor;

    // Store original value for ChangeFilterValueAction
    private string? _valueBeforeEdit;

    public FilterViewModel(IFilter filter, ICommandExecutor commandExecutor, FilterViewModel? parent = null)
    {
        Filter = filter;
        Parent = parent;
        _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor)); // Store executor

        if (filter is CompositeFilter composite)
        {
            foreach (var child in composite.SubFilters)
            {
                // Pass the executor down when creating children
                Children.Add(new FilterViewModel(child, commandExecutor, this));
            }
        }
    }

    [RelayCommand]
    public void AddChildFilter(IFilter childFilter)
    {
        if (Filter is CompositeFilter composite)
        {
            // Determine insert index (e.g., end)
            int index = Children.Count;
            // Create and execute the action via the command executor
            var action = new AddFilterAction(this, childFilter, index);
            _commandExecutor.Execute(action);
             // Selection/Expansion logic might need slight adjustment if ExecuteAction doesn't return the new VM
             // Find the VM after execution:
             var addedVM = Children.LastOrDefault(vm => vm.Filter == childFilter);
             if (addedVM != null) {
                 IsExpanded = true;
                 // Need a way to set MainViewModel.SelectedFilterNode perhaps? Or let MainViewModel handle selection.
             }
        }
    }

    public void RemoveChild(FilterViewModel child)
    {
        if (Filter is not CompositeFilter)
            throw new InvalidOperationException("Cannot remove child from a non-composite filter.");
        var action = new RemoveFilterAction(this, child);
        _commandExecutor.Execute(action);
    }


    public bool Enabled
    {
        get => Filter.Enabled;
        set
        {
            if (Filter.Enabled != value)
            {
                // Create and execute the command instead of setting directly
                var action = new ToggleFilterEnabledAction(this);
                _commandExecutor.Execute(action);
            }
        }
    }

    public string DisplayText => Filter.DisplayText;
    public string FilterType => Filter.TypeText;

    public string FilterText
    {
        get => Filter.Value;
        set
        {
            if (!Filter.IsEditable)
                throw new InvalidOperationException("Filter is not editable.");
            if (Filter.Value != value)
            {
                Filter.Value = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayText));
                // DO NOT notify on every keystroke. Notification happens on EndEdit or Enabled change.
            }
        }
    }

    [RelayCommand]
    public void BeginEdit()
    {
        if (IsEditable)
        {
            _valueBeforeEdit = Filter.Value;
            IsEditing = true;
            IsNotEditing = false;
        }
    }

    [RelayCommand]
    public void EndEdit()
    {
        if (IsEditing)
        {
            IsEditing = false;
            IsNotEditing = true;

            // Only execute command if value actually changed
            string currentValue = Filter.Value;
            if (_valueBeforeEdit != null && _valueBeforeEdit != currentValue)
            {
                var action = new ChangeFilterValueAction(this, _valueBeforeEdit, currentValue);
                _commandExecutor.Execute(action);
            } else {
                 Debug.WriteLine($"EndEdit: Value unchanged ('{currentValue}'), no action executed.");
            }
             _valueBeforeEdit = null;
        }
    }

    // Helper method used by Actions to update bound properties after direct model manipulation.
    // TODO: A little kludgy. Is there a solution where we don't need to expose this method?
    // TODO: Will there be three events? That could be costly?
    public void RefreshProperties()
    {
        OnPropertyChanged(nameof(FilterText));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(Enabled)); // This will now update the UI for Enabled state changes
    }

     // Helper method used by AddFilterAction to recreate children VMs during Undo/Redo if needed
     // Or perhaps AddFilterAction directly manipulates the Children collection is simpler. Let's stick with that.

     // We removed the _filterConfigurationChangedCallback, assuming ICommandExecutor.Execute handles notifications.
}
