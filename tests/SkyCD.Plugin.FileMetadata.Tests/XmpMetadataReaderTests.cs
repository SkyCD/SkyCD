using System.Text;
using Xunit;

namespace SkyCD.Plugin.FileMetadata.Tests;

public sealed class XmpMetadataReaderTests
{
    [Fact]
    public void LocalizedAlt_PrefersXDefaultOverTheFirstEntry()
    {
        const string packet = """
            <?xpacket begin=""?>
            <x:xmpmeta xmlns:x="adobe:ns:meta/">
              <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#">
                <rdf:Description xmlns:dc="http://purl.org/dc/elements/1.1/">
                  <dc:description>
                    <rdf:Alt>
                      <rdf:li xml:lang="fr">Soleil couchant</rdf:li>
                      <rdf:li xml:lang="x-default">Sunset</rdf:li>
                    </rdf:Alt>
                  </dc:description>
                </rdf:Description>
              </rdf:RDF>
            </x:xmpmeta>
            <?xpacket end="w"?>
            """;

        var data = Encoding.UTF8.GetBytes(packet);

        Assert.True(XmpPacketLocator.TryLocate(data, out var located));
        Assert.True(XmpMetadataReader.TryRead(located, out var metadata));
        Assert.Equal("Sunset", metadata.Comment);
    }

    [Fact]
    public void BareRdf_WithoutXpacketWrapper_IsLocated()
    {
        const string packet = """
            <rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
                     xmlns:dc="http://purl.org/dc/elements/1.1/">
              <rdf:Description>
                <dc:subject><rdf:Bag><rdf:li>bare</rdf:li></rdf:Bag></dc:subject>
              </rdf:Description>
            </rdf:RDF>
            """;

        var data = Encoding.UTF8.GetBytes(packet);

        Assert.True(XmpPacketLocator.TryLocate(data, out var located));
        Assert.True(XmpMetadataReader.TryRead(located, out var metadata));
        Assert.Equal(["bare"], metadata.Keywords);
    }

    [Fact]
    public void InvalidXml_YieldsNoMetadata()
    {
        Assert.False(XmpMetadataReader.TryRead(Encoding.UTF8.GetBytes("<rdf:RDF><broken>"),
            out var metadata));
        Assert.Null(metadata.Comment);
        Assert.Empty(metadata.Keywords);
    }

    [Fact]
    public void MissingPacket_IsNotLocated()
    {
        Assert.False(XmpPacketLocator.TryLocate(Encoding.UTF8.GetBytes("no metadata here"), out _));
    }
}
