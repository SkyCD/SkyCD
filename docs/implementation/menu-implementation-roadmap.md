# Menu Implementation Roadmap

This document outlines the implementation requirements for menu functionality in SkyCD v3 to achieve parity with legacy version.

## Related Issues
- #169: BUG - Not all 'Help' menu items work
- #170: BUG - View -> Status Bar doesn't toggle visibility
- #178: BUG - File -> Exit doesn't work

## Required MenuBar Components

### File Menu
- **Exit** (#178)
  - Command: Exit/Close application
  - Action: Call `Application.Shutdown()` or close main window
  - Shortcut: Alt+F4 (system default)

### View Menu  
- **Status Bar** (#170)
  - Toggle checkbox command: `ToggleStatusBarCommand`
  - Property: Bind to `IsStatusBarVisible` in MainWindowViewModel
  - Shortcut: Could use Ctrl+Shift+S or similar
  - Behavior: Show/hide status bar in main window

### Help Menu
- **About** (#169)
  - Open AboutDialog
  - Show version, copyright, credits
- **Help Contents** (#169)
  - Link to documentation
  - Could open help file or web documentation
- **Check for Updates** (#169)
  - Check for new versions
  - Show update dialog if available

## Implementation Notes

- These features require a fuller MainWindow implementation with MenuBar
- Current bootstrap MainWindow doesn't have MenuBar
- Should follow plugin-based menu contribution pattern established in codebase
- Menu items should be properly wired to ViewModel commands
- Status bar visibility state should be persisted in AppOptions

## Dependencies
- MainWindowViewModel needs enhanced methods
- Main window needs MenuBar in XAML
- AboutDialog window needs implementation
- Documentation/help content needs to be available
