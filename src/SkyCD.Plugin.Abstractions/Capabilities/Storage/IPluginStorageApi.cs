using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.Storage;

/// <summary>
/// One stored document as a neutral id plus a field map, so a plugin persists its own records without
/// referencing any host document type.
/// </summary>
public sealed record PluginDocumentRecord(string Id, IReadOnlyDictionary<string, object?> Fields);

/// <summary>
/// A single named document collection owned by one plugin. The host namespaces the name by the plugin id so
/// distinct plugins never collide, while a plugin can keep a stable collection name across releases and reuse
/// data it already wrote under the same name.
/// </summary>
public interface IPluginCollection
{
    string Name { get; }

    PluginDocumentRecord? Get(string id);

    IReadOnlyList<PluginDocumentRecord> GetAll();

    void Upsert(string id, IReadOnlyDictionary<string, object?> fields);

    void Delete(string id);
}

/// <summary>
/// Host-provided persistence a feature plugin uses for its own documents, isolated per plugin identity. The
/// host maps this onto the same attribute-driven document-store mechanism the core uses, so no separate
/// storage stack is introduced.
/// </summary>
public interface IPluginStorageApi
{
    /// <summary>Opens a collection owned by this plugin, creating it on first use.</summary>
    IPluginCollection OpenCollection(string name);
}
