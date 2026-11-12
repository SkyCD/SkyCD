using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SkyCD.Formatting.Rtf;

public sealed class RichTextParagraph : IEquatable<RichTextParagraph>
{
    public RichTextParagraph()
    {
        Spans = [];
    }

    public RichTextParagraph(IEnumerable<RichTextSpan> spans, bool isBullet = false)
    {
        Spans = spans?.Select(s => s.Clone()).ToList() ?? [];
        IsBullet = isBullet;
    }

    public RichTextParagraph(string text, bool isBullet = false)
    {
        Spans = string.IsNullOrEmpty(text) ? [] : [new RichTextSpan(text)];
        IsBullet = isBullet;
    }

    public List<RichTextSpan> Spans { get; set; }

    public bool IsBullet { get; set; }

    public string PlainText
    {
        get
        {
            if (Spans.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var span in Spans)
            {
                builder.Append(span.Text);
            }

            return builder.ToString();
        }
    }

    public bool HasFormatting => IsBullet || Spans.Any(s => s.HasFormatting);

    public void Normalize()
    {
        if (Spans.Count <= 1)
        {
            return;
        }

        var normalized = new List<RichTextSpan>();
        RichTextSpan? current = null;

        foreach (var span in Spans)
        {
            if (string.IsNullOrEmpty(span.Text))
            {
                continue;
            }

            if (current is null)
            {
                current = span.Clone();
            }
            else if (current.HasSameFormatting(span))
            {
                current.Text += span.Text;
            }
            else
            {
                normalized.Add(current);
                current = span.Clone();
            }
        }

        if (current is not null)
        {
            normalized.Add(current);
        }

        Spans = normalized;
    }

    public RichTextParagraph Clone()
    {
        return new RichTextParagraph(Spans.Select(s => s.Clone()), IsBullet);
    }

    public bool Equals(RichTextParagraph? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (IsBullet != other.IsBullet)
        {
            return false;
        }

        var thisNormalized = Clone();
        thisNormalized.Normalize();
        var otherNormalized = other.Clone();
        otherNormalized.Normalize();

        if (thisNormalized.Spans.Count != otherNormalized.Spans.Count)
        {
            return false;
        }

        for (var i = 0; i < thisNormalized.Spans.Count; i++)
        {
            if (!thisNormalized.Spans[i].Equals(otherNormalized.Spans[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as RichTextParagraph);

    public override int GetHashCode()
    {
        return HashCode.Combine(IsBullet, PlainText);
    }

    public override string ToString()
    {
        var prefix = IsBullet ? "• " : string.Empty;
        return prefix + PlainText;
    }
}
