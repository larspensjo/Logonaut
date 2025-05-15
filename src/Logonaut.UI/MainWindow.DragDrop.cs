using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input; // Required for RoutedUICommand
using System.Windows.Navigation;
using Logonaut.UI.ViewModels;
using Logonaut.Filters;
using System.Diagnostics;

namespace Logonaut.UI;

// All the event handlers related to Drag and Drop functionality for the Filter Palette and FilterTreeView.
// Drag-and-drop logic is a distinct feature set with its own event handlers and visual feedback mechanisms.
public partial class MainWindow : Window, IDisposable
{
    // Field for DnD visual feedback +++
    private TreeViewItem? _dragOverTreeViewItem = null;
    private Brush? _originalDragOverItemBrush = null;
    private const string DragDropDataFormatFilterType = "LogonautFilterTypeIdentifier"; // Custom data format key

    // --- Drag Source: Filter Palette Item ---
    // Initiates a drag operation when a filter type is dragged from the palette.
    private void PaletteItemsControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? originalSource = e.OriginalSource as DependencyObject;
        ContentPresenter? paletteItemContainer = null;

        while (originalSource != null && originalSource != sender as ItemsControl)
        {
            if (originalSource is ContentPresenter cp && cp.DataContext is Logonaut.Common.PaletteItemDescriptor)
            {
                paletteItemContainer = cp;
                break;
            }
            originalSource = VisualTreeHelper.GetParent(originalSource);
        }

