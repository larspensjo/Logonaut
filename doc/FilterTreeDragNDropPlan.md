# Logonaut: Filter Tree Drag-and-Drop Implementation Plan

This plan outlines the incremental steps to replace the current button-based filter node management with a more intuitive Drag-and-Drop (DnD) interface. Each major step aims to leave the application in a functional state.

It is a Work-in-progress.

## Prerequisites

*   Familiarity with WPF Drag-and-Drop concepts (`AllowDrop`, `DragDrop.DoDragDrop`, `DragEventArgs`, Adorners).
*   Understanding of the existing MVVM structure (`MainViewModel`, `FilterProfileViewModel`, `FilterViewModel`).

## Step 0: Implement Command Pattern for Undo/Redo

**Goal:** Establish the foundation for Undo/Redo by refactoring existing state-changing actions into Command objects. This is crucial *before* implementing DnD actions.

**Tasks:**

1.  **Define Command Interface:** Create an interface like `IUndoableAction` with `void Execute()` and `void Undo()` methods.
2.  **Implement Concrete Commands:**
    *   Create classes implementing `IUndoableAction` for *existing* filter actions:
        *   `AddFilterAction(parentVM, newFilterModel, targetIndex)`
        *   `RemoveFilterAction(parentVM, removedFilterVM, originalIndex)`
        *   `ChangeFilterValueAction(filterVM, oldValue, newValue)`
        *   `ToggleFilterEnabledAction(filterVM, oldState)`
        *   *(Initially, Move is not needed, as it's not button-based)*
    *   These commands encapsulate the logic currently in `MainViewModel` or `FilterViewModel` command handlers (e.g., manipulating `Model.SubFilters` and `ViewModel.Children`). Store necessary state for `Undo()`.
3.  **ViewModel Undo/Redo Logic:**
    *   Add `UndoStack` (e.g., `Stack<IUndoableAction>`) and `RedoStack` to `MainViewModel`.
    *   Implement `UndoCommand` and `RedoCommand` in `MainViewModel`:
        *   `Undo`: Pop from `UndoStack`, call `Undo()`, push onto `RedoStack`. Update `CanExecute`.
        *   `Redo`: Pop from `RedoStack`, call `Execute()`, push onto `UndoStack`. Update `CanExecute`.
4.  **Refactor Existing Actions:**
    *   Modify the *current* button command handlers (`AddFilter`, `RemoveFilterNode`, `ToggleEditNode`, `FilterViewModel.EndEdit`, `FilterViewModel.Enabled` setter) to:
        *   Create the corresponding `IUndoableAction` object.
        *   Call its `Execute()` method.
        *   Push the action onto the `UndoStack`.
        *   Clear the `RedoStack`.
        *   Update `UndoCommand`/`RedoCommand` `CanExecute`.
5.  **UI:** Add "Undo" and "Redo" buttons/menu items bound to the new commands in `MainViewModel`.
6.  **Testing:** Thoroughly test existing Add/Remove/Edit/Toggle functionality AND the new Undo/Redo feature.

**Result:** Application functions as before, but filter tree modifications are now undoable/redoable. The Command pattern is ready for DnD actions.

---

## Step 1: Implement Filter Palette & Add via DnD

**Goal:** Allow users to add *new* filter nodes by dragging from a palette onto composite nodes in the tree. The old "Add" buttons can be removed.

**Tasks:**

1.  **ViewModel:**
    *   Define a simple collection in `MainViewModel` representing the available filter *types* (e.g., a list of strings or simple descriptor objects: "Substring", "Regex", "AND", "OR", "NOR").
2.  **View (XAML):**
    *   Replace the existing "Add" buttons in `MainWindow.xaml` with an `ItemsControl` (e.g., inside a styled `Border` acting as the palette).
    *   Bind its `ItemsSource` to the new collection in `MainViewModel`.
    *   Create a `DataTemplate` for the palette items (e.g., a `Border` with `Icon` + `TextBlock`).
    *   Style these items to look draggable ("chips").
3.  **Drag Source (Palette Items):**
    *   In the code-behind for `MainWindow.xaml` (or using attached behaviors), handle `PreviewMouseLeftButtonDown` on the palette items.
    *   Inside the handler, call `DragDrop.DoDragDrop`.
        *   `dragSource`: The palette item itself.
        *   `data`: Pass the *filter type identifier* (e.g., the string "SubstringType") using `DataFormats.String` or a custom format.
        *   `allowedEffects`: `DragDropEffects.Copy` (since we're creating a new instance).
4.  **Drop Target (TreeView):**
    *   Set `AllowDrop="True"` on the `FilterTreeView`.
    *   Handle `TreeView.DragEnter` and `TreeView.DragOver`:
        *   Check if `e.Data.GetDataPresent(DataFormats.String)` is true (or your custom format).
        *   Hit-test to find the `TreeViewItem` under the cursor.
        *   Check if the `DataContext` of the target item is a `FilterViewModel` representing a *composite* filter.
        *   If valid target: Set `e.Effects = DragDropEffects.Copy`. Add visual feedback (target highlight, insertion line - see Step 4 for refinement). Mark `e.Handled = true`.
        *   If invalid target: Set `e.Effects = DragDropEffects.None`. Mark `e.Handled = true`.
    *   Handle `TreeView.DragLeave`: Remove visual feedback.
    *   Handle `TreeView.Drop`:
        *   Perform the same validation as `DragOver`.
        *   If valid:
            *   Get the filter type string from `e.Data.GetData()`.
            *   Get the target `FilterViewModel` (the composite node).
            *   Create a new `IFilter` model instance based on the type.
            *   Determine the drop index (optional, for specific positioning).
            *   **Crucially:** Create and execute an `AddFilterAction` (from Step 0), passing the target parent VM, the new filter model, and index.
            *   Remove visual feedback.
            *   Mark `e.Handled = true`.
5.  **Testing:** Verify dragging each type from the palette to valid composite nodes adds the correct filter. Test dropping on invalid targets. Test Undo/Redo for adds.

**Result:** Users can add new filters via DnD from the palette. The old Add buttons are gone. Move/Delete still use original buttons (if any remain) or require context menus/new buttons later.

---

## Step 2: Implement Move/Re-Parent via DnD

**Goal:** Allow users to move existing filter nodes from one composite parent to another within the tree.

**Tasks:**

1.  **ViewModel:**
    *   Implement `MoveFilterAction(oldParentVM, newParentVM, movedFilterVM, originalIndex, newIndex)` implementing `IUndoableAction`. `Execute()` removes from old parent and adds to new parent (both model and VM children). `Undo()` reverses this.
2.  **Drag Source (TreeViewItem):**
    *   Handle `PreviewMouseLeftButtonDown` on the `ContentPresenter` or main `Border` *inside* the `TreeViewItem`'s `DataTemplate` (or use attached behaviors).
    *   Inside the handler, call `DragDrop.DoDragDrop`.
        *   `dragSource`: The `TreeViewItem` itself.
        *   `data`: Pass a reference to the `FilterViewModel` being dragged (use a custom data format or rely on object type).
        *   `allowedEffects`: `DragDropEffects.Move`.
3.  **Drop Target (TreeView - Enhancements):**
    *   Modify `TreeView.DragEnter`/`DragOver`:
        *   Check if data is a `FilterViewModel`.
        *   Perform validation:
            *   Target must be a composite node.
            *   Target cannot be the node being dragged.
            *   Target cannot be a descendant of the node being dragged (prevent recursive loops).
            *   *(Deferring Reorder):* Target cannot be the *current* parent of the node being dragged.
        *   If valid: Set `e.Effects = DragDropEffects.Move`. Add visual feedback. Mark `e.Handled = true`.
        *   If invalid: Set `e.Effects = DragDropEffects.None`. Mark `e.Handled = true`.
    *   Modify `TreeView.Drop`:
        *   Perform the same validation.
        *   If valid:
            *   Get the source `FilterViewModel` from `e.Data`.
            *   Get the target `FilterViewModel`.
            *   Get the source's original parent `FilterViewModel`.
            *   Determine the drop index.
            *   Create and execute a `MoveFilterAction`, passing the old parent, new parent (target), the moved VM, original index, and new index.
            *   Remove visual feedback.
            *   Mark `e.Handled = true`.
4.  **Testing:** Verify moving nodes between different valid composite parents. Test dropping onto invalid targets (self, children, non-composites, original parent). Test Undo/Redo for moves.

**Result:** Users can restructure the filter tree by dragging existing nodes. Adding is via palette DnD. Deletion still needs implementation.

---

## Step 3: Implement Delete via DnD Trash Bin

**Goal:** Allow users to delete filter nodes by dragging them onto a dedicated "Trash Bin" area. Remove the old "Remove Node" button.

**Tasks:**

1.  **View (XAML):**
    *   Add a `Button` or `Border` styled as a Trash Bin icon (e.g., at the bottom-left of the filter panel). Name it `TrashBinTarget`.
    *   Set `AllowDrop="True"` on `TrashBinTarget`.
    *   Remove the old "Remove Node" button.
2.  **Drop Target (Trash Bin):**
    *   Handle `TrashBinTarget.DragEnter`/`DragOver`:
        *   Check if `e.Data` contains a `FilterViewModel`.
        *   If yes: Set `e.Effects = DragDropEffects.Move` (semantically, it's being moved *to* deletion). Add visual feedback (highlight trash). Mark `e.Handled = true`.
        *   If no: Set `e.Effects = DragDropEffects.None`. Mark `e.Handled = true`.
    *   Handle `TrashBinTarget.DragLeave`: Remove visual feedback.
    *   Handle `TrashBinTarget.Drop`:
        *   Check if data is a `FilterViewModel`.
        *   If yes:
            *   Get the source `FilterViewModel` from `e.Data`.
            *   Get its parent `FilterViewModel`.
            *   Find its original index within the parent.
            *   Create and execute a `RemoveFilterAction`, passing the parent, the VM to remove, and its original index.
            *   Remove visual feedback.
            *   Mark `e.Handled = true`.
3.  **Testing:** Verify dragging nodes from the tree onto the trash bin deletes them. Test dragging non-nodes (from palette) to trash (should do nothing). Test Undo/Redo for deletions.

**Result:** Core DnD functionality (Add, Move, Delete) is implemented. Filter tree manipulation is now primarily via drag-and-drop.

---

## Step 4: Improve DnD Visual Feedback

**Goal:** Enhance the user experience with clearer cursors, ghost images during drag, and precise insertion indicators.

**Tasks:**

1.  **Cursors:**
    *   Handle the `GiveFeedback` event on the *drag source* elements (palette items, TreeViewItems).
    *   Check `e.Effects` (set by the `DragOver` handler on the target).
    *   Set `Mouse.SetCursor(...)` based on `e.Effects`:
        *   `DragDropEffects.Copy` -> `Cursors.Arrow` with a "+" overlay (requires custom cursor or Adorner).
        *   `DragDropEffects.Move` -> `Cursors.Arrow` (standard move).
        *   `DragDropEffects.None` -> `Cursors.No`.
    *   Mark `e.Handled = true` to prevent default cursors.
2.  **Ghost Image (Adorner):**
    *   Create a simple `Adorner` class that can display a semi-transparent visual (e.g., a copy of the palette item or TreeViewItem content).
    *   In the `PreviewMouseLeftButtonDown` handler (where drag starts), create and show the adorner on the `AdornerLayer` of a suitable parent element (e.g., the main `Window` or the `TreeView`).
    *   In `DragOver` or `GiveFeedback` handlers, update the adorner's position to follow the mouse.
    *   In the `Drop` handler and on drag completion/cancellation (check `DragDrop.DoDragDrop` result), remove the adorner.
3.  **Insertion Indicator Line:**
    *   This is the trickiest part. Handle `TreeView.DragOver`.
    *   Hit-test *precisely* to determine if the cursor is *between* items or over the top/bottom half of an item within the target composite `TreeViewItem`.
    *   Use another `Adorner` associated with the *target* `TreeViewItem`. This adorner draws a simple horizontal line at the calculated insertion point.
    *   Update the insertion adorner's visibility and position in `DragOver`. Hide it in `DragLeave` and `Drop`.
4.  **Theme Considerations:** Ensure highlight colors, insertion line colors, and adorner visuals work well in both Light and Dark themes, likely using `DynamicResource` bindings within styles or templates where possible.
5.  **Testing:** Verify cursors change correctly, ghost image appears and follows mouse, and insertion line shows accurately over valid drop targets.

**Result:** DnD operations provide much clearer visual feedback, making the interaction more intuitive and less error-prone.

---

## Step 5: Implement Advanced DnD Features (Optional Enhancements)

**Goal:** Add Copy (Ctrl+Drag) and Reorder Siblings functionality.

**Tasks:**

1.  **Copy (Ctrl+Drag):**
    *   **ViewModel:** Implement `CopyFilterAction` (needs recursive copying logic for models/VMs) and integrate with Undo/Redo.
    *   **View (Drop Handler):** In `TreeView.Drop`, check `e.KeyStates.HasFlag(DragDropKeyStates.ControlKey)`.
    *   If Ctrl is pressed *and* dragging an *existing* node to a *valid composite target*:
        *   Execute a `CopyFilterAction` instead of `MoveFilterAction`. Ensure the source node remains untouched.
    *   **Cursor:** Update `GiveFeedback` to show a "copy" cursor (e.g., standard arrow + "+") when Ctrl is held over a valid drop target during a move operation.
2.  **Reorder Siblings:**
    *   **ViewModel:** Implement `ReorderFilterAction(parentVM, movedVM, oldIndex, newIndex)` and integrate with Undo/Redo. This action modifies the order within the *same* parent's `Children`/`SubFilters`.
    *   **View (Drop Handler):** In `TreeView.Drop`, if dragging an existing node:
        *   Check if the target composite node VM is the *same instance* as the source node's parent VM.
        *   If yes: Execute a `ReorderFilterAction` based on the calculated drop index within the target (which is also the source parent).
        *   If no: Proceed with the existing `MoveFilterAction` logic.
    *   **Feedback:** The insertion line indicator (Step 4) is essential for showing the target order visually.
3.  **Testing:** Verify Ctrl+Drag copies nodes. Verify dragging within the same parent reorders nodes. Test Undo/Redo for both.

**Result:** The filter tree is highly flexible, allowing copying and reordering via DnD.

---

## Step 6: Palette Click Action (Low Priority)

**Goal:** Allow clicking a palette item to add it to the currently selected composite node (if any).

**Tasks:**

1.  **View:** Handle `Click` or `MouseUp` events on the palette items *in addition* to the DnD initiation handlers.
2.  **ViewModel:** Determine the "current target" composite node (this could be `SelectedFilterNode` if it's composite, or its parent if a leaf is selected, or potentially the CCN if that concept were implemented).
3.  **Logic:** If a valid composite target exists, create and execute the corresponding `AddFilterAction` for the clicked filter type, adding it to that target.
4.  **Testing:** Verify clicking palette items adds to the appropriate target. Test cases where no valid target is selected.

**Result:** Provides an alternative Add mechanism for users who prefer clicks over DnD.