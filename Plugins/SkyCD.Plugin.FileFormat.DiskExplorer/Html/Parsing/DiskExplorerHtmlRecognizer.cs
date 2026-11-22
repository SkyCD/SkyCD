using System;
using System.IO;
using System.Text;
using SkyCD.Plugin.FileFormat.DiskExplorer.Html.Exceptions;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Html.Parsing;

/// <summary>
/// Content-aware recognizer for Disk Explorer HTML reports. Requires report-shaped
/// markup (canonical <c>dx-*</c> hooks or a Disk Explorer title marker) within a
/// bounded peek, so unrelated HTML files sharing the extension are never claimed.
/// </summary>
public static class DiskExplorerHtmlRecognizer
{
    private const int PeekBytes = 65536;

    public static bool IsDiskExplorerHtml(Stream stream)
    {
        try
        {
            var position = stream.CanSeek ? stream.Position : 0;
            try
            {
                var buffer = new byte[PeekBytes];
                var read = 0;
                int chunk;
                while (read < PeekBytes && (chunk = stream.Read(buffer, read, PeekBytes - read)) > 0)
                {
                    read += chunk;
                }

                if (read == 0)
                {
                    return false;
                }

                var head = Encoding.ASCII.GetString(buffer, 0, read);
                return HasReportShape(head);
            }
            finally
            {
                if (stream.CanSeek)
                {
                    stream.Position = position;
                }
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool HasReportShape(string head)
    {
        var lower = head.ToLowerInvariant();
        if (!lower.Contains("<html"))
        {
            return false;
        }

        if (lower.Contains("dx-item") || lower.Contains("dx-report") || lower.Contains("data-itemtype"))
        {
            return true;
        }

        return lower.Contains("disk explorer");
    }
}
