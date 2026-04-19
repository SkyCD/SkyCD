# PR #161: Legacy Tab Styling for WinForms Parity

## Closes
- #161 UI: Tabs should look more like old version

## Summary
This PR updates the tab control styling in the Avalonia UI to match the legacy VB.NET/WinForms appearance more closely. The modern Fluent theme tabs are replaced with a simpler, flatter styling that provides visual parity with the original SkyCD desktop application.

## Changes
- **New File**: `src/SkyCD.App/Styles/TabControlStyles.axaml`
  - Custom TabControl and TabItem styles implementing legacy appearance
  - Flat design with simple borders (matching WinForms look)
  - Color scheme using grays similar to Windows Classic theme
  - Selected and hover states for visual feedback
  
- **Modified**: `src/SkyCD.App/App.axaml`
  - Added StyleInclude reference to the new TabControlStyles.axaml
  - Styles are loaded after FluentTheme for proper precedence
  
- **Modified**: `src/SkyCD.App/SkyCD.App.csproj`
  - Added `Styles\**` to AvaloniaResource includes to bundle style files

## Visual Changes
Tabs now display with:
- Flat, simple borders (1px gray borders)
- Gray inactive tab background (#E8E8E8)
- White selected tab background (consistent with content area)
- Light gray hover effect for better interactivity
- Matches the appearance of WinForms TabControl tabs

## Testing
- ✅ Build: Successful (no errors or warnings)
- ✅ Tests: All 119 tests passing
  - SkyCD.App.Tests: 59 tests ✅
  - SkyCD.Domain.Tests: 2 tests ✅
  - SkyCD.Plugin.Runtime.Tests: 4 tests ✅
  - SkyCD.LegacyFormats.Tests: 8 tests ✅
  - SkyCD.Infrastructure.Tests: 2 tests ✅
  - SkyCD.Migration.Tests: 1 test ✅
  - SkyCD.Plugin.Host.Tests: 43 tests ✅
- ✅ Code Format: Applied via `dotnet format SkyCD.slnx`

## Affected Dialogs
The styling applies to all tabs in:
- Properties Dialog
- Options Dialog
- Add to List Dialog
- Any other TabControl usage in the application

## Notes
- No breaking changes to APIs or functionality
- Style is purely visual and doesn't affect behavior
- Compatible with both light and dark theme variants
