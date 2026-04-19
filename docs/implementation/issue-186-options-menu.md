# Issue #186: Tools -> Options Menu Item Implementation

## Overview
The 'Tools -> Options' menu item needs to be properly wired to open the Options dialog.

## Current Status
- OptionsWindow and OptionsDialogViewModel already exist
- Menu item may exist but is not properly connected

## Requirements

### Menu Item
- Location: Tools menu
- Label: "Options"
- Shortcut: Ctrl+Alt+O (optional)
- Command: OpenOptionsCommand

### Behavior
- Opens OptionsWindow as a modal dialog
- Centers on parent window
- Prevents interaction with main window until closed
- Persists user settings when OK clicked
- Discards changes when Cancel clicked
- Re-opens with previously selected tab

### Implementation Steps
1. Verify OpenOptionsCommand exists in MainWindowViewModel
2. Implement dialog service integration
3. Create MenuItem binding in MainWindow XAML
4. Pass current settings/languages to OptionsWindow
5. Handle dialog result (OK/Cancel)
6. Persist settings using ApplicationSettings
7. Apply language change if needed
8. Update UI if settings changed

### Settings Persistence
- Save plugin path
- Save language preference
- Save plugin enable/disable states
- Store in application configuration

### Testing
- Verify menu item opens dialog
- Verify settings are loaded correctly
- Verify changes are saved on OK
- Verify changes are discarded on Cancel
- Verify language change applies immediately

## Related Issues
- #165: Language selection should switch program language
- #163: Plugin checkboxes functionality
