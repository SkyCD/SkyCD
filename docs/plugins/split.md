# Split

File > Split... cuts the entries of the currently open catalog into part catalogs at entry
boundaries. It works on any catalog the app can open — legacy text, the JSON/XML catalog formats
and the database — because it splits the entries of the open catalog, not the bytes of one file
format. Split is a feature plugin, `SkyCD.Plugin.Feature.Split` with the stable plugin id
`SkyCD.Plugin.Feature.Split` (the id is persisted in the plugin settings, so renames keep it), and
owns its menu entry, dialog and split service. The host no longer carries a Split command.

## Install and availability

Split ships as a normal first-party plugin in the host `Plugins` folder and is installed, enabled and
removed through Options > Plugins like any other plugin. Discovery reads the plugin assembly alone:
there is no `plugin.json`. The plugin contributes the File > Split... entry through
`IMenuPluginCapability.GetMenuContributions` only while it is loaded, so a host without the plugin
shows no Split entry and no separator left behind for the empty menu group. The entry needs an open
catalog and a loaded format that can write the parts; while no loaded format can write, the host
keeps the entry disabled, exactly as it does for Save As. After a plugin refresh — for example after
disabling or removing the plugin in Options — the entry disappears; re-enabling it brings the entry
back.

## What the dialog asks

The dialog collects the output folder (the source folder by default), the part file name pattern,
the maximum entries per part and an optional maximum part size as a secondary cap. It also asks for
the saving format: only formats that can be written and read back are offered, so every part reopens
as a valid catalog. The pattern defaults to `part #N` plus the chosen format's extension. `#N`
expands to the legacy `#1`, `#2` naming, so splitting to `.scd` reproduces the original SkyCD Split
output; `{n}`, `{total}` and `{source}` are also available. The pattern extension must belong to the
chosen format, and a pattern that expands to anything but a plain file name in the output folder is
rejected before a file is written. Max entries per part and max part size are independent, and a
part ends when either limit is reached; the size limit is measured against the bytes each part
serializes to, so a part file is never larger than the limit unless a single entry alone exceeds it.

## Writing the parts

Parts are written through the host save surface a plugin receives as `ICatalogSaveHostApi`, so a
plugin never binds to a catalog format. That surface uses the same save path as Save As, with the
same catalog-scope data (virtual collections, custom status definitions, scan exclusions, AI
exclusions and auxiliary account data) travelling into every part. Formats that do not preserve
catalog scope show the existing data-loss warning once for the whole run, not once per part. An
existing file with a target part name stops the split before anything is written. Parts are staged
beside their target name and published with a rename, and progress is reported per part in the
host-owned window. Cancelling, declining the data-loss confirmation, or a failure halfway through
deletes every part written by that run, so the output folder never keeps a part that looks like a
finished split. The source catalog is only read: the entry list, the entry order and the source
files stay untouched.

## Layout

The plugin follows the standard plugin project layout: `SplitPlugin` and `AssemblyInfo.cs` at the
root, menus and dialog wiring in `SplitPlugin`, limits, pattern rules and the split planner in
`Parsing`, dialog options and the view model in `ViewModels`, the host-embedded form in `Views`,
dialog messages in `Localization`, and its own exception types in `Exceptions`.
