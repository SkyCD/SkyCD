using Couchbase.Lite;
using SkyCD.App.Models;
using SkyCD.App.Services.Documents;
using System;

namespace SkyCD.App.Services;

public sealed class AppOptionsStore
{
    private readonly CouchbaseLocalStore localStore;

    public AppOptionsStore(CouchbaseLocalStore localStore)
    {
        this.localStore = localStore;
    }

    public AppOptions Load()
    {
        var settingsCollection = localStore.GetCollection(LocalCollection.Settings);
        using var document = settingsCollection.GetDocument(CouchbaseLocalStore.AppOptionsDocumentId);
        return AppOptionsDocument.FromDocument(document)?.ToAppOptions() ?? new AppOptions();
    }

    public void Save(AppOptions options)
    {
        var settingsCollection = localStore.GetCollection(LocalCollection.Settings);
        using var document = AppOptionsDocument.FromAppOptions(options)
            .ToMutableDocument(CouchbaseLocalStore.AppOptionsDocumentId);
        settingsCollection.Save(document);
    }
}
