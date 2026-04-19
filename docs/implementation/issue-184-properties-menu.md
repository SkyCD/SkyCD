# Issue #184: Edit -> Properties Menu Item Implementation

## Overview
The 'Edit -> Properties' menu item needs to be implemented to open the Properties dialog for selected items.

## Requirements

### Menu Item
- Location: Edit menu
- Label: "Properties"
- Shortcut: Alt+Enter or Right-click context menu
- Command: OpenPropertiesCommand

### Behavior
- Should be enabled only when an item is selected
- Opens PropertiesWindow with details of selected item
- Dialog is modal (doesn't allow interaction with main window until closed)
- Shows tabs for different property categories (Info, Advanced, etc.)
- Updates item data when user clicks OK

### Implementation Steps
1. Add OpenPropertiesCommand to MainWindowViewModel
2. Create MenuItem binding in MainWindow XAML
3. Create dialog service for opening PropertiesWindow
4. Pass selected item to PropertiesWindow
5. Handle dialog result (OK/Cancel)
6. Update item data if properties changed
7. Bind context menu (right-click) to same command
8. Add keyboard shortcut handling

### Dialog Integration
- PropertiesWindow already exists in the codebase
- Create PropertiesDialogViewModel with selected item data
- Handle Save/Cancel operations
- Sync changes back to main window

### Testing
- Verify menu item is disabled when no item selected
- Verify Properties dialog opens with correct data
- Verify changes persist when OK is clicked
- Verify context menu also opens Properties

## Related Issues
- #183: Edit -> Delete menu item
- #190: Context menu implementation
