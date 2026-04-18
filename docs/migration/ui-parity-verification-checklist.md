# UI Parity Verification Checklist (#142)

This document tracks UI parity between legacy VB.NET/WinForms SkyCD and v3 Avalonia implementation.
Each parity item is mapped to verification evidence (tests, screenshots, or manual verification).

## 1. Main Window Shell

### 1.1 Window Controls and Layout
- [x] **Status Bar Toggle**: `ToggleStatusBarCommand` hides/shows status bar
  - Verification: `MainWindowViewModelTests.ToggleStatusBarCommand_ChangesVisibility()`
  - Evidence: Unit test ✓

- [x] **Tree Browser Panel**: Displays folder hierarchy on left side
  - Verification: `MainWindowViewModelTests.Constructor_InitializesLegacyShellDefaults()`
  - Evidence: Unit test validates tree nodes present ✓

- [x] **List Browser Panel**: Displays items in selected folder
  - Verification: `MainWindowViewModelTests.SelectingDifferentTreeNode_RefreshesListAndSelectsFirstItem()`
  - Evidence: Unit test validates list refresh ✓

- [ ] **Window Position/Size Persistence**: AppOptions stores window state
  - Test: Manual - verify window position/size saved on close and restored on reopen
  - Screenshot baseline needed

### 1.2 View Mode (Icon/Tiles/Details)
- [x] **View Mode Selection**: `SetViewModeCommand` switches between Details, Tiles, LargeIcons
  - Verification: `MainWindowViewModelTests.SetViewModeCommand_UpdatesModeAndCheckedState()`
  - Evidence: Unit test ✓

- [x] **View Mode Flags**: Icon grid mode, list-like mode, tiles mode derived properties
  - Verification: `MainWindowViewModelTests.SetViewModeCommand_UpdatesDerivedLayoutFlags()`
  - Evidence: Unit test validates font size, grid width, column display ✓

- [ ] **Visual Rendering**: Icons, fonts, grid layout render correctly
  - Test: Manual - take screenshots of each view mode
  - Screenshot baseline needed

### 1.3 Sorting and Ordering
- [x] **Sort Mode Selection**: `SetSortModeCommand` switches between Name, Type, Date
  - Verification: `MainWindowViewModelTests.SetSortModeCommand_AppliesRequestedSortMode()`
  - Evidence: Unit test ✓

- [x] **Sort Reordering**: Items reorder when sort mode changes
  - Verification: `MainWindowViewModelTests.SetSortModeCommand_ChangesCurrentListOrdering()`
  - Evidence: Unit test validates item order changes (Classical Collection vs Concert-2025.flac) ✓

- [ ] **Correct Sorting**: Verify alphabetical, type-based, and date-based sorting
  - Test: Manual - verify sort results match expected order
  - Screenshot baseline needed

### 1.4 Tree Navigation
- [x] **Tree Node Expansion**: `ExpandSelectionCommand` expands nodes
  - Verification: `MainWindowViewModelTests.ExpandAndCollapseSelectionCommand_UpdatesSelectedTreeNodeExpansion()`
  - Evidence: Unit test ✓

- [x] **Tree Node Selection**: Selecting tree node refreshes list
  - Verification: `MainWindowViewModelTests.SelectingDifferentTreeNode_RefreshesListAndSelectsFirstItem()`
  - Evidence: Unit test validates first item selection ✓

- [ ] **Visual Tree Rendering**: Tree expand/collapse arrows, indentation
  - Test: Manual - take screenshot of tree with collapsed/expanded nodes
  - Screenshot baseline needed

### 1.5 Icon Glyphs
- [x] **Icon Glyphs Present**: All tree nodes and browser items have icon glyphs
  - Verification: `MainWindowViewModelTests.TreeAndListItems_ExposeIconGlyphs()`
  - Evidence: Unit test validates all items have non-empty glyphs ✓

