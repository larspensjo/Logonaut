using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Filters;
using Logonaut.Core.Commands;
using System.Diagnostics;
using System.Collections.Generic; // For List and KeyValuePair

namespace Logonaut.UI.ViewModels;

public record FilterHighlightColorChoice(string Name, string Key);

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
public partial class FilterViewModel : ObservableObject
{
    public IFilter Filter { get; }
    public FilterViewModel? Parent { get; }
    public ObservableCollection<FilterViewModel> Children { get; } = new();

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isEditing = false;
    [ObservableProperty] private bool _isNotEditing = true;
    [ObservableProperty] private bool _isExpanded; // Relevant for CompositeFilters
    [ObservableProperty] private bool _isActivelyMatching; // New property for visual cue

    public IRelayCommand<string> ChangeHighlightColorCommand { get; }


    public string HighlightColorKey
    {
        get => Filter.HighlightColorKey;
        set // This setter is still used if something binds directly to HighlightColorKey with TwoWay binding
        {
            if (Filter.HighlightColorKey != value)
            {
                // Check if the command can execute, to avoid issues if called inappropriately
                if (ChangeHighlightColorCommand.CanExecute(value))
                {
                    ChangeHighlightColorCommand.Execute(value);
                }
            }
        }
    }

    public static List<FilterHighlightColorChoice> AvailableHighlightColors { get; } = new()
    {
        new("Default", "FilterHighlight.Default"),
        new("Red", "FilterHighlight.Red"),
        new("Green", "FilterHighlight.Green"),
        new("Blue", "FilterHighlight.Blue"),
        new("Yellow", "FilterHighlight.Yellow")
    };

    public bool IsEditable => Filter.IsEditable;
    private readonly ICommandExecutor _commandExecutor;
    public ICommandExecutor CommandExecutor => _commandExecutor;
    private string? _valueBeforeEdit;

    public FilterViewModel(IFilter filter, ICommandExecutor commandExecutor, FilterViewModel? parent = null)
    {
        Filter = filter;
        Parent = parent;
        _commandExecutor = commandExecutor ?? throw new ArgumentNullException(nameof(commandExecutor));

        // Initialize the command
        ChangeHighlightColorCommand = new RelayCommand<string>(ExecuteChangeHighlightColor, CanExecuteChangeHighlightColor);

        if (filter is CompositeFilter composite)
        {
            foreach (var child in composite.SubFilters)
            {
                Children.Add(new FilterViewModel(child, commandExecutor, this));
            }
        }
    }

    private bool CanExecuteChangeHighlightColor(string? newColorKey)
    {
        return newColorKey != null && Filter.HighlightColorKey != newColorKey;
    }

    private void ExecuteChangeHighlightColor(string? newColorKey)
    {
        if (newColorKey == null || Filter.HighlightColorKey == newColorKey) return;

        var action = new ChangeFilterHighlightColorKeyAction(this, Filter.HighlightColorKey, newColorKey);
        _commandExecutor.Execute(action);
        // RefreshProperties is called by the action, which will update OnPropertyChanged for HighlightColorKey
        // This will also make the IsOpen binding for the popup (via ToggleButton.IsChecked) false IF we add that logic
    }

    [RelayCommand] public void AddChildFilter(IFilter childFilter)
    {
        if (Filter is CompositeFilter composite)
        {
            int index = Children.Count;
            var action = new AddFilterAction(this, childFilter, index);
            _commandExecutor.Execute(action);
            var addedVM = Children.LastOrDefault(vm => vm.Filter == childFilter);
            if (addedVM != null)
            {
                IsExpanded = true;
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

    [RelayCommand] public void BeginEdit()
    {
        if (IsEditable)
        {
            _valueBeforeEdit = Filter.Value;
            IsEditing = true;
            IsNotEditing = false;
        }
    }

    [RelayCommand] public void EndEdit()
    {
        if (IsEditing)
        {
            IsEditing = false;
            IsNotEditing = true;
            string currentValue = Filter.Value;
            if (_valueBeforeEdit != null && _valueBeforeEdit != currentValue)
            {
                var action = new ChangeFilterValueAction(this, _valueBeforeEdit, currentValue);
                _commandExecutor.Execute(action);
            }
            else
            {
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
        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(HighlightColorKey));
        // Note: IsActivelyMatching is not refreshed here as it's driven by MainViewModel logic
    }
}
