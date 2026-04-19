# Issue #185: File -> Save Menu Item Implementation

## Overview
The 'File -> Save' menu item needs to be implemented to save the current catalog to disk.

## Requirements

### Menu Item
- Location: File menu
- Label: "Save"
- Shortcut: Ctrl+S
- Command: SaveCommand

### Behavior
- Should be enabled when there are unsaved changes to the catalog
- Saves catalog to the currently open file
- If no file is open, should invoke "Save As" dialog
- Shows progress indication during save
- Updates window title to remove unsaved indicator (if any)
- Updates status bar with save completion message

### Implementation Steps
1. Add SaveCommand to MainWindowViewModel
2. Track unsaved changes state in ViewModel
3. Create MenuItem binding in MainWindow XAML
4. Implement catalog serialization/save logic
5. Handle file I/O errors gracefully
6. Add keyboard shortcut binding
7. Integrate with File -> Save As
8. Add undo/redo support if applicable

### Change Tracking
- Monitor changes to catalog items
- Track property modifications
- Mark as "dirty" when changes occur
- Clear dirty flag after successful save

### Error Handling
- Handle file write errors
- Handle permission denied scenarios
- Handle invalid file paths
- Show appropriate error dialogs

### Testing
- Verify Save is disabled when no changes
- Verify Save is enabled after item modification
- Verify catalog is saved correctly
- Verify error messages on failure

## Related Issues
- #173: File -> Open menu item
- #174: File -> Save As menu item
- #172: File -> New menu item
- #189: Unsaved changes tracking
