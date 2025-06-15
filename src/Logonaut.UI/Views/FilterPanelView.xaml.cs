// ===== File: C:\Users\larsp\src\Logonaut\src\Logonaut.UI\Views\FilterPanelView.xaml.cs =====

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents; // Required for AdornerLayer
using System.Windows.Input;
using System.Windows.Media;
using Logonaut.Filters;
using Logonaut.UI.Helpers;
using Logonaut.UI.ViewModels;
using System.Diagnostics;
using System.Collections.Specialized; // For NotifyCollectionChangedEventArgs

namespace Logonaut.UI.Views;
public partial class FilterPanelView : UserControl
{
    private MainViewModel? _viewModel;

    // Fields for DnD visual feedback
    private TreeViewItem? _dragOverTreeViewItem = null;
    private Brush? _originalDragOverItemBrush = null;
    private const string DragDropDataFormatFilterType = "LogonautFilterTypeIdentifier"; // Custom data format key

    // Adorner for empty TreeView drop target
    private EmptyDropTargetAdorner? _emptyDropAdorner;
    private AdornerLayer? _adornerLayer;

    public FilterPanelView()
    {
        InitializeComponent();
        this.Loaded += FilterPanelView_Loaded;
        this.Unloaded += FilterPanelView_Unloaded;
    }

