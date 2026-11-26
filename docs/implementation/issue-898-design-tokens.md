# Design tokens for spacing and typography (issue #898)

A single token scale replaces ad-hoc `FontSize`, `Spacing` and control-metric literals in XAML.
Tokens live in [DesignTokens.axaml](../../src/SkyCD.App/Styles/DesignTokens.axaml), merged into
`Application.Resources` by `App.axaml`; global defaults and typography classes live in
[GlobalStyles.axaml](../../src/SkyCD.App/Styles/GlobalStyles.axaml).

## Scale

| Token | Value | Use |
|---|---|---|
| `SkySpaceXxs` | 2 | hairline gap, dense legacy layouts |
| `SkySpaceXs` | 4 | inline gap between label and field |
| `SkySpaceSm` | 8 | default gap between related controls |
| `SkySpaceMd` | 12 | window padding, section gap |
| `SkySpaceLg` | 16 | wide section gap |
| `SkySpaceXl` | 24 | page-level separation |
| `SkySpaceXxl` | 32 | empty-state separation |
| `SkyFontCaption` | 11 | captions, hints, compact dialogs |
| `SkyFontSecondary` | 12 | default control and body text |
| `SkyFontBody` | 14 | primary reading text |
| `SkyFontSubtitle` | 16 | card and section headings |
| `SkyFontTitle` | 20 | dialog title |
| `SkyFontWindowTitle` | 24 | main window title |

Metrics: `SkyIconSize` (16, the toolbar button icon slot and the matching glyphs in the Options
toolbar lists), `SkyWindowPadding` (12), `SkySectionPadding` (12), `SkyControlPadding` (8,4),
`SkyInsetXxs`/`Xs`/`Sm`/`Md`/`Lg` (2/4/8/12/16 as `Thickness` for `Margin` and `Padding`),
`SkyCornerRadius` (4), `SkyCornerRadiusLarge` (8), `SkyFontFamily` (`Segoe UI, Inter`).
Small control radii are `SkyCornerRadiusSmall` (2) and `SkyCornerRadiusMedium` (3);
`SkyCornerRadiusNone` (0) keeps square corners where a surface cancels inherited rounding.
Radii that cannot round onto the ramp without changing the measured shape are named after
their surface (`SkyColorThumbRadius`, `SkyAcpAgentCardRadius`, `SkyAcpAgentBadgeRadius`).
The app keeps the classic Windows font (Segoe UI) and falls back to the packaged Inter font on
platforms without it. Spacing tokens are `double` values for `Spacing`/`RowSpacing`/`ColumnSpacing`;
use the `SkyInset*` `Thickness` tokens for `Margin`/`Padding`.

## Insets (issue #999)

`Margin` and `Padding` are the box insets of the same scale, not a free layer. They were the last
unruled surface, which is how one-off literals crept back into the padding of otherwise identical
panels. XAML cannot assemble a `Thickness` from the scalar `SkySpace*` ramp, so the dictionary
carries a `Thickness` vocabulary and a number belongs nowhere else.

Every `Margin`/`Padding` is one of:

| Form | Token | Use |
|---|---|---|
| `0` | none | cancel an inset inherited from a shared style |
| four equal sides | `SkyInsetXxs`…`SkyInsetLg` (2/4/8/12/16) | an even inset around a surface |
| one side | `SkyGapTop*`, `SkyGapBottom*`, `SkyGapLeft*`, `SkyGapRight*` | separate a block from its neighbour |
| one axis | `SkyGapY*`, `SkyGapX*` | separate rows or columns inside a stack |
| composed sides | `SkyRowPadding`, `SkyControlPadding`, `SkyControlPaddingTight`, `SkyTabHeaderPadding`, … | a surface whose sides differ, named for that surface |

A gap step uses the same ramp as `SkySpace*`: `Xxs` 2, `Xs` 4, `Sm` 8, `Md` 12, `Lg` 16, so
`SkyGapTopSm` is `0,8,0,0`. Add a token when markup needs a step or a side the vocabulary does not
have yet, name it after the step (`SkyGapBottomMd`) or the surface it shapes (`SkyRowPadding`), and
comment it when its value is tied to another metric: `SkyControlPaddingInverse` cancels
`SkyControlPadding`, `SkyTabCardBorderOverlap` overlaps the 1px card border it sits on. Never add a
per-file exception or a baseline of known violations — the gate is red until markup is migrated, and
the migration is what removes the drift.

## Usage rules

