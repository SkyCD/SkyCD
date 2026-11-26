# Email metadata

`SkyCD.Plugin.Metadata.Email` registers `SkyCD.Plugin.Email` through assembly metadata and exposes version 1 of `skycd.email.metadata`. Indexing reads EML and MSG message facts and PST mailbox facts through the host's bounded media source.

- EML and MSG publish sender, subject, recipient counts, dates, identifiers, conversation topic, and body and attachment statistics where available. `BodyLength` counts UTF-16 code units in the plain-text body when available, with HTML and then RTF as fallbacks. Attachment names are limited to 16 names, 128 code units per name, and 1,024 code units for the combined field.
- PST publishes folder count and summaries of up to 2,048 regular mailbox items. When `ScanIncomplete` is true, the message count and received-date range describe the items examined, and an attachment finding is included only if an attachment was observed. Folder-level parser limits can fail extraction before summaries are available.
- The plugin reads at most 16 MiB, or the host's remaining read budget if smaller. Oversized files produce a failed provider result with a size-limit error in the host log. PST files that exceed this limit are common.
- The reader projects attachment metadata only. The plugin does not open attachment content streams, open linked files, resolve remote resources, or perform S/MIME operations.
- A password-protected store reports `Protected`, `ScanIncomplete`, and the OfficeIMO password exception type in `StoreDiagnosticCode`. PST password checksums represent access protection. Unsupported PST encryption is reported separately when OfficeIMO identifies it.
