using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.FileFormat.CDCat.Tests;

public static class CdCatTestHelper
{
    public static byte[] CreateGzipXml(string xmlContent)
    {
        var xmlBytes = Encoding.UTF8.GetBytes(xmlContent);
        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, leaveOpen: true))
        {
            gzipStream.Write(xmlBytes, 0, xmlBytes.Length);
        }
        return memoryStream.ToArray();
    }

    public static string SampleV21Xml => """
        <?xml version="1.0" encoding="utf-8"?>
        <datafile version="2.1">
            <catalog>
                <media name="Audio CD" number="1" type="CD" time="2024-05-10 12:00:00">
                    <comment>Audio album backup</comment>
                    <borrow>Loaned to Bob</borrow>
                    <directory name="Music" time="2024-05-10 12:00:00">
                        <comment>Collection of rock tracks</comment>
                        <directory name="EmptyDir" time="2024-05-10 12:00:00" />
                        <file name="track01.mp3" size="4.5 Mb" time="2024-05-10 12:00:00">
                            <mp3tag album="Best Hits" artist="The Band" title="Song One" year="2024" genre="Rock" track="1" bitrate="320">Original demo</mp3tag>
                        </file>
                        <file name="track02.mp3" size="3.2 Mb" time="2024-05-10 12:00:00">
                            <comment>0:7, 192 kbps</comment>
                        </file>
                    </directory>
                </media>
                <media name="External Drive" number="2" type="HardDisc" time="2024-06-01 15:30:00">
                    <file name="movie.avi" size="700 Mb" time="2024-06-01 15:30:00">
                        <comment>Video:#XVID MPEG-4#Gesamtzeit = 1:16:09#Framerate = 23.976 f/s#Aufloesung = 640x272##Audio:#ISO/MPEG Layer-3 #Kanaele = 2 #Sample/s = 48.0kHz #Bitrate = 123 kBit</comment>
                    </file>
                    <file name="archive.zip" size="1.2 Gb" time="2024-06-01 15:30:00">
                        <content>file1.txt&#x0A;file2.doc</content>
                        <borrow>Borrowed by Alice</borrow>
                    </file>
                    <file name="photo.jpg" size="2.5 Mb" time="2024-06-01 15:30:00">
                        <comment>751 x 500, 16.7 million colors (24 bit)</comment>
                    </file>
                </media>
            </catalog>
        </datafile>
        """;

    public static string UnicodeXml => """
        <?xml version="1.0" encoding="utf-8"?>
        <datafile version="2.0">
            <catalog>
                <media name="メディア 1 (Media One)" number="1" type="flashdrive" time="2024-07-01 10:00:00">
                    <directory name="アルバム (Album)" time="2024-07-01 10:00:00">
                        <file name="音楽_曲１.mp3" size="5.1 Mb" time="2024-07-01 10:00:00">
                            <mp3tag album="夏祭り" artist="初音ミク" title="メロディー" year="2024" />
                        </file>
                    </directory>
                    <directory name="Документы" time="2024-07-01 10:00:00">
                        <file name="Отчёт_финал.pdf" size="1.8 Mb" time="2024-07-01 10:00:00">
                            <comment>Годовой отчёт</comment>
                        </file>
                    </directory>
                    <directory name="Événements été" time="2024-07-01 10:00:00">
                        <file name="Café_Paris.jpg" size="3.4 Mb" time="2024-07-01 10:00:00" />
                    </directory>
                </media>
            </catalog>
        </datafile>
        """;

    public static string Version30Xml => """
        <?xml version="1.0" encoding="utf-8"?>
        <datafile version="3.0">
            <catalog>
                <media name="Future CD" number="1" type="CD" time="2030-01-01 00:00:00">
                    <file name="file.txt" size="10 byte" time="2030-01-01 00:00:00" />
                </media>
            </catalog>
        </datafile>
        """;

    public static string Version22Xml => """
        <?xml version="1.0" encoding="utf-8"?>
        <datafile version="2.2">
            <catalog>
                <media name="Unsupported Media" number="1" type="CD" time="2025-01-01 00:00:00">
                    <file name="file.txt" size="10 byte" time="2025-01-01 00:00:00" />
                </media>
            </catalog>
        </datafile>
        """;

    public static string EmptyCatalogXml => """
        <?xml version="1.0" encoding="utf-8"?>
        <datafile version="2.1">
            <catalog />
        </datafile>
        """;

    public static string EnsureSampleHcfExists(TestWorkspace workspace) =>
        EnsureHcfExists(workspace, "cdcat-sample-v2.1.hcf", () => CreateGzipXml(SampleV21Xml));

    private static string EnsureHcfExists(TestWorkspace workspace, string fileName, Func<byte[]> createBytes)
    {
        var path = workspace.GetPath(fileName);
        return File.Exists(path) ? path : workspace.CreateFile(fileName, createBytes());
    }
}
