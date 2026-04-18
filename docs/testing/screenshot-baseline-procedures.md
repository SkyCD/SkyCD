# Screenshot Baseline Procedures (#142)

This document describes the process for capturing, storing, and reviewing screenshot baselines for UI parity testing.

## 1. Directory Structure

```
artifacts/
└── ui-baselines/
    ├── screenshots/
    │   ├── main-window/
    │   │   ├── default-state.png
    │   │   ├── details-view.png
    │   │   ├── tiles-view.png
    │   │   └── large-icons-view.png
    │   ├── dialogs/
    │   │   ├── properties-dialog.png
    │   │   ├── add-to-list-dialog.png
    │   │   ├── options-dialog.png
    │   │   ├── login-dialog.png
    │   │   └── about-dialog.png
    │   └── tree-expansion/
    │       ├── tree-collapsed.png
    │       └── tree-expanded.png
    ├── metadata.json
    └── README.md
```

## 2. Baseline Metadata File

File: `artifacts/ui-baselines/metadata.json`

Structure:
```json
{
  "version": "1.0",
  "created": "2026-04-18",
  "updated": "2026-04-18",
  "baselineName": "v3.0.0-avalonia-launch",
  "os": ["windows-latest", "macos-latest", "linux-latest"],
  "screenshots": [
    {
      "id": "main-window-default",
      "path": "screenshots/main-window/default-state.png",
      "description": "Main window in default state",
      "viewMode": "Details",
      "sortMode": "Name",
      "statusBarVisible": true,
      "capturedOn": "2026-04-18",
      "capturedBy": "gh-actions"
    },
    {
      "id": "main-window-tiles",
      "path": "screenshots/main-window/tiles-view.png",
      "description": "Main window in Tiles view mode",
      "viewMode": "Tiles",
      "sortMode": "Name",
      "statusBarVisible": true,
      "capturedOn": "2026-04-18",
      "capturedBy": "gh-actions"
    }
  ]
}
```

## 3. Screenshot Capture Procedure

### Prerequisites
- Application running on clean state
- Standard screen resolution (1280x720 minimum)
- Consistent font rendering

### Steps

1. **Initialize State**
   - Restart application
   - Select default catalog (library)
   - Ensure status bar visible

2. **Capture Main Window Default**
   - Command: View Mode = Details, Sort Mode = Name
   - Filename: `main-window/default-state.png`

3. **Capture View Modes**
   - Set to Tiles mode
   - Filename: `main-window/tiles-view.png`
   - Set to LargeIcons mode
   - Filename: `main-window/large-icons-view.png`
   - Return to Details mode

4. **Capture Tree Expansion**
   - Expand first folder node
   - Filename: `tree-expansion/tree-expanded.png`
   - Collapse node
   - Filename: `tree-expansion/tree-collapsed.png`

5. **Capture Dialogs**
   - Open Properties dialog on first item
   - Filename: `dialogs/properties-dialog.png`
   - Open Add to List dialog
   - Filename: `dialogs/add-to-list-dialog.png`
   - Open Options dialog
   - Filename: `dialogs/options-dialog.png`
   - Open Login dialog
   - Filename: `dialogs/login-dialog.png`
   - Open About dialog
   - Filename: `dialogs/about-dialog.png`

## 4. CI Screenshot Capture

Configuration for GitHub Actions:

```yaml
- name: Capture UI Baselines (Windows)
  if: runner.os == 'Windows'
  run: |
    # Build and run screenshot capture tool
    dotnet build tools/SkyCD.ScreenshotCapture/SkyCD.ScreenshotCapture.csproj
    dotnet run --project tools/SkyCD.ScreenshotCapture -- \
      --output artifacts/ui-baselines/screenshots \
      --metadata artifacts/ui-baselines/metadata.json
  
- name: Upload Baseline Artifacts
  uses: actions/upload-artifact@v3
  with:
    name: ui-baselines-${{ runner.os }}
    path: artifacts/ui-baselines/
    retention-days: 90
```

## 5. Baseline Review Process

1. **Automated Comparison**
   - CI captures baselines on all platforms
   - New screenshots compared against approved baselines
   - Differences flagged for review

2. **Manual Review**
   - Maintainers review new/changed screenshots
   - Verify visual correctness
   - Comment on PR with approval

3. **Approval Criteria**
   - All dialogs render without artifacts
   - Text is readable
   - Icons are visible
   - Layout matches spec
   - Cross-platform consistency

4. **Baseline Update**
   - PR must include screenshot approval
   - Baselines committed alongside code
   - Metadata updated with reviewer info

## 6. Baseline Versioning

Baselines are tagged by version and platform:

- Version: Semantic version of SkyCD (e.g., v3.0.0)
- Platform: windows-latest, macos-latest, linux-latest
- Tagged in git: `baselines/v3.0.0-{platform}`

Example commit message:
```
chore: baseline ui screenshots for v3.0.0

- Add main window view mode screenshots
- Add dialog baseline screenshots
- Verify cross-platform consistency

Approves: ui-parity for issue #142
```

## 7. Pixel-Diff Tolerance

For future regression testing:

- Font rendering: ±5% pixel variance
- Color rendering: ±3% color shift
- Layout: ±1px offset tolerance
- Ignored regions: System clock, timestamps

## 8. Tools and Scripts

Helper scripts for baseline management:

### `scripts/capture-ui-baselines.ps1`
Captures all UI baselines locally:
```powershell
. scripts/capture-ui-baselines.ps1 -OutputDir artifacts/ui-baselines
```

### `scripts/compare-ui-baselines.ps1`
Compares current baselines against approved:
```powershell
. scripts/compare-ui-baselines.ps1 `
  -Current artifacts/ui-baselines `
  -Approved approved-baselines
```

## 9. Maintenance Schedule

- **Weekly**: CI captures baselines on all PRs
- **Monthly**: Maintainers review and approve
- **Quarterly**: Archive old baselines
- **Annual**: Clean up deprecated baselines

## 10. Known Visual Differences

Current expected visual differences between platforms:

| Element | Windows | macOS | Linux | Notes |
|---------|---------|-------|-------|-------|
| Font rendering | Native | Native | Native | Platform default fonts |
| Tree indent | 16px | 16px | 16px | Consistent |
| Glyph rendering | ClearType | CoreGraphics | FreeType | Sub-pixel rendering |
| Button style | Fluent | Fluent | Fluent | Avalonia theme |

## References

- Issue #142: UI parity regression tests
- Issue #129: UI Parity Epic
- [Avalonia Testing Guide](https://docs.avaloniaui.net/docs/get-started/test-your-app)
- [screenshot-baseline-procedures.md](./screenshot-baseline-procedures.md)
