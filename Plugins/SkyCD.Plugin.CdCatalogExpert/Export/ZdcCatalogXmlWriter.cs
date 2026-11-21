using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using SkyCD.Plugin.CdCatalogExpert.Exceptions;
using SkyCD.Plugin.CdCatalogExpert.Models;

namespace SkyCD.Plugin.CdCatalogExpert.Export;

/// <summary>
/// Deterministic Windows-1252 XML writer for <c>zdc_index.xml</c> and disc
/// tree members.
/// </summary>
/// <remarks>
/// The declaration always declares <c>encoding="windows-1252"</c>. Attribute
/// values are escaped by <see cref="XmlWriter"/> including CR, LF, quotes,
/// ampersands, and angle brackets. Raw numeric values are emitted verbatim as
/// invariant decimal text. A character that Windows-1252 cannot represent
/// fails with <see cref="ZdcCharacterNotRepresentableException"/> instead of
/// being replaced.
/// </remarks>
public static class ZdcCatalogXmlWriter
{
    private static readonly Encoding Windows1252 = CreateWindows1252();

    static ZdcCatalogXmlWriter()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Serializes the catalog index member.</summary>
    public static byte[] WriteIndex(ZdcCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ValidateCatalog(catalog);
        return Write(writer =>
        {
            writer.WriteStartElement("Z");
            writer.WriteAttributeString("N", catalog.Name);
            WriteOptionalAttribute(writer, "D", catalog.Description);
            WriteExtensions(writer, catalog.ExtensionAttributes);
            foreach (var child in catalog.Children)
            {
                switch (child)
                {
                    case ZdcGroup group:
                        WriteGroup(writer, group);
                        break;
                    case ZdcDisc disc:
                        WriteDisc(writer, disc);
                        break;
                    default:
                        throw new ZdcFormatException($"unsupported catalog entry '{child.GetType().Name}'.");
                }
            }

            writer.WriteEndElement();
        });
    }

    /// <summary>Serializes one disc-tree member.</summary>
    public static byte[] WriteDiscTree(ZdcDiscTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ValidateDiscTree(tree);
        return Write(writer =>
        {
            writer.WriteStartElement("R");
            foreach (var entry in tree.Entries)
            {
                WriteTreeEntry(writer, entry);
            }

            writer.WriteEndElement();
        });
    }

    private static byte[] Write(Action<XmlWriter> body)
    {
        using var buffer = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = Windows1252,
            Indent = false,
            NewLineHandling = NewLineHandling.Entitize,
            CheckCharacters = true,
            CloseOutput = false
        };

        try
        {
            using var writer = XmlWriter.Create(buffer, settings);
            body(writer);
            writer.Flush();
        }
        catch (ArgumentException exception)
        {
            throw new ZdcFormatException(exception.Message);
        }

