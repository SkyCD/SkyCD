using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.App;

public sealed class CatalogSaveService(FileFormatManager formats)
{
    public Task<FileFormatWriteResult> SaveAsync(string path, IReadOnlyList<CatalogDocument> documents)
    {
        var formatId = formats.ResolveFormatId(null, path, forWrite: true);
        var rows = documents.Select(document => new Dictionary<string, object?>
        {
            ["id"] = document.Id,
            ["parentId"] = document.ParentId,
            ["name"] = document.Name,
            ["type"] = document.Type.ToString(),
            ["size"] = document.Size,
            ["childrenCount"] = document.ChildrenCount,
            ["contentsState"] = document.ContentsState.ToString(),
            ["status"] = document.Status,
            ["properties"] = document.Properties
        }).ToList();
        return formats.WriteFileAsync(formatId, path, rows);
    }
}
