# Issue #190: UI - Context Menu for Items Implementation

## Overview
A right-click context menu should be available for items in the list/tree view to provide quick access to item operations.

## Requirements

### Menu Items
1. **Add** - Add new item to catalog
2. **Edit/Properties** - Open Properties dialog for selected item
3. **Separator**
4. **Delete** - Delete selected item(s)
5. **Separator**
6. **Copy** - Copy item details (optional)
7. **Paste** - Paste copied item (optional)
8. **Cut** - Cut item (optional)

### Behavior
- Menu appears on right-click in list/tree view
- Menu is disabled/hidden when no item is under cursor
- Menu items enable/disable based on context
- Keyboard shortcuts shown in menu
- Uses same commands as menu/toolbar items
- Supports multi-selection operations

### Implementation Steps
1. Create ContextMenu in list/tree view XAML
2. Define MenuItem bindings
3. Implement context menu opening logic
4. Enable/disable items based on selection state
5. Wire menu items to ViewModel commands
6. Handle right-click on empty space (no menu)
7. Support keyboard menu trigger (Shift+F10)
8. Style to match application theme

### Integration
- Share commands with main menu and toolbar
- Enable/disable logic matches button states
- Use same icons for consistency
- Implement copy/paste if supported by data

### Testing
- Verify context menu appears on right-click
- Verify menu doesn't appear on empty space
- Verify menu items are correctly enabled/disabled
- Verify menu items execute correct commands
- Verify keyboard trigger (Shift+F10) works
- Verify multi-selection works correctly

## Related Issues
- #183: Edit -> Delete menu item
- #184: Edit -> Properties menu item
- #177: Edit -> Add modal
