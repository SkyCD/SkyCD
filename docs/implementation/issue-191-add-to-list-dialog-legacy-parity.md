# Issue 191: Add To List dialog uses non-legacy colors/layout

GitHub issue: https://github.com/SkyCD/SkyCD/issues/248

## Problem
The Avalonia `Add To List` dialog currently renders with a custom beige color palette and a structure that does not match legacy WinForms behavior.

This creates visual inconsistency versus other dialogs and legacy UI expectations.

## Scope
- Restore dialog layout structure to match legacy:
  - left source selector column
  - media/source input sections
  - bottom tab control (`All contents add...`, `Misc`)
  - right-aligned OK/Cancel row
- Remove non-legacy custom dialog background styling.
- Keep existing MVVM command and validation behavior.

## Acceptance Criteria
- Dialog opens at fixed legacy-like size and cannot be resized.
- Source selection is shown as a left vertical selector.
- Folder/Internet source fields appear in the source section based on source mode.
- Target placement and extended info options are available through bottom tabs.
- Colors no longer use the previous custom beige surface treatment.

## Implementation Notes
- Updated `src/SkyCD.App/Views/AddToListWindow.axaml`.
- No ViewModel or command logic changes required.