- Reference tokens with `{DynamicResource Sky...}` in XAML. Plugin and shared-control XAML
  resolves the same application-level keys, so host surfaces and plugin pages share one scale.
- Use the typography classes instead of sizes for headings: `Classes="h1"` (title),
  `Classes="h2"` (subtitle), `Classes="caption"`, `Classes="body"`.
- Dense legacy-parity dialogs set `Classes="compact"` on the `Window`; the caption scale then
  applies to their text, buttons and inputs without per-window font blocks.
- Do not add literal numeric `FontSize`, `Spacing`, `RowSpacing` or `ColumnSpacing` values.
- Do not add literal numeric `Margin` or `Padding` values, as an attribute or as a `Setter` value;
  pick the closest inset token. Rounding a hand-tuned pixel onto the ramp is the point: the ramp is
  what makes two dialogs read as one application. A value that cannot be rounded without breaking a
  measured relationship becomes a named token instead.
- A test that reads an inset off a control hosts the control in a shown `Window` first: the value is a
  dynamic resource, so a bare instance has no resource scope and reports the property default instead.
- Use theme brushes (`ThemeForegroundBrush`, `ThemeForegroundLowBrush`, `ThemeBorderLowBrush`,
  `ThemeBorderMidBrush`, `ThemeControlLowBrush`, `ThemeAccentBrush`) instead of hex colors.
  A surface that must keep one fixed color in both themes takes a `Sky*` brush token in
  `DesignTokens.axaml` instead — alert, success, warning and status colors, source-selection
  chrome, agent badge and selected-agent chrome, and dropdown chrome all live there
  (`SkyAlertErrorBackground`, `SkyErrorForeground`, `SkyStatusInfoForeground`, …).
  Literal hex may appear only in a brush definition (`Color="..."` in `DesignTokens.axaml`
  or a theme dictionary); every other position takes a brush reference.
- Do not add literal numeric `CornerRadius` values, as an attribute or as a `Setter` value;
  use a `SkyCornerRadius*` token.
- Do not open a column list with a literal pixel width. The leading column of a settings row
  is a `Sky*` label-width token: `SkyFormLabelWidth` (180, AI settings rows),
  `SkySettingsLabelWidth` (140, other settings rows), or a surface-named width for
  one-off chrome (sidebar, badge, source panel). Grids use the element form with
  `Width="{StaticResource ...}"` because an attribute value cannot mix a markup extension
  with the comma-separated column list; a column definition is not in the resource
  inheritance tree, so `DynamicResource` cannot resolve there and the load-time
  `StaticResource` (fail fast on typos, validated by the gate) is the working form.
  Star-sized and `Auto` columns need no token, and mid-list widths that size one specific
  field stay literal.

## Guard

`tools/scripts/ci/check-design-tokens.ps1` (also run from `validate.ps1`) fails on literal
numeric typography/spacing values, literal numeric `Margin`/`Padding` in any thickness form
(single, comma or space separated) including `Setter` values, hard-coded surface colors in any
AXAML file under `src/` or `Plugins/` (attributes and `Setter` values; `Color="..."` brush
definitions stay allowed), literal `CornerRadius` (attributes and `Setter` values), and literal
leading column widths (the `ColumnDefinitions` attribute and the first `ColumnDefinition`
element), plus references to undefined `Sky*` tokens. No per-file allowlists: the token file
itself and theme dictionaries hold the only literal colors by construction, because the gate
matches usages, not `Color="..."` definitions.

`DesignTokenUsageTests` and `DesignTokensTests` provide the same coverage in the test suites, and
`DesignTokenGateTests` runs the script itself against violating and compliant fixture markup for
the inset, color, radius and column rules. `OptionsAiProviderLayoutTests` loads the Options
window headless and asserts the AI form geometry the gate cannot see: one shared label width
across the provider and description rows, no label margin, actions aligned with the field
column, and a single section header with no in-card duplicate.

## Follow-ups

- Code-behind `FontSize` values in `SkyCD.UI` controls (`BrowserItemsView.axaml.cs`) still use
  literals.
- `new Thickness(...)` in code-behind and view models (`BrowserItemsView.axaml.cs`,
  `OptionsSidebarEntry.cs`, the windows that build rows from C#) stays outside the gate, which reads
  AXAML; migrating it means reading the same keys off the application resources.
- Literal control `Width`/`Height` (icon buttons, progress bars, fixed fields) and mid-list
  column widths stay outside the gate, which rules leading widths only; a shared metric token
  set for icon-button sizes is the natural next step if those drift.
