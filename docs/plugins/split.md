# Split

File > Split... cuts the entries of the currently open catalog into part catalogs at entry
boundaries. It works on any catalog the app can open — legacy text, the JSON/XML catalog formats
and the database — because it splits the entries of the open catalog, not the bytes of one file
format. The legacy `SkyCD.Plugin.Feature.Split` Tools command that only understood `.scd` files is
retired; the host owns the command and the dialog.

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

Parts are written through the same save path as Save As, with the same catalog-scope data
(virtual collections, custom status definitions, scan exclusions, AI exclusions and auxiliary
account data) travelling into every part. Formats that do not preserve catalog scope show the
existing data-loss warning once for the whole run, not once per part. An existing file with a
target part name stops the split before anything is written. Parts are staged beside their target
name and published with a rename, and progress is reported per part. Cancelling, declining the
data-loss confirmation, or a failure halfway through deletes every part written by that run, so
the output folder never keeps a part that looks like a finished split. The source catalog is only
read: the entry list, the entry order and the source files stay untouched.