    private void FilterPanelView_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel = DataContext as MainViewModel;
        if (_viewModel == null)
        {
            // This can happen if DataContext is not set yet or is of a different type.
            // Defer further initialization until DataContext is available.
            DataContextChanged += FilterPanelView_DataContextChanged;
            return;
        }
        InitializeAdornerAndEventHandlers();
    }

    private void FilterPanelView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MainViewModel newViewModel)
        {
            _viewModel = newViewModel;
            InitializeAdornerAndEventHandlers();
            // Unsubscribe from DataContextChanged to avoid multiple initializations
            DataContextChanged -= FilterPanelView_DataContextChanged;
        }
    }

    private void InitializeAdornerAndEventHandlers()
    {
        if (_viewModel == null) return; // Guard against null ViewModel

        _adornerLayer = AdornerLayer.GetAdornerLayer(FilterTreeView);
        if (_adornerLayer == null)
        {
            // If the adorner layer is not ready yet, defer.
            // This can happen if the control is loaded but not yet fully rendered.
            // A common way to handle this is to retry on the next layout update,
            // but for simplicity, we'll log and assume it becomes available.
            Debug.WriteLine("FilterPanelView: AdornerLayer not found on load. Adorner might not work.");
        }

        _viewModel.ActiveTreeRootNodes.CollectionChanged += ActiveTreeRootNodes_CollectionChanged;
        UpdateEmptyTreeViewAdornerVisibility(); // Initial check

        // If TreeView_SelectedItemChanged was previously used for _viewModel.SelectedFilterNode,
        // ensure that logic is still correctly handled (likely via binding now, but double-check)
    }


    private void FilterPanelView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.ActiveTreeRootNodes.CollectionChanged -= ActiveTreeRootNodes_CollectionChanged;
        }
        // Other cleanup if necessary (e.g., disposables for any local subscriptions)
        _viewModel = null; // Release reference
        _adornerLayer = null;
        _emptyDropAdorner = null;
    }

    private void ActiveTreeRootNodes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateEmptyTreeViewAdornerVisibility();
    }

    private void UpdateEmptyTreeViewAdornerVisibility()
    {
        if (_adornerLayer == null) _adornerLayer = AdornerLayer.GetAdornerLayer(FilterTreeView);
        if (_adornerLayer == null) return; // Still not available
        if (_viewModel == null) return; // ViewModel not set

        bool isTreeEmpty = (_viewModel.ActiveFilterProfile?.RootFilterViewModel == null && _viewModel.ActiveTreeRootNodes.Count == 0);

        if (isTreeEmpty)
        {
            if (_emptyDropAdorner == null)
            {
                _emptyDropAdorner = new EmptyDropTargetAdorner(FilterTreeView);
                _adornerLayer.Add(_emptyDropAdorner);
            }
            _emptyDropAdorner.SetVisualState(false); // Set to resting state
            _emptyDropAdorner.Visibility = Visibility.Visible;
        }
        else
        {
            if (_emptyDropAdorner != null)
            {
                _emptyDropAdorner.Visibility = Visibility.Collapsed;
            }
        }
    }

    // --- Drag Source: Filter Palette Item ---
    private void PaletteItemsControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel == null) return;

        DependencyObject? originalSource = e.OriginalSource as DependencyObject;
        ContentPresenter? paletteItemContainer = null;

        // Find the ContentPresenter for the PaletteItemDescriptor
        while (originalSource != null && originalSource != sender as ItemsControl)
        {
            if (originalSource is ContentPresenter cp && cp.DataContext is Logonaut.UI.Descriptors.PaletteItemDescriptor)
            {
                paletteItemContainer = cp;
                break;
            }
            originalSource = VisualTreeHelper.GetParent(originalSource);
        }

        if (paletteItemContainer != null && paletteItemContainer.DataContext is Logonaut.UI.Descriptors.PaletteItemDescriptor descriptor)
        {
            if (!descriptor.IsEnabled)
            {
                e.Handled = true;
                return;
            }

            DataObject dragData = new DataObject();
            dragData.SetData(DragDropDataFormatFilterType, descriptor.TypeIdentifier);

            if (descriptor.IsDynamic && !string.IsNullOrEmpty(descriptor.InitialValue))
            {
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

    private void UpdateDragDropEffects(DragEventArgs e)
    {
        if (_viewModel == null) { e.Effects = DragDropEffects.None; e.Handled = true; return; }

        e.Effects = DragDropEffects.None;
        ClearDropTargetAdornment();

        if (e.Data.GetDataPresent(DragDropDataFormatFilterType))
        {
            Point pt = e.GetPosition(FilterTreeView);
            TreeViewItem? targetTVI = GetVisualAncestor<TreeViewItem>(FilterTreeView.InputHitTest(pt) as DependencyObject);
            FilterViewModel? targetVM = (targetTVI?.DataContext) as FilterViewModel;

            if (_viewModel.ActiveFilterProfile == null)
            {
                e.Effects = DragDropEffects.None; // Should not happen if UI is consistent
                e.Handled = true;
                return;
            }

            if (targetTVI != null && targetVM != null && targetVM.Filter is CompositeFilter)
            {
                e.Effects = DragDropEffects.Copy;
                ApplyDropTargetAdornment(targetTVI);
                HideEmptyDropAdornerOrSetResting();
            }
            else if (targetTVI == null) // Dropped on empty space
            {
                if (_viewModel.ActiveFilterProfile.RootFilterViewModel == null && _viewModel.ActiveTreeRootNodes.Count == 0) // Tree is completely empty
                {
                    e.Effects = DragDropEffects.Copy;
                    ShowEmptyDropAdornerActiveState();
                }
                else if (_viewModel.ActiveFilterProfile.RootFilterViewModel?.Filter is CompositeFilter) // Tree has composite root
                {
                    e.Effects = DragDropEffects.Copy;
                    // No specific item adornment, permanent empty adorner might be handled by UpdateEmptyTreeViewAdornerVisibility
                    // if it's meant to be visible when root is composite but empty.
                    // For drag over empty space *below* root, let's ensure adorner is resting or hidden.
                    HideEmptyDropAdornerOrSetResting();
                }
            }
        }
        else // No valid data
        {
            HideEmptyDropAdornerOrSetResting();
        }
        e.Handled = true;
    }

    private void FilterTreeView_DragLeave(object sender, DragEventArgs e)
    {
        ClearDropTargetAdornment();
        HideEmptyDropAdornerOrSetResting();
        e.Handled = true;
    }

    private void FilterTreeView_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel == null) return;

        ClearDropTargetAdornment();
        HideEmptyDropAdornerOrSetResting();

        string? filterTypeIdentifier = e.Data.GetData(DragDropDataFormatFilterType) as string;
        if (string.IsNullOrEmpty(filterTypeIdentifier)) return;

        string? initialValue = null;
        if (e.Data.GetDataPresent("LogonautFilterInitialValue"))
        {
            initialValue = e.Data.GetData("LogonautFilterInitialValue") as string;
        }

        FilterViewModel? targetParentVM = null;
        int? dropIndex = null;

        Point pt = e.GetPosition(FilterTreeView);
        TreeViewItem? targetTVI = GetVisualAncestor<TreeViewItem>(FilterTreeView.InputHitTest(pt) as DependencyObject);
        FilterViewModel? hitTestVM = (targetTVI?.DataContext) as FilterViewModel;

        if (hitTestVM != null)
        {
            if (hitTestVM.Filter is CompositeFilter)
            {
                targetParentVM = hitTestVM;
                dropIndex = targetParentVM.Children.Count; // Append for now
            }
            else
            {
                e.Effects = DragDropEffects.None;
                return;
            }
        }

        _viewModel.ExecuteAddFilterFromDrop(filterTypeIdentifier, targetParentVM, dropIndex, initialValue);
        e.Handled = true;
    }

    // --- Visual Feedback Helpers ---
    private void ShowEmptyDropAdornerActiveState()
    {
        if (_emptyDropAdorner != null && _emptyDropAdorner.Visibility == Visibility.Visible)
        {
            _emptyDropAdorner.SetVisualState(true);
        }
        else if (_viewModel?.ActiveFilterProfile?.RootFilterViewModel == null && _viewModel?.ActiveTreeRootNodes.Count == 0)
        {
            UpdateEmptyTreeViewAdornerVisibility(); // Show it (will be resting)
            _emptyDropAdorner?.SetVisualState(true); // Then set to active
        }
    }

    private void HideEmptyDropAdornerOrSetResting()
    {
        if (_emptyDropAdorner != null && _emptyDropAdorner.Visibility == Visibility.Visible)
        {
            bool isTreeEmpty = (_viewModel?.ActiveFilterProfile?.RootFilterViewModel == null && _viewModel?.ActiveTreeRootNodes.Count == 0);
            if (isTreeEmpty)
            {
                _emptyDropAdorner.SetVisualState(false);
            }
            else
            {
                // If not empty, UpdateEmptyTreeViewAdornerVisibility (called after drop) will handle hiding it.
                // Or, we can explicitly hide it if we are sure it should be hidden.
                // For now, let UpdateEmpty... handle it unless it's a drag leave on non-empty tree.
            }
        }
    }

    private void ApplyDropTargetAdornment(TreeViewItem? tvi)
    {
        if (tvi != null)
        {
            _dragOverTreeViewItem = tvi;
            _originalDragOverItemBrush = tvi.Background;
            var highlightBrush = TryFindResource("AccentBrush") as Brush ?? SystemColors.HighlightBrush;
            tvi.Background = highlightBrush;
        }
    }

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
    private static T? GetVisualAncestor<T>(DependencyObject? d) where T : class
    {
        while (d != null)
        {
            if (d is T tItem) return tItem;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    // Update SelectedFilterNode in ViewModel when TreeView selection changes
    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_viewModel != null)
        {
            _viewModel.SelectedFilterNode = e.NewValue as FilterViewModel;
        }
    }
}
