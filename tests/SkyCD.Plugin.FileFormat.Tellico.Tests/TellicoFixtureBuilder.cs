using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace SkyCD.Plugin.FileFormat.Tellico.Tests;

/// <summary>
/// Builds synthetic Tellico fixtures. Shapes follow the published Tellico DTD
/// (v11): tellico root with syntaxVersion, typed collection(s) with a fields
/// section, entries with typed child elements, images, borrowers/loans.
/// Native .tc files are ZIPs holding tellico.xml plus an images/ folder.
/// </summary>
public static class TellicoFixtureBuilder
{
    public static readonly string FixtureDirectory =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tellico");

    public static string BooksXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE tellico PUBLIC "-//Robby Stephenson/DTD Tellico V11.0//EN" "http://periapsis.org/tellico/dtd/v11/tellico.dtd">
        <tellico syntaxVersion="11" xmlns="http://periapsis.org/tellico/">
         <collection title="My Books" type="2" entryTitle="Books">
          <fields>
           <field name="title" title="Title" category="General" type="1"/>
           <field name="author" title="Author" category="General" type="1" flags="1"/>
           <field name="pub_year" title="Publication Year" category="Publishing" type="6"/>
           <field name="read" title="Read" category="Personal" type="4"/>
           <field name="rating" title="Rating" category="Personal" type="14"/>
           <field name="cover" title="Cover" category="General" type="10"/>
           <field name="purchase_date" title="Purchase Date" category="Personal" type="12" format="4"/>
           <field name="genre" title="Genre" category="General" type="3"/>
           <field name="pages" title="Pages" category="Publishing" type="6"/>
           <field name="custom_note" title="Note" category="Personal" type="99"/>
           <field name="bibtex" title="Bibtex" category="General" type="1"><prop name="bibtex-field">title</prop></field>
          </fields>
          <images>
           <image id="cover1" format="JPEG" width="100" height="150"/>
          </images>
          <entry id="1">
           <title>Dune</title>
           <author>Frank Herbert</author>
           <pub_year>1965</pub_year>
           <read>true</read>
           <rating>5</rating>
           <cover>cover1</cover>
           <purchase_date>2020-05-17</purchase_date>
           <genre>Science Fiction</genre>
           <pages>412</pages>
           <custom_note>First edition</custom_note>
           <bibtex>Dune</bibtex>
          </entry>
          <entry id="2">
           <title>Neuromancer</title>
           <author>William Gibson</author>
           <pub_year>1984</pub_year>
           <read>false</read>
           <genre>Cyberpunk</genre>
           <unknown_field>kept verbatim</unknown_field>
          </entry>
          <borrowers>
           <borrower>Alice<loan loanDate="2024-01-05" dueDate="2024-02-05" entryRef="1" uid="u1"/></borrower>
           <borrower>Nobody<loan loanDate="2024-01-05" entryRef="999" uid="u2"/></borrower>
          </borrowers>
         </collection>
        </tellico>
        """;

    public static string MusicXml() => """
        <?xml version="1.0" encoding="UTF-8"?>
        <tellico syntaxVersion="12" xmlns="http://periapsis.org/tellico/">
         <collection title="My Music" type="4">
          <fields>
           <field name="title" title="Title" category="General" type="1"/>
           <field name="artists" title="Artists" category="General" type="1" flags="1"/>
           <field name="year" title="Year" category="General" type="6"/>
           <field name="tracks" title="Tracks" category="General" type="8"/>
          </fields>
          <entry id="10">
           <title>Blue</title>
           <artists><artist>Joni Mitchell</artist></artists>
           <year>1971</year>
           <tracks><track><column>1</column><column>A Case of You</column></track><track><column>2</column><column>California</column></track></tracks>
          </entry>
          <entry>
           <title>Untitled Album</title>
          </entry>
         </collection>
        </tellico>
        """;

    public static byte[] BooksArchive()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var document = archive.CreateEntry("tellico.xml");
            using (var writer = new StreamWriter(document.Open(), new UTF8Encoding(false)))
            {
                writer.Write(BooksXml());
            }

            var image = archive.CreateEntry("images/cover1");
            using (var imageStream = image.Open())
            {
                imageStream.Write(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, 0, 4);
            }
        }

        return output.ToArray();
    }

    public static byte[] ZipSlipArchive()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("../evil.txt");
            var document = archive.CreateEntry("tellico.xml");
            using var writer = new StreamWriter(document.Open(), new UTF8Encoding(false));
            writer.Write(BooksXml());
        }

        return output.ToArray();
    }

}
