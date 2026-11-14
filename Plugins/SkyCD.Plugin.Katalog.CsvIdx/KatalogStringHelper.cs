using System;
using System.Text;

namespace SkyCD.Plugin.Katalog.CsvIdx;

/// <summary>
/// Helper methods for unescaping Katalog text fields.
/// </summary>
public static class KatalogStringHelper
{
    /// <summary>
    /// Unescapes backslash escape sequences used by Katalog:
    /// \\ -> \, \n -> LF, \r -> CR, \t -> TAB.
    /// Performs a single left-to-right pass matching Katalog's unescapeHistoryField.
    /// </summary>
    public static string Unescape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                switch (next)
                {
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case '\\':
                        sb.Append('\\');
                        break;
                    default:
                        sb.Append('\\');
                        sb.Append(next);
                        break;
                }
            }
            else
            {
                sb.Append(value[i]);
            }
        }

        return sb.ToString();
    }
}
