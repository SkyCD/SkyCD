using System;
using System.Collections.Generic;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.Metadata.VideoProjects.Exceptions;
using SkyCD.Plugin.Metadata.VideoProjects.Models;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

/// <summary>
/// Reads the XML form of an After Effects project. The XML form keeps every chunk body as lowercase hex in a
/// <c>bdata</c> attribute, so the version word and composition record decode exactly as in the binary form.
/// </summary>
internal static class AfterEffectsXmlReader
{
    private const int MaxNestingDepth = 16;

    public static VideoProjectFacts? Read(
        XmlReader reader, VideoProjectPluginOptions options, CancellationToken cancellationToken)
    {
        string? applicationVersion = null;
        long compositionCount = 0;
        long footageCount = 0;
        long folderCount = 0;
        string? firstCompositionName = null;
        byte[]? firstCompositionData = null;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.Depth > ProjectXml.MaxDepth)
            {
                throw new VideoProjectLimitExceededException("XML depth", ProjectXml.MaxDepth);
            }

            if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "head" when applicationVersion is null:
                    applicationVersion = AeVersion.DecodeHead(
                        HexPrefix.Decode(reader.GetAttribute("bdata"), 32));
                    break;

                case "svap" when applicationVersion is null:
                    applicationVersion = AeVersion.DecodeWord(
                        HexPrefix.Decode(reader.GetAttribute("bdata"), 4));
                    break;

                case "Fold":
                    ScanFolder(reader, options, cancellationToken, 1, ref compositionCount, ref footageCount,
                        ref folderCount, ref firstCompositionName, ref firstCompositionData);
                    break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        string? resolution = null;
        double? frameRate = null;
        double? durationSeconds = null;
        if (firstCompositionData is not null)
        {
            CompositionFacts.Read(firstCompositionData, out resolution, out frameRate, out durationSeconds);
        }

        return new VideoProjectFacts(
            VideoProjectIdentity.KindAfterEffects,
            VideoProjectIdentity.FormatAepx,
            VideoProjectIdentity.ApplicationAfterEffects)
        {
            ApplicationVersion = applicationVersion,
            ProjectTitle = firstCompositionName,
            TimelineCount = compositionCount,
            ItemCount = compositionCount + footageCount + folderCount,
            MediaFileCount = footageCount,
            FrameRate = frameRate,
            DurationSeconds = durationSeconds,
            Resolution = resolution
        };
    }

    private static void ScanFolder(
        XmlReader reader,
        VideoProjectPluginOptions options,
        CancellationToken cancellationToken,
        int depth,
        ref long compositionCount,
        ref long footageCount,
        ref long folderCount,
        ref string? firstCompositionName,
        ref byte[]? firstCompositionData)
    {
        if (depth > MaxNestingDepth)
        {
            throw new VideoProjectLimitExceededException("project nesting depth", MaxNestingDepth);
        }

        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (subtree.NodeType != XmlNodeType.Element || subtree.LocalName != "Item")
            {
                continue;
            }

            ReadItem(subtree, options, cancellationToken, depth, ref compositionCount, ref footageCount,
                ref folderCount, ref firstCompositionName, ref firstCompositionData);
        }
    }

    private static void ReadItem(
        XmlReader item,
        VideoProjectPluginOptions options,
        CancellationToken cancellationToken,
        int depth,
        ref long compositionCount,
        ref long footageCount,
        ref long folderCount,
        ref string? firstCompositionName,
        ref byte[]? firstCompositionData)
    {
        if (depth > MaxNestingDepth)
        {
            throw new VideoProjectLimitExceededException("project nesting depth", MaxNestingDepth);
        }

        var itemType = 0;
        string? name = null;
        byte[]? compositionData = null;

        using var subtree = item.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType != XmlNodeType.Element || subtree.Depth != 1)
            {
                continue;
            }

            switch (subtree.LocalName)
            {
                case "idta":
                    var identity = HexPrefix.Decode(subtree.GetAttribute("bdata"), 64);
                    if (identity.Length >= 2)
                    {
                        itemType = (identity[0] << 8) | identity[1];
                    }
                    else if (identity.Length == 1)
                    {
                        itemType = identity[0];
                    }

                    break;

                case "cdta":
                    compositionData ??= HexPrefix.Decode(subtree.GetAttribute("bdata"), 204);
                    break;

                case "string":
                    name ??= ProjectXml.ReadElementText(subtree, options.MaxNameCharacters);
                    break;

                case "Sfdr":
                    ScanFolder(subtree, options, cancellationToken, depth + 1, ref compositionCount,
                        ref footageCount, ref folderCount, ref firstCompositionName, ref firstCompositionData);
                    break;
            }
        }

        switch (itemType)
        {
            case 1:
                folderCount++;
                break;

            case 4:
                compositionCount++;
                if (firstCompositionData is null)
                {
                    firstCompositionName = ProjectText.Clean(name, options.MaxNameCharacters);
                    firstCompositionData = compositionData;
                }

                break;

            case 7:
                footageCount++;
                break;
        }
    }
}
