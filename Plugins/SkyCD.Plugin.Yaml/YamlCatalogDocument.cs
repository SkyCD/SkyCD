using System.Collections.Generic;

namespace SkyCD.Plugin.Yaml;

internal sealed class YamlCatalogDocument
{
    public string? SchemaVersion { get; set; }
    public List<Dictionary<string, string?>>? Payload { get; set; }
}
