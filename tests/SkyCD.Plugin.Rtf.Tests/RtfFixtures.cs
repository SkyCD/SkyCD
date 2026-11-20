using System;
using System.Text;

namespace SkyCD.Plugin.Rtf.Tests;

/// <summary>Builds structurally valid RTF documents in memory, so the suite commits no binary blobs.</summary>
internal static class RtfFixtures
{
    public static byte[] Document(
        string? title = null,
        string? subject = null,
        string? author = null,
        string? manager = null,
        string? company = null,
        string? operatorName = null,
        string? category = null,
        string? keywords = null,
        string? comment = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? modifiedAt = null,
        DateTimeOffset? printedAt = null,
        int? revisionNumber = null,
        int? editingTimeMinutes = null,
        int? pageCount = null,
        int? wordCount = null,
        int? characterCount = null,
        int? characterCountWithSpaces = null,
        bool protectedDocument = false,
        bool includeInfo = true,
        string body = @"Hello RTF")
    {
        var builder = new StringBuilder();
        builder.Append(@"{\rtf1\ansi\ansicpg1252\deff0");

        if (includeInfo)
        {
            builder.Append(@"{\info");
            AppendText(builder, "title", title);
            AppendText(builder, "subject", subject);
            AppendText(builder, "author", author);
            AppendText(builder, "manager", manager);
            AppendText(builder, "company", company);
            AppendText(builder, "operator", operatorName);
            AppendText(builder, "category", category);
            AppendText(builder, "keywords", keywords);
            AppendText(builder, "doccomm", comment);
            AppendTimestamp(builder, "creatim", createdAt);
            AppendTimestamp(builder, "revtim", modifiedAt);
            AppendTimestamp(builder, "printim", printedAt);
            AppendParameter(builder, "version", revisionNumber);
            AppendParameter(builder, "edmins", editingTimeMinutes);
            AppendParameter(builder, "nofpages", pageCount);
            AppendParameter(builder, "nofwords", wordCount);
            AppendParameter(builder, "nofchars", characterCount);
            AppendParameter(builder, "nofcharsws", characterCountWithSpaces);

            if (protectedDocument)
            {
                builder.Append(@"{\*\passwordhash 9F2A}");
            }

            builder.Append('}');
        }

        builder.Append(@"\pard ");
        builder.Append(body);
        builder.Append(@"\par}");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    public static byte[] LongText(int characters)
    {
        return Document(body: new string('a', characters));
    }

    public static byte[] EmptyBody() => Document(body: string.Empty);

    public static byte[] NotRtf() => Encoding.ASCII.GetBytes("Just plain text");

    public static byte[] Truncated()
    {
        var complete = Document(title: "Truncated", body: @"First paragraph\par Second paragraph");
        return complete[..(complete.Length / 2)];
    }

    private static void AppendText(StringBuilder builder, string destination, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(@"{\").Append(destination).Append(' ').Append(value).Append('}');
        }
    }

    private static void AppendTimestamp(StringBuilder builder, string destination, DateTimeOffset? value)
    {
        if (value is not { } timestamp)
        {
            return;
        }

        builder.Append(@"{\").Append(destination)
            .Append(@"\yr").Append(timestamp.Year)
            .Append(@"\mo").Append(timestamp.Month)
            .Append(@"\dy").Append(timestamp.Day)
            .Append(@"\hr").Append(timestamp.Hour)
            .Append(@"\min").Append(timestamp.Minute)
            .Append(@"\sec").Append(timestamp.Second)
            .Append('}');
    }

    private static void AppendParameter(StringBuilder builder, string word, int? value)
    {
        if (value.HasValue)
        {
            builder.Append('\\').Append(word).Append(value.Value);
        }
    }
}