        if (paletteItemContainer != null && paletteItemContainer.DataContext is Logonaut.Common.PaletteItemDescriptor descriptor)
        {
            if (!descriptor.IsEnabled) // If the item itself is disabled, do not start drag
            {
                e.Handled = true;
                return;
            }

            DataObject dragData = new DataObject();
            dragData.SetData(DragDropDataFormatFilterType, descriptor.TypeIdentifier);

            if (descriptor.IsDynamic && !string.IsNullOrEmpty(descriptor.InitialValue))
            {
                // Add a new format for the initial value
                dragData.SetData("LogonautFilterInitialValue", descriptor.InitialValue);
            }

            DragDrop.DoDragDrop(paletteItemContainer, dragData, DragDropEffects.Copy);
            e.Handled = true;
        }
    }

    // --- Drop Target: FilterTreeView ---
    private void FilterTreeView_DragEnter(object sender, DragEventArgs e)
    {
        UpdateDragDropEffects(e);
    }

    private void FilterTreeView_DragOver(object sender, DragEventArgs e)
    {
        UpdateDragDropEffects(e);
    }

    // Determines the effect of a drag operation over the TreeView and provides visual feedback.
    private void UpdateDragDropEffects(DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        ClearDropTargetAdornment();
        // Don't immediately hide/set resting here. Let the logic below decide.
        // The permanent adorner's resting state is managed by UpdateEmptyTreeViewAdornerVisibility

        if (e.Data.GetDataPresent(DragDropDataFormatFilterType))
        {
            Point pt = e.GetPosition(FilterTreeView);
            TreeViewItem? targetTVI = GetVisualAncestor<TreeViewItem>(FilterTreeView.InputHitTest(pt) as DependencyObject);
            FilterViewModel? targetVM = (targetTVI?.DataContext) as FilterViewModel;

            if (_viewModel.ActiveFilterProfile is not { } activeFilterProfile)
                throw new InvalidOperationException("ActiveFilterProfile is null.");

            if (targetTVI != null && targetVM != null && targetVM.Filter is CompositeFilter)
            {
                e.Effects = DragDropEffects.Copy;
                ApplyDropTargetAdornment(targetTVI);
                HideEmptyDropAdornerOrSetResting(); // If over an item, ensure empty adorner (if visible) is resting or hidden
            }
            else if (targetTVI == null)
            {
                if (activeFilterProfile.RootFilterViewModel == null) // Tree is completely empty
                {
                    e.Effects = DragDropEffects.Copy;
                    ShowEmptyDropAdornerActiveState(); // Show in ACTIVE state during drag
                }
                else if (activeFilterProfile.RootFilterViewModel.Filter is CompositeFilter) // Tree has composite root
                {
                    e.Effects = DragDropEffects.Copy;
                    // No specific item adornment, permanent empty adorner should be hidden by UpdateEmptyTreeViewAdornerVisibility
                }
            }
        }
        else // No valid data
        {
            HideEmptyDropAdornerOrSetResting(); // Ensure it's resting if no valid data
        }
        e.Handled = true;
    }

    private void FilterTreeView_DragLeave(object sender, DragEventArgs e)
    {
        ClearDropTargetAdornment();
        HideEmptyDropAdornerOrSetResting();
        e.Handled = true;
    }

    // Handles the drop action on the FilterTreeView.
    private void FilterTreeView_Drop(object sender, DragEventArgs e)
    {
        ClearDropTargetAdornment(); // Clear highlight after drop
        HideEmptyDropAdornerOrSetResting();
        var mainViewModel = DataContext as MainViewModel;
        if (mainViewModel == null) return;

        string? filterTypeIdentifier = e.Data.GetData(DragDropDataFormatFilterType) as string;
        if (string.IsNullOrEmpty(filterTypeIdentifier)) return;

        string? initialValue = null;
        if (e.Data.GetDataPresent("LogonautFilterInitialValue"))
            initialValue = e.Data.GetData("LogonautFilterInitialValue") as string;

        FilterViewModel? targetParentVM = null;
        int? dropIndex = null; // For future precise insertion; null means append for now.

        Point pt = e.GetPosition(FilterTreeView);
        TreeViewItem? targetTVI = GetVisualAncestor<TreeViewItem>(FilterTreeView.InputHitTest(pt) as DependencyObject);
        FilterViewModel? hitTestVM = (targetTVI?.DataContext) as FilterViewModel;

        if (hitTestVM != null) // Dropped on an existing item
        {
            if (hitTestVM.Filter is CompositeFilter)
            {
                targetParentVM = hitTestVM;
                // For Step 1, always append. Index calculation for specific position comes later.
                dropIndex = targetParentVM.Children.Count;
            }
            else
            {
                // Dropping on a non-composite leaf is handled by DragOver setting Effects to None.
                // If it still reaches here, do nothing.
                e.Effects = DragDropEffects.None;
                return;
            }
        }
        // If hitTestVM is null, it implies a drop on empty TreeView space.
        // mainViewModel.ExecuteAddFilterFromDrop will handle logic for adding to root or empty tree.
        // In this case, targetParentVM remains null, and ExecuteAddFilterFromDrop will figure it out.

        mainViewModel.ExecuteAddFilterFromDrop(filterTypeIdentifier, targetParentVM, dropIndex, initialValue);
        e.Handled = true;
    }

    // --- Visual Feedback Helpers ---
    private void ShowEmptyDropAdornerActiveState() // Renamed for clarity
    {
        if (_emptyDropAdorner != null && _emptyDropAdorner.Visibility == Visibility.Visible)
        {
            _emptyDropAdorner.SetVisualState(true); // Switch to active state
        }
        // If it wasn't visible, UpdateEmptyTreeViewAdornerVisibility should handle showing it in resting first.
        // Or, if it should immediately appear active:
        // else if (_viewModel.ActiveFilterProfile?.RootFilterViewModel == null) // Only if tree is truly empty
        // {
        //     UpdateEmptyTreeViewAdornerVisibility(); // This will show it in resting
        //     _emptyDropAdorner?.SetVisualState(true); // Then immediately switch to active
        // }
    }

    private void HideEmptyDropAdornerOrSetResting() // Renamed and modified
    {
        if (_emptyDropAdorner != null && _emptyDropAdorner.Visibility == Visibility.Visible)
        {
            // If the tree is still empty, revert to resting state, otherwise it will be hidden by UpdateEmptyTreeViewAdornerVisibility
            bool isTreeEmpty = (_viewModel.ActiveFilterProfile?.RootFilterViewModel == null && _viewModel.ActiveTreeRootNodes.Count == 0);
            if (isTreeEmpty)
            {
                _emptyDropAdorner.SetVisualState(false); // Revert to resting
            }
            // If not empty, UpdateEmptyTreeViewAdornerVisibility (called after drop) will hide it.
        }
    }

    // Applies a temporary background highlight to a TreeViewItem during drag-over.
    private void ApplyDropTargetAdornment(TreeViewItem? tvi)
    {
        if (tvi != null)
        {
            _dragOverTreeViewItem = tvi;
            _originalDragOverItemBrush = tvi.Background;
            // Use a theme-aware highlight brush if possible, or fallback
            var highlightBrush = TryFindResource("AccentBrush") as Brush ?? SystemColors.HighlightBrush; // Using AccentBrush for highlight
            tvi.Background = highlightBrush;
        }
    }

    // Clears any temporary background highlight from a TreeViewItem.
    private void ClearDropTargetAdornment()
    {
        if (_dragOverTreeViewItem != null && _originalDragOverItemBrush != null)
        {
            _dragOverTreeViewItem.Background = _originalDragOverItemBrush;
        }
        _dragOverTreeViewItem = null;
        _originalDragOverItemBrush = null;
    }

    // --- Helper Methods ---

    // Gets the FilterViewModel associated with a DependencyObject (typically a UI element within a TreeViewItem).
    private FilterViewModel? GetFilterViewModelFromElement(DependencyObject? element)
    {
        // Traverse up to find TreeViewItem and get its DataContext
        TreeViewItem? tvi = GetVisualAncestor<TreeViewItem>(element);
        return tvi?.DataContext as FilterViewModel;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) // Catch potential errors opening the link
        {
            Debug.WriteLine($"Error opening hyperlink: {ex.Message}");
            // Optionally show a message to the user if desired, though often failing silently is acceptable here.
            // MessageBox.Show($"Could not open link: {e.Uri.AbsoluteUri}\nError: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        e.Handled = true; // Important: Mark the event as handled to prevent default navigation attempts
    }

    // Finds the first visual ancestor of a specific type.
    public static T? GetVisualAncestor<T>(DependencyObject? d) where T : class
    {
        while (d != null)
        {
            if (d is T tItem) return tItem;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
