# Split

Build `Plugins/SkyCD.Plugin.Feature.Split` and install its assembly through the normal plugin loader. Discovery uses assembly capabilities; no plugin.json is required. The plugin references
`Plugins/SkyCD.Plugin.FileFormat.LegacySkyCD` so parts are written by the `legacy-scd` writer instead of a second copy of the grammar.

Tools > Split Catalog... opens a dialog for the catalog that is currently open. The command is offered for any catalog but reports the reason and stops when the file is not a legacy SkyCD text catalog, including the legacy binary layout. The source is only read: the entry list, the entry order and the file bytes stay untouched.

The dialog collects an output folder (the source folder by default), a part file name pattern and at least one limit. `#N` expands to the legacy `#1`, `#2` naming, so the default `part #N.scd` reproduces the original SkyCD Split output; `{n}`, `{total}` and `{source}` are also available. A pattern that expands to anything but a plain file name in the output folder is rejected before a file is written. Max entries per part and max part size are independent, and a part ends when either limit is reached; the size limit is measured against the bytes each entry line actually serializes to, including the part preamble, so a part file is never larger than the limit unless a single entry alone exceeds it.

Parts are staged beside their target name and published with a rename, and progress is reported per part. Cancelling, or a failure halfway through, deletes every part written by that run, so the output folder never keeps a part that looks complete. An existing file with the target part name stops the split instead of overwriting it.