- [ ] **Icon Visual Accuracy**: Glyphs match legacy (folder, file, document types)
  - Test: Manual - verify icon meanings match legacy
  - Screenshot baseline needed

## 2. Status Bar and Progress

### 2.1 Status Text Display
- [x] **Initial Status**: Status shows "Done." on startup
  - Verification: `MainWindowViewModelTests.Constructor_InitializesLegacyShellDefaults()`
  - Evidence: Unit test ✓

- [x] **Operation Status**: Status updates during operations (e.g., "Loading catalog...")
  - Verification: `MainWindowViewModelTests.OpenCatalogCommand_TracksLifecycleAndResetsProgressVisuals()`
  - Evidence: Unit test validates status transitions ✓

- [ ] **Status Message Clarity**: Messages are user-friendly
  - Test: Manual - verify messages match legacy
  - Baseline needed

### 2.2 Progress Bar
- [x] **Progress Visibility**: Progress bar shows/hides appropriately
  - Verification: `MainWindowViewModelTests.OpenCatalogCommand_TracksLifecycleAndResetsProgressVisuals()`
  - Evidence: Unit test validates visibility toggle ✓

- [x] **Progress Values**: Progress updates 0→35→80→100 during catalog load
  - Verification: `MainWindowViewModelTests.OpenCatalogCommand_TracksLifecycleAndResetsProgressVisuals()`
  - Evidence: Unit test validates progress transitions ✓

- [ ] **Visual Progress**: Progress bar renders correctly
  - Test: Manual - take screenshot during operation
  - Screenshot baseline needed

## 3. Command Buttons and Toolbar

### 3.1 File Operations
- [x] **Save Command State**: Initially disabled, enabled after Open
  - Verification: `MainWindowViewModelTests.OpenThenSave_UpdatesSaveCommandState()`
  - Evidence: Unit test ✓

- [x] **Delete Command State**: Only enabled when item selected
  - Verification: `MainWindowViewModelTests.DeleteCommand_EnabledOnlyWhenItemIsSelected()`
  - Evidence: Unit test ✓

- [x] **Add Item Command**: `AddItemCommand` raises dialog request
  - Verification: `MainWindowViewModelTests.AddItemCommand_RaisesAddToListRequest()`
  - Evidence: Unit test ✓

- [ ] **Toolbar Visual Layout**: Buttons positioned correctly
  - Test: Manual - take screenshot of toolbar
  - Screenshot baseline needed

## 4. Dialogs

### 4.1 Properties Dialog
- [x] **Confirm Button**: Sets DialogAccepted=true
  - Verification: `PropertiesDialogViewModelTests.ConfirmCommand_SetsDialogAcceptedTrue()`
  - Evidence: Unit test ✓

- [x] **Comments Tab**: Comments editable
  - Verification: `PropertiesDialogViewModelTests.Comments_CanBeModified()`
  - Evidence: Unit test ✓

- [x] **Info Tab Visibility**: Info tab shown only when properties exist
  - Verification: `PropertiesDialogViewModelTests.HasInfoTab_Is*WhenInfoPropertiesEmpty()`
  - Evidence: Unit test ✓

- [x] **Info Display**: Shows property/value pairs correctly
  - Verification: `MainWindowViewModelTests.OpenPropertiesCommand_RaisesRequestWithSelectedObjectValues()`
  - Evidence: Unit test validates info properties populated ✓

### 4.2 Add to List Dialog
- [x] **Input Validation**: Media source requires media name
  - Verification: `AddToListDialogViewModelTests.MediaSource_RequiresMediaName()`
  - Evidence: Unit test ✓

- [x] **Folder Source Validation**: Folder path required
  - Verification: `AddToListDialogViewModelTests.FolderSource_RequiresFolderPath()`
  - Evidence: Unit test ✓

- [x] **Confirmation**: Valid input allows confirm
  - Verification: `AddToListDialogViewModelTests.ValidFolderFlow_CanConfirmAndAccept()`
  - Evidence: Unit test ✓

