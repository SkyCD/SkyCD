using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Librer.Parsing;

/// <summary>
/// Represents the header information of a Librer catalog.
/// </summary>
public sealed class LibrerHeader
{
    public string Label { get; set; } = string.Empty;
    public string ScanPath { get; set; } = string.Empty;
    public string Info { get; set; } = string.Empty;
    public long CreationTime { get; set; }
    public string CreationHost { get; set; } = string.Empty;
    public string CreationOS { get; set; } = string.Empty;
    public long QuantFiles { get; set; }
    public long QuantFolders { get; set; }
    public long SumSize { get; set; }
    public long Free { get; set; } = -1;
    public object? DataFormatVersion { get; set; }
    public long CdeSizeExtracted { get; set; }
    public int CompressionLevel { get; set; } = 9;
    public int ItemsNames { get; set; }
    public int ItemsCd { get; set; }
    public Dictionary<string, object?> RawFields { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static LibrerHeader FromDictionary(IDictionary<string, object?> dict)
    {
        var header = new LibrerHeader();
        foreach (var (key, value) in dict)
        {
            header.RawFields[key] = value;
        }

        if (dict.TryGetValue("label", out var labelVal) && labelVal is not null)
            header.Label = labelVal.ToString() ?? string.Empty;

        if (dict.TryGetValue("scan_path", out var scanPathVal) && scanPathVal is not null)
            header.ScanPath = scanPathVal.ToString() ?? string.Empty;

        if (dict.TryGetValue("info", out var infoVal) && infoVal is not null)
            header.Info = infoVal.ToString() ?? string.Empty;

        if (dict.TryGetValue("creation_time", out var creationTimeVal))
            header.CreationTime = ConvertToInt64(creationTimeVal);

        if (dict.TryGetValue("creation_host", out var hostVal) && hostVal is not null)
            header.CreationHost = hostVal.ToString() ?? string.Empty;

        if (dict.TryGetValue("creation_os", out var osVal) && osVal is not null)
            header.CreationOS = osVal.ToString() ?? string.Empty;

        if (dict.TryGetValue("quant_files", out var quantFilesVal))
            header.QuantFiles = ConvertToInt64(quantFilesVal);

        if (dict.TryGetValue("quant_folders", out var quantFoldersVal))
            header.QuantFolders = ConvertToInt64(quantFoldersVal);

        if (dict.TryGetValue("sum_size", out var sumSizeVal))
            header.SumSize = ConvertToInt64(sumSizeVal);

        if (dict.TryGetValue("free", out var freeVal))
            header.Free = ConvertToInt64(freeVal);

        if (dict.TryGetValue("data_format_version", out var dfvVal))
            header.DataFormatVersion = dfvVal;

        if (dict.TryGetValue("cde_size_extracted", out var cdeSizeVal))
            header.CdeSizeExtracted = ConvertToInt64(cdeSizeVal);

        if (dict.TryGetValue("compression_level", out var comprVal))
            header.CompressionLevel = (int)ConvertToInt64(comprVal);

        if (dict.TryGetValue("items_names", out var itemsNamesVal))
            header.ItemsNames = (int)ConvertToInt64(itemsNamesVal);

        if (dict.TryGetValue("items_cd", out var itemsCdVal))
            header.ItemsCd = (int)ConvertToInt64(itemsCdVal);

        return header;
    }

    private static long ConvertToInt64(object? val)
    {
        if (val is null) return 0;
        return val switch
        {
            sbyte sb => sb,
            byte b => b,
            short s => s,
            ushort us => us,
            int i => i,
            uint ui => ui,
            long l => l,
            ulong ul => (long)ul,
            float f => (long)f,
            double d => (long)d,
            decimal dec => (long)dec,
            string str when long.TryParse(str, out var parsed) => parsed,
            _ => 0
        };
    }
}
