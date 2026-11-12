using System;

namespace SkyCD.Formatting.Rtf;

public sealed class RichTextSpan : IEquatable<RichTextSpan>
{
    public RichTextSpan()
        : this(string.Empty)
    {
    }

    public RichTextSpan(string text, bool bold = false, bool italic = false, bool underline = false, bool strikeout = false)
    {
        Text = text ?? string.Empty;
        Bold = bold;
        Italic = italic;
        Underline = underline;
        Strikeout = strikeout;
    }

    public string Text { get; set; }

    public bool Bold { get; set; }

    public bool Italic { get; set; }

    public bool Underline { get; set; }

    public bool Strikeout { get; set; }

    public bool HasFormatting => Bold || Italic || Underline || Strikeout;

    public bool HasSameFormatting(RichTextSpan? other)
    {
        if (other is null)
        {
            return false;
        }

        return Bold == other.Bold &&
               Italic == other.Italic &&
               Underline == other.Underline &&
               Strikeout == other.Strikeout;
    }

    public RichTextSpan Clone()
    {
        return new RichTextSpan(Text, Bold, Italic, Underline, Strikeout);
    }

    public bool Equals(RichTextSpan? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return string.Equals(Text, other.Text, StringComparison.Ordinal) &&
               Bold == other.Bold &&
               Italic == other.Italic &&
               Underline == other.Underline &&
               Strikeout == other.Strikeout;
    }

    public override bool Equals(object? obj) => Equals(obj as RichTextSpan);

    public override int GetHashCode()
    {
        return HashCode.Combine(Text, Bold, Italic, Underline, Strikeout);
    }

    public override string ToString()
    {
        return Text;
    }
}
