using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Runtime.Documents;

namespace SkyCD.Plugin.Runtime.Repositories;

public sealed class PluginConfigurationRepository : RepositoryBase<PluginConfigurationDocument>
{
    public new IReadOnlyList<PluginConfigurationDocument> GetAll()
    {
        return base.GetAll()
            .Where(static mapped => !string.IsNullOrWhiteSpace(mapped.Id))
            .Select(static mapped =>
            {
                mapped.Settings ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                return mapped;
            })
            .ToList();
    }
}