### 4.3 Options Dialog
- [x] **Language Selection**: Dropdown initialized with languages
  - Verification: `OptionsDialogViewModelTests.Constructor_InitializesLanguageSelection()`
  - Evidence: Unit test ✓

- [x] **Plugin Refresh**: RefreshPluginsCommand raises event
  - Verification: `OptionsDialogViewModelTests.RefreshPluginsCommand_RaisesRefreshRequest()`
  - Evidence: Unit test ✓

### 4.4 Login Dialog
- [x] **Credential Validation**: Both username and password required
  - Verification: `LoginDialogViewModelTests.ConfirmCommand_RequiresBothUsernameAndPassword()`
  - Evidence: Unit test ✓

### 4.5 About Dialog
- [x] **Product Info**: Displays product name, version, website
  - Verification: `AboutDialogViewModelTests.Constructor_InitializesWithCustomValues()`
  - Evidence: Unit test ✓

## 5. Session State Persistence

### 5.1 Application Options
- [x] **View Mode Persistence**: `ApplySessionState` restores view mode
  - Verification: `MainWindowViewModelTests.ApplySessionState_RestoresViewSortAndStatusBarAndRefreshesOrdering()`
  - Evidence: Unit test ✓

- [x] **Sort Mode Persistence**: `ApplySessionState` restores sort mode
  - Verification: `MainWindowViewModelTests.ApplySessionState_RestoresViewSortAndStatusBarAndRefreshesOrdering()`
  - Evidence: Unit test ✓

- [x] **Status Bar Persistence**: `ApplySessionState` restores visibility
  - Verification: `MainWindowViewModelTests.ApplySessionState_RestoresViewSortAndStatusBarAndRefreshesOrdering()`
  - Evidence: Unit test ✓

- [ ] **Window Position**: Saved/restored via AppOptions
  - Test: Manual - close and reopen, verify position
  - Baseline needed

## 6. ViewModel Test Coverage Summary

| Component | Tests | Status |
|-----------|-------|--------|
| MainWindowViewModel | 20 | ✓ Complete |
| PropertiesDialogViewModel | 6 | ✓ Complete |
| AddToListDialogViewModel | 6 | ✓ Complete |
| OptionsDialogViewModel | 3 | ✓ Complete |
| LoginDialogViewModel | 2 | ✓ Complete |
| AboutDialogViewModel | 4 | ✓ Complete |
| AddingProgressDialogViewModel | 5 | ✓ Complete |
| **Total** | **46** | **✓** |

## 7. Manual Testing Checklist

These items require manual verification and screenshot baselines:
- [ ] Window position/size persistence
- [ ] View mode visual rendering (Details, Tiles, LargeIcons)
- [ ] Sort result accuracy
- [ ] Tree node visual rendering (expand/collapse, indentation)
- [ ] Icon glyph visual accuracy
- [ ] Status bar message clarity
- [ ] Progress bar visual rendering
- [ ] Toolbar button layout
- [ ] Dialog layouts and fonts
- [ ] Properties info display formatting

## 8. Screenshot Baseline Registry

Location: `artifacts/ui-baselines/`

Screenshots needed:
- `main-window-default.png` - Default main window state
- `main-window-details-view.png` - Details view mode
- `main-window-tiles-view.png` - Tiles view mode
- `main-window-large-icons-view.png` - Large icons view mode
- `dialog-properties.png` - Properties dialog
- `dialog-add-to-list.png` - Add to list dialog
- `dialog-options.png` - Options dialog
- `dialog-login.png` - Login dialog
- `dialog-about.png` - About dialog

## 9. CI Integration Status

- [x] ViewModel tests integrated into CI
- [ ] Screenshot baselines versioned
- [ ] Manual test checklist tracked
- [ ] Baseline artifacts published

## Notes

- This checklist maps to issue #142 requirements
- Test evidence section indicates verification method (unit test, manual, screenshot)
- Manual tests should be documented with dates and testers
- Screenshot baselines should be reviewed in PR before merge
