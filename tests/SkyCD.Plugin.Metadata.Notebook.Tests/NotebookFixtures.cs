using System.Text;

namespace SkyCD.Plugin.Metadata.Notebook.Tests;

/// <summary>Builds notebook sources in memory so each expected value is known to the test asserting it.</summary>
internal static class NotebookFixtures
{
    /// <summary>A v4 notebook with Markdown, code and raw cells, including stored output payloads.</summary>
    public const string FullNotebook = """
        {
          "cells": [
            {
              "cell_type": "markdown",
              "metadata": {},
              "source": ["# Revenue check\n", "\n", "Quarterly numbers for the team."]
            },
            {
              "cell_type": "code",
              "execution_count": 2,
              "metadata": {},
              "outputs": [
                { "name": "stdout", "output_type": "stream", "text": ["42\n"] },
                {
                  "data": {
                    "image/png": "SENTINEL-IMAGE-PAYLOAD",
                    "text/plain": "SENTINEL-PLAIN-PAYLOAD"
                  },
                  "metadata": {},
                  "output_type": "display_data"
                }
              ],
              "source": ["print(6 * 7)"]
            },
            {
              "cell_type": "raw",
              "metadata": {},
              "source": ["raw note"]
            }
          ],
          "metadata": {
            "kernelspec": {
              "display_name": "Python 3",
              "language": "python",
              "name": "python3"
            },
            "language_info": { "name": "python", "version": "3.12.0" }
          },
          "nbformat": 4,
          "nbformat_minor": 5
        }
        """;

    /// <summary>Headings inside a fenced block are not titles; the real heading is found later in the cell.</summary>
    public const string FencedHeadingNotebook = """
        {
          "cells": [
            {
              "cell_type": "markdown",
              "metadata": {},
              "source": ["```\n", "# not a heading\n", "```\n", "## Real Title\n"]
            },
            {
              "cell_type": "markdown",
              "metadata": {},
              "source": ["Plain prose without a heading."]
            }
          ],
          "metadata": {
            "language_info": { "name": "julia", "version": "1.10.0" }
          },
          "nbformat": 4,
          "nbformat_minor": 4
        }
        """;

    /// <summary>Trailing empty cells raise the total but are not included by the reader.</summary>
    public const string NotebookWithEmptyCell = """
        {
          "cells": [
            { "cell_type": "markdown", "metadata": {}, "source": ["# Kept"] },
            { "cell_type": "code", "execution_count": null, "metadata": {}, "outputs": [], "source": [] },
            { "cell_type": "markdown", "metadata": {}, "source": ["   "] }
          ],
          "metadata": {},
          "nbformat": 4,
          "nbformat_minor": 5
        }
        """;

    /// <summary>A notebook without kernel metadata carries no language.</summary>
    public const string NotebookWithoutKernel = """
        {
          "cells": [
            { "cell_type": "markdown", "metadata": {}, "source": ["# Bare"] }
          ],
          "metadata": {},
          "nbformat": 4,
          "nbformat_minor": 5
        }
        """;

    /// <summary>A legacy nbformat 3 notebook stores cells under worksheets instead of a cells array.</summary>
    public const string LegacyNotebook = """
        {
          "metadata": { "name": "legacy" },
          "nbformat": 3,
          "nbformat_minor": 0,
          "worksheets": [
            {
              "cells": [
                {
                  "cell_type": "code",
                  "collapsed": false,
                  "input": ["print(1)"],
                  "language": "python",
                  "metadata": {},
                  "outputs": []
                }
              ],
              "metadata": {}
            }
          ]
        }
        """;

    /// <summary>Valid JSON that is not a notebook: no cells array.</summary>
    public const string NotANotebook = """
        { "metadata": {}, "nbformat": 4, "nbformat_minor": 5 }
        """;

    /// <summary>Truncated JSON, so the reader reports a line and byte position.</summary>
    public const string CorruptNotebook = """
        {"cells": [ {"cell_type": "markdown", "source": ["# Broken"]
        """;

    public static byte[] Bytes(string notebook) => new UTF8Encoding(false).GetBytes(notebook);

    public static string ManyCells(int count)
    {
        var builder = new StringBuilder();
        builder.Append("""{ "cells": [""");
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append($$"""
                { "cell_type": "markdown", "metadata": {}, "source": ["# Cell {{index}}"] }
                """);
        }

        builder.Append("""], "metadata": {}, "nbformat": 4, "nbformat_minor": 5 }""");
        return builder.ToString();
    }
}
