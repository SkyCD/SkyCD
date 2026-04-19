# Issue #189: Data - Unsaved Changes Tracking Implementation

## Overview
Implement tracking and warning for unsaved changes to prevent data loss.

## Requirements

### Change Tracking
- Track when catalog data is modified
- Mark catalog as "dirty" when changes occur
- Clear dirty flag after successful save
- Track specific change types (item added/modified/deleted)

### User Warnings
- Show indicator in window title (e.g., "* Catalog.skc")
- Update status bar to show unsaved changes status
- Warn user before closing if unsaved changes exist
- Warn user before opening new file if unsaved changes exist
- Warn user before exiting application if unsaved changes exist

### Dialogs
- "Do you want to save changes before closing?"
- Buttons: "Save", "Don't Save", "Cancel"
- Show filename in warning message
- Include summary of changes (optional)

### Implementation Steps
1. Add IsDirty/HasChanges property to ViewModel
2. Hook item modification events
3. Track add/edit/delete operations
4. Monitor property changes in catalog
5. Update window title with dirty indicator
6. Implement close handlers to check for changes
7. Create warning dialogs
8. Handle save-on-close workflow

### Change Detection
- Monitor ObservableCollection changes
- Track property modifications via INotifyPropertyChanged
- Reset dirty flag only after successful save
- Differentiate between user changes and programmatic updates

### Testing
- Verify dirty flag set on item changes
- Verify dirty flag cleared on save
- Verify warning shown before close
- Verify data not lost when "Save" selected
- Verify data discarded when "Don't Save" selected

## Related Issues
- #185: File -> Save menu item
- #188: Window state persistence
