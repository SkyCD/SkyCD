# CDCollect version-4 interoperability fixture

`cds3.db` and `thumbs/7/7_0.png` are synthetic fixtures authored for SkyCD, under the repository license. No upstream database, source code, artwork, or user data is included.

The database independently expresses observed CDCollect SVN r138 format facts: tables and column names/types, schema version 4, directory=0/file=1, colon-separated tree indices, and PNG sidecar naming `thumbs/<cd_id>/<cd_id>_<path>.png`. Reference: https://svn.code.sf.net/p/cdcollect/code/cdcollect/trunk/src/ (DBsql.cs, Conf.cs, FileItem.cs). No GPL implementation is copied.

Includes two media, an empty medium, Unicode, exact whitespace information, folders, a packed archive with a child, MIME types, dates, keywords, icons, and inert executable configuration. This is a schema interoperability fixture, not evidence of a GUI run in CDCollect 0.6.x.
