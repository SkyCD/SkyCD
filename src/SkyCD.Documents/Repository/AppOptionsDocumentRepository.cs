using System;
using System.IO;
using SkyCD.Couchbase.Repository;

namespace SkyCD.Documents.Repository;

public sealed class AppOptionsDocumentRepository : RepositoryBase<AppOptionsDocument>
{
    public AppOptionsDocument GetOrCreateAppOptions()
    {
        return GetOrCreate(AppOptionsDocument.DocumentId, created =>
        {
            created.Window = new WindowOptionsDocument();
            created.Browser = new BrowserOptionsDocument();
            created.Language = "English";
            created.PluginPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Plugins"));
            created.AppStartCount = 0;
        });
    }
}
