using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Logonaut.Common;
using Logonaut.Filters;
using Logonaut.UI.Commands;
using System.Diagnostics;

namespace Logonaut.UI.ViewModels;

/*
 * ViewModel for a FilterProfile model, displayed in UI lists (e.g., ComboBox).
 *
 * Purpose/Role:
 * Wraps a FilterProfile, exposing its name for display/editing and holding the
 * root FilterViewModel for the profile's rule tree. Manages inline renaming UI state.
 * Decouples profile selection UI from the model.
 *
 * Implementation Notes:
 * Uses CommunityToolkit.Mvvm. Requires ICommandExecutor injection for child VMs.
 */
public partial class FilterProfileViewModel : ObservableObject
{
    private readonly ICommandExecutor _commandExecutor;

    public FilterProfile Model { get; }

    // The root ViewModel for the filter tree displayed when this profile is active.
    // Lazily created or managed by MainViewModel.
    [ObservableProperty]
    private FilterViewModel? _rootFilterViewModel;

    // Name property for binding
    [ObservableProperty]
    private string _name;

    // Editing state properties
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotEditing))] // Notify dependent property
    private bool _isEditing;

    public bool IsNotEditing => !IsEditing;

    // Store the original name during edit
    private string? _originalNameBeforeEdit;

    // Commands
    public IRelayCommand BeginRenameCommand { get; }
    public IRelayCommand EndRenameCommand { get; }
    public IRelayCommand CancelRenameCommand { get; } // New command for cancellation

    public FilterProfileViewModel(FilterProfile model, ICommandExecutor commandExecutor)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        _name = Model.Name ?? "Unnamed Profile"; // Ensure name is not null for display
        _commandExecutor = commandExecutor;

        if (Model.RootFilter != null)
        {
            _rootFilterViewModel = new FilterViewModel(Model.RootFilter, _commandExecutor);
        }

        BeginRenameCommand = new RelayCommand(() =>
        {
            // Store original name and enter edit mode
            _originalNameBeforeEdit = Name;
            IsEditing = true;
            Debug.WriteLine($"BeginRenameCommand: Original='{_originalNameBeforeEdit}', IsEditing={IsEditing}");
        }, () => IsNotEditing); // Can only begin if not already editing

        EndRenameCommand = new RelayCommand(() =>
        {
            // Commit the rename (validation happens in MainViewModel)
            if (IsEditing)
            {
                IsEditing = false;
                // The change to Name should already be bound.
                // Trigger the callback ONLY if the name actually changed from the original.
                // MainViewModel should observe the Name property change for saving/validation.
                if (Name != _originalNameBeforeEdit)
                {
                    Model.Name = Name; // Ensure model is synced if binding didn't catch it
                    // Maybe notify here? Or let MainViewModel handle it via PropertyChanged.
                    // Let's rely on MainViewModel observing Name changes.
                    Debug.WriteLine($"EndRenameCommand: Committed name '{Name}', IsEditing={IsEditing}");
                }
                else
                {
                        Debug.WriteLine($"EndRenameCommand: Name unchanged ('{Name}'), IsEditing={IsEditing}");
                }
                _originalNameBeforeEdit = null; // Clear stored name
            }
        }, () => IsEditing); // Can only end if currently editing

        CancelRenameCommand = new RelayCommand(() => // New Command Logic
        {
            // Cancel the rename
            if (IsEditing)
            {
                // Revert to original name if it was stored
                if (_originalNameBeforeEdit != null)
                {
                    Name = _originalNameBeforeEdit; // This triggers OnNameChanged if different
                }
                IsEditing = false;
                _originalNameBeforeEdit = null; // Clear stored name
                    Debug.WriteLine($"CancelRenameCommand: Reverted to '{Name}', IsEditing={IsEditing}");
            }
        }, () => IsEditing); // Can only cancel if currently editing

        // --- Refresh CanExecute when IsEditing changes ---
        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(IsEditing))
            {
                // Notify dependent commands that their CanExecute might have changed
                BeginRenameCommand.NotifyCanExecuteChanged();
                EndRenameCommand.NotifyCanExecuteChanged();
                CancelRenameCommand.NotifyCanExecuteChanged();
            }
        };
    }

    // Update the underlying model's name when the VM's Name changes
    // This happens automatically via binding usually, but ensure model sync if needed.
    partial void OnNameChanged(string value)
    {
        // If editing is active, the model might not be updated immediately by binding,
        // EndRename handles the final sync.
        // If NOT editing (e.g., reverted by Cancel), ensure model is updated.
        if (!IsEditing && Model.Name != value)
        {
            Model.Name = value;
            // Potentially trigger callback if needed immediately, but usually handled by MainViewModel observing
            // _filterConfigurationChangedCallback?.Invoke(); // Probably not needed here
        }
        Debug.WriteLine($"OnNameChanged: New value='{value}', IsEditing={IsEditing}, Model.Name='{Model.Name}'");
    }

    // Method to rebuild the RootFilterViewModel needs to pass the executor
    public void RefreshRootViewModel()
    {
        if (Model.RootFilter != null)
            RootFilterViewModel = new FilterViewModel(Model.RootFilter, _commandExecutor);
        else
            RootFilterViewModel = null;
    }

    // Helper to update the model's root filter (e.g., when adding the first filter node)
    public void SetModelRootFilter(IFilter? filter)
    {
        Model.RootFilter = filter;
        RefreshRootViewModel();

        // Maybe trigger filter update via executor? Or let caller handle it.
        // For now, let MainViewModel handle the TriggerFilterUpdate after calling this.
    }
}
