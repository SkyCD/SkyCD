using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SkyCD.Formatting.Rtf;

public sealed class RichTextDocument : IEquatable<RichTextDocument>
{
    public RichTextDocument()
    {
        Paragraphs = [];
    }

    public RichTextDocument(IEnumerable<RichTextParagraph> paragraphs)
    {
        Paragraphs = paragraphs?.Select(p => p.Clone()).ToList() ?? [];
        if (Paragraphs.Count == 0)
        {
            Paragraphs.Add(new RichTextParagraph());
        }
    }

    public List<RichTextParagraph> Paragraphs { get; set; }

    public bool HasFormatting => Paragraphs.Any(p => p.HasFormatting);

    public string ToPlainText()
    {
        if (Paragraphs.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < Paragraphs.Count; i++)
        {
            var para = Paragraphs[i];
            if (para.IsBullet)
            {
                sb.Append("• ");
            }

            sb.Append(para.PlainText);

            if (i < Paragraphs.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    public string ToRtf()
    {
        return RtfWriter.Write(this);
    }

    public static RichTextDocument FromRtf(string rtf)
    {
        return RtfReader.Read(rtf);
    }

    public static RichTextDocument FromPlainText(string? plainText)
    {
        var doc = new RichTextDocument();
        if (string.IsNullOrEmpty(plainText))
        {
            doc.Paragraphs.Add(new RichTextParagraph());
            return doc;
        }

        var normalized = plainText.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        foreach (var rawLine in lines)
        {
            var isBullet = false;
            var line = rawLine;

            if (line.StartsWith("• ", StringComparison.Ordinal) ||
                line.StartsWith("•\t", StringComparison.Ordinal))
            {
                isBullet = true;
                line = line[2..];
            }
            else if (line.StartsWith("* ", StringComparison.Ordinal) ||
                     line.StartsWith("- ", StringComparison.Ordinal))
            {
                isBullet = true;
                line = line[2..];
            }

            doc.Paragraphs.Add(new RichTextParagraph(line, isBullet));
        }

        if (doc.Paragraphs.Count == 0)
        {
            doc.Paragraphs.Add(new RichTextParagraph());
        }

        return doc;
    }

    public static RichTextDocument FromRtfOrPlainText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new RichTextDocument([new RichTextParagraph()]);
        }

        if (RtfReader.TryRead(text, out var document))
        {
            return document;
        }

        return FromPlainText(text);
    }

    public void Normalize()
    {
        foreach (var para in Paragraphs)
        {
            para.Normalize();
        }

        while (Paragraphs.Count > 1 &&
               Paragraphs[^1].Spans.Count == 0 &&
               !Paragraphs[^1].IsBullet)
        {
            Paragraphs.RemoveAt(Paragraphs.Count - 1);
        }

        if (Paragraphs.Count == 0)
        {
            Paragraphs.Add(new RichTextParagraph());
        }
    }

    public RichTextDocument Clone()
    {
        return new RichTextDocument(Paragraphs.Select(p => p.Clone()));
    }

    public bool Equals(RichTextDocument? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        var thisNormalized = Clone();
        thisNormalized.Normalize();
        var otherNormalized = other.Clone();
        otherNormalized.Normalize();

        if (thisNormalized.Paragraphs.Count != otherNormalized.Paragraphs.Count)
        {
            return false;
        }

        for (var i = 0; i < thisNormalized.Paragraphs.Count; i++)
        {
            if (!thisNormalized.Paragraphs[i].Equals(otherNormalized.Paragraphs[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as RichTextDocument);

    public override int GetHashCode()
    {
        return HashCode.Combine(Paragraphs.Count, ToPlainText());
    }

    public override string ToString() => ToPlainText();
}
