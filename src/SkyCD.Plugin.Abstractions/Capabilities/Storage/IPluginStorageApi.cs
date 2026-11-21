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
    /// <summary>Gets the collection name as seen by the owning plugin (without host namespacing).</summary>
    string Name { get; }

    /// <summary>Gets one record by id, or null when no record with that id exists.</summary>
    /// <param name="id">The document id.</param>
    PluginDocumentRecord? Get(string id);

    /// <summary>Gets every record in the collection.</summary>
    IReadOnlyList<PluginDocumentRecord> GetAll();

    /// <summary>Creates or replaces the record with the given id from its field map.</summary>
    /// <param name="id">The document id.</param>
    /// <param name="fields">The field values to persist.</param>
    void Upsert(string id, IReadOnlyDictionary<string, object?> fields);

    /// <summary>Deletes the record with the given id when it exists.</summary>
    /// <param name="id">The document id.</param>
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