        return buffer.ToArray();
    }

    private static void WriteGroup(XmlWriter writer, ZdcGroup group)
    {
        writer.WriteStartElement("G");
        writer.WriteAttributeString("N", group.Name);
        WriteOptionalAttribute(writer, "C", group.Timestamp);
        WriteExtensions(writer, group.ExtensionAttributes);
        foreach (var child in group.Children)
        {
            switch (child)
            {
                case ZdcGroup nested:
                    WriteGroup(writer, nested);
                    break;
                case ZdcDisc disc:
                    WriteDisc(writer, disc);
                    break;
                default:
                    throw new ZdcFormatException($"unsupported group entry '{child.GetType().Name}'.");
            }
        }

        writer.WriteEndElement();
    }

    private static void WriteDisc(XmlWriter writer, ZdcDisc disc)
    {
        writer.WriteStartElement("L");
        writer.WriteAttributeString("N", disc.Name);
        writer.WriteAttributeString("F", disc.GuidText);
        WriteOptionalAttribute(writer, "P", disc.SourcePath);
        WriteOptionalAttribute(writer, "C", disc.Created);
        WriteOptionalAttribute(writer, "M", disc.Modified);
        WriteOptionalAttribute(writer, "D", disc.Description);
        WriteOptionalAttribute(writer, "V", disc.VolumeLabel);
        WriteOptionalAttribute(writer, "I", disc.FileSystem);
        WriteOptionalAttribute(writer, "T", disc.MediaType);
        WriteOptionalAttribute(writer, "E", disc.VolumeSerial);
        WriteOptionalAttribute(writer, "K", disc.Capacity);
        WriteOptionalAttribute(writer, "R", disc.FreeSpace);
        WriteOptionalAttribute(writer, "O", disc.Options);
        WriteOptionalAttribute(writer, "SO_A", disc.ScanFlagA);
        WriteOptionalAttribute(writer, "SO_B", disc.ScanFlagB);
        WriteOptionalAttribute(writer, "SO_C", disc.ScanFlagC);
        WriteOptionalAttribute(writer, "SO_D", disc.ScanFlagD);
        WriteOptionalAttribute(writer, "SO_E", disc.ScanFlagE);
        WriteOptionalAttribute(writer, "SO_F", disc.ScanFlagF);
        WriteOptionalAttribute(writer, "SO_G", disc.ScanFlagG);
        WriteOptionalAttribute(writer, "G", disc.CoverMemberName);
        WriteOptionalAttribute(writer, "W", disc.Keywords);
        WriteOptionalAttribute(writer, "DS", disc.StorageLocation);
        WriteOptionalAttribute(writer, "DI", disc.IndexCode);
        WriteOptionalAttribute(writer, "DB", disc.Barcode);
        WriteOptionalAttribute(writer, "DR", disc.Rating);
        WriteExtensions(writer, disc.ExtensionAttributes);
        writer.WriteEndElement();
    }

    private static void WriteTreeEntry(XmlWriter writer, ZdcTreeEntry entry)
    {
        writer.WriteStartElement(entry.IsDirectory ? "D" : "F");
        writer.WriteAttributeString("N", entry.Name);
        WriteOptionalAttribute(writer, "A", entry.Attributes);
        WriteOptionalAttribute(writer, "M", entry.Modified);
        WriteOptionalAttribute(writer, "C", entry.Created);
        WriteOptionalAttribute(writer, "B", entry.TimestampB);
        WriteOptionalAttribute(writer, "D", entry.Description);
        WriteOptionalAttribute(writer, "W", entry.Keywords);
        if (!entry.IsDirectory)
        {
            writer.WriteAttributeString("S", entry.Size ?? "0");
        }

        WriteExtensions(writer, entry.ExtensionAttributes);
        foreach (var child in entry.Children)
        {
            WriteTreeEntry(writer, child);
        }

        writer.WriteEndElement();
    }

    private static void WriteOptionalAttribute(XmlWriter writer, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            writer.WriteAttributeString(name, value);
        }
    }

    private static void WriteExtensions(XmlWriter writer, List<KeyValuePair<string, string>> extensions)
    {
        foreach (var extension in extensions)
        {
            writer.WriteAttributeString(extension.Key, extension.Value);
        }
    }

    private static void ValidateCatalog(ZdcCatalog catalog)
    {
        EnsureRepresentable("Z.N", catalog.Name);
        EnsureRepresentable("Z.D", catalog.Description);
        ValidateExtensions("Z", catalog.ExtensionAttributes);
        foreach (var child in catalog.Children)
        {
            switch (child)
            {
                case ZdcGroup group:
                    ValidateGroup(group);
                    break;
                case ZdcDisc disc:
                    ValidateDisc(disc);
                    break;
                default:
                    throw new ZdcFormatException($"unsupported catalog entry '{child.GetType().Name}'.");
            }
        }
    }

    private static void ValidateGroup(ZdcGroup group)
    {
        EnsureRepresentable("G.N", group.Name);
        EnsureRepresentable("G.C", group.Timestamp);
        ValidateExtensions("G", group.ExtensionAttributes);
        foreach (var child in group.Children)
        {
            switch (child)
            {
                case ZdcGroup nested:
                    ValidateGroup(nested);
                    break;
                case ZdcDisc disc:
                    ValidateDisc(disc);
                    break;
                default:
                    throw new ZdcFormatException($"unsupported group entry '{child.GetType().Name}'.");
            }
        }
    }

    private static void ValidateDisc(ZdcDisc disc)
    {
        EnsureRepresentable("L.N", disc.Name);
        EnsureRepresentable("L.F", disc.GuidText);
        EnsureRepresentable("L.P", disc.SourcePath);
        EnsureRepresentable("L.C", disc.Created);
        EnsureRepresentable("L.M", disc.Modified);
        EnsureRepresentable("L.D", disc.Description);
        EnsureRepresentable("L.V", disc.VolumeLabel);
        EnsureRepresentable("L.I", disc.FileSystem);
        EnsureRepresentable("L.T", disc.MediaType);
        EnsureRepresentable("L.E", disc.VolumeSerial);
        EnsureRepresentable("L.K", disc.Capacity);
        EnsureRepresentable("L.R", disc.FreeSpace);
        EnsureRepresentable("L.O", disc.Options);
        EnsureRepresentable("L.SO_A", disc.ScanFlagA);
        EnsureRepresentable("L.SO_B", disc.ScanFlagB);
        EnsureRepresentable("L.SO_C", disc.ScanFlagC);
        EnsureRepresentable("L.SO_D", disc.ScanFlagD);
        EnsureRepresentable("L.SO_E", disc.ScanFlagE);
        EnsureRepresentable("L.SO_F", disc.ScanFlagF);
        EnsureRepresentable("L.SO_G", disc.ScanFlagG);
        EnsureRepresentable("L.G", disc.CoverMemberName);
        EnsureRepresentable("L.W", disc.Keywords);
        EnsureRepresentable("L.DS", disc.StorageLocation);
        EnsureRepresentable("L.DI", disc.IndexCode);
        EnsureRepresentable("L.DB", disc.Barcode);
        EnsureRepresentable("L.DR", disc.Rating);
        ValidateExtensions("L", disc.ExtensionAttributes);
    }

    private static void ValidateDiscTree(ZdcDiscTree tree)
    {
        foreach (var entry in tree.Entries)
        {
            ValidateTreeEntry(entry);
        }
    }

    private static void ValidateTreeEntry(ZdcTreeEntry entry)
    {
        var prefix = entry.IsDirectory ? "D" : "F";
        EnsureRepresentable($"{prefix}.N", entry.Name);
        EnsureRepresentable($"{prefix}.A", entry.Attributes);
        EnsureRepresentable($"{prefix}.M", entry.Modified);
        EnsureRepresentable($"{prefix}.C", entry.Created);
        EnsureRepresentable($"{prefix}.B", entry.TimestampB);
        EnsureRepresentable($"{prefix}.D", entry.Description);
        EnsureRepresentable($"{prefix}.W", entry.Keywords);
        EnsureRepresentable($"{prefix}.S", entry.Size);
        ValidateExtensions(prefix, entry.ExtensionAttributes);
        foreach (var child in entry.Children)
        {
            ValidateTreeEntry(child);
        }
    }

    private static void ValidateExtensions(
        string elementName,
        List<KeyValuePair<string, string>> extensions)
    {
        foreach (var extension in extensions)
        {
            EnsureRepresentable($"{elementName}@{extension.Key}", extension.Key);
            EnsureRepresentable($"{elementName}@{extension.Key}", extension.Value);
        }
    }

    private static void EnsureRepresentable(string field, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        foreach (var character in value)
        {
            if (!IsXmlCharacter(character) || !IsWindows1252Character(character))
            {
                throw new ZdcCharacterNotRepresentableException(field, character);
            }
        }
    }

    private static bool IsXmlCharacter(char character) =>
        character is '\t' or '\n' or '\r' ||
        (character >= 0x20 && character != 0xFFFE && character != 0xFFFF);

    private static bool IsWindows1252Character(char character)
    {
        try
        {
            Windows1252.GetByteCount(stackalloc char[] { character });
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}
