This PR completes the work for issue #127 by addressing the remaining items that were missed in the original fix.

## Changes Made
- **Updated 5 remaining plugin versions** from 1.0.0 to 2.0.0:
  - SkyCD.Plugin.Legacy.Ascd
  - SkyCD.Plugin.Legacy.Cscd  
  - SkyCD.Plugin.Legacy.Scd
  - SkyCD.Plugin.Sample.Menu
  - SkyCD.Plugin.Sample.Modal

- **Removed v3 references from documentation**:
  - Updated CONTRIBUTING.md to use "main stack" instead of "v3 stack"
  - Updated README.md to remove "v3" references
  - Changed all solution name references from `SkyCD.V3.slnx` to `SkyCD.slnx`

## Verification
- All plugin files now have consistent version 2.0.0
- No remaining v3 references in documentation
- Build completes successfully with 0 warnings/errors
- All references point to the correct solution file

Fixes #127
