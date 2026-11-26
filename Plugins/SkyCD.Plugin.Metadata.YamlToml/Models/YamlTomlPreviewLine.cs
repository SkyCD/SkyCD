using System.Collections.Generic;

namespace SkyCD.Plugin.Metadata.YamlToml.Models;

/// <summary>How one run of text on a preview line is drawn.</summary>
public enum YamlTomlSpanKind
{
    /// <summary>A mapping key or a table name. Drawn bold.</summary>
    Key,

    /// <summary>A scalar, punctuation or indent. Drawn regular.</summary>
    Value,

    /// <summary>A comment. Drawn italic.</summary>
    Comment
}

/// <summary>One styled run inside a preview line.</summary>
public sealed record YamlTomlPreviewSpan(YamlTomlSpanKind Kind, string Text);

/// <summary>One source-ordered line of the structure-aware page.</summary>
public sealed record YamlTomlPreviewLine(IReadOnlyList<YamlTomlPreviewSpan> Spans);
