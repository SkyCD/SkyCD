# Issue #188: UI - Window State Persistence Implementation

## Overview
The application should persist window state (size, position, maximized/normal) between sessions.

## Requirements

### Window Properties to Persist
- **Width** - Window width in pixels
- **Height** - Window height in pixels
- **Left** - X position on screen
- **Top** - Y position on screen
- **WindowState** - Normal, Maximized, or Minimized
- **Splitter Positions** - Any split panel divider positions

### Storage
- Save to application configuration file
- Load on application startup
- Reset to defaults if configuration is corrupted
- Handle multiple monitor scenarios gracefully

### Implementation Steps
1. Create window state model class
2. Implement save functionality on window closing
3. Implement load functionality on window opening
4. Handle DPI scaling for different monitors
5. Add validation for off-screen coordinates
6. Store in persistent settings (AppSettings/Config)
7. Restore to last known position
8. Gracefully handle missing settings

### Edge Cases
- Handle when saved position is off-screen
- Handle when window can't fit on current monitor
- Handle different screen resolutions
- Handle when monitors are disconnected
- Prevent saving minimized state

### Testing
- Verify window size persists
- Verify window position persists
- Verify maximized state persists
- Verify invalid positions are handled
- Verify multi-monitor scenarios work

## Related Issues
- #189: Unsaved changes tracking
