# Issue #183: Edit -> Delete Menu Item Implementation

## Overview
The 'Edit -> Delete' menu item needs to be implemented to allow users to delete items from the catalog.

## Requirements

### Menu Item
- Location: Edit menu
- Label: "Delete"
- Shortcut: Delete key or Ctrl+D
- Command: DeleteItemCommand

### Behavior
- Should be enabled only when an item is selected in the list/tree view
- Delete selected item(s) from the catalog
- Update the display to reflect the deletion
- Optionally show confirmation dialog before deletion
- Update status bar to show number of items deleted

### Implementation Steps
1. Add DeleteItemCommand to MainWindowViewModel
2. Create MenuItem binding in MainWindow XAML
3. Implement delete operation in the data layer
4. Handle selection state (enable/disable based on selection)
5. Add keyboard shortcut handling
6. Add confirmation dialog (optional but recommended for UX)

### Testing
- Verify menu item is disabled when no item selected
- Verify menu item is enabled when item(s) selected
- Verify deletion removes item from display
- Verify keyboard shortcut (Delete key) works
- Verify confirmation dialog appears (if implemented)

## Related Issues
- #177: Edit -> Add modal doesn't do anything
- #184: Edit -> Properties menu item not implemented
