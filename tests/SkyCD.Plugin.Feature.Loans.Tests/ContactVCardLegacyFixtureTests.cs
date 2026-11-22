using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Feature.Loans.Models;
using SkyCD.Plugin.Feature.Loans.Parsing;
using Xunit;

namespace SkyCD.Plugin.Feature.Loans.Tests;

public sealed class ContactVCardLegacyFixtureTests
{
    [Fact]
    public void Parse_LegacyQuotedPrintableFixture_DecodesFoldedAndGroupedValues()
    {
        var contact = Assert.Single(VCardCodec.Parse(ReadFixture("legacy-vcard-21-quoted-printable.vcf")));

        Assert.Equal("José Silva", contact.Name);
        Assert.Equal("José Silva", contact.DisplayName);
        Assert.Equal("Example Lda", contact.Company);
        Assert.Equal("Director de Contas", contact.Position);
        Assert.Equal("+1 555 0100, +1 555 0199", contact.Phone);
        Assert.Equal("+1 555 0101", contact.Fax);
        Assert.Equal("jose@example.com, jose.alt@example.com", contact.Email);
        Assert.Equal("https://example.com/contacts/jose", contact.WebPage);
        Assert.Equal("xmpp:jose@example.com", contact.Messenger);
        Assert.Equal("Rua da Praia 12, Lisboa, 1000-001, Portugal", contact.Address);
        Assert.Contains("Follow up after the conference.", contact.Comments);
        Assert.Contains("Café", contact.Comments);
        Assert.Contains("folded with a soft line break.", contact.Comments);
    }

    [Fact]
    public void Parse_Latin1QuotedPrintableFixture_UsesDeclaredCharset()
    {
        var contact = Assert.Single(VCardCodec.Parse(ReadFixture("legacy-vcard-21-latin1.vcf")));

        Assert.Equal("Jürgen Müller", contact.Name);
        Assert.Equal("Jürgen Müller", contact.DisplayName);
        Assert.Equal("+49 30 123456", contact.Phone);
        Assert.Equal("juergen@example.com", contact.Email);
        Assert.Equal("Café at 5pm", contact.Comments);
    }

    [Fact]
    public void Parse_TruncatedCard_SkipsIncompleteCardAndKeepsValidOnes()
    {
        var contacts = VCardCodec.Parse(ReadFixture("truncated-malformed.vcf"));

        Assert.Equal(2, contacts.Count);
        Assert.Equal("Jane Doe", contacts[0].Name);
        Assert.Equal("jane@example.com", contacts[0].Email);
        Assert.Equal("Recovered Person", contacts[1].Name);
        Assert.Equal("+1 555 0111", contacts[1].Phone);
        Assert.DoesNotContain(contacts, contact => contact.Name == "Truncated Person");
    }

    [Theory]
    [InlineData("legacy-vcard-21-quoted-printable.vcf")]
    [InlineData("legacy-vcard-21-latin1.vcf")]
    [InlineData("truncated-malformed.vcf")]
    public void Fixture_ExportImportExport_PreservesSemantics(string fileName)
    {
        var imported = VCardCodec.Parse(ReadFixture(fileName));
        var exported = VCardCodec.Write(imported);
        var reimported = VCardCodec.Parse(exported);

        Assert.Equal(imported.Count, reimported.Count);
        foreach (var (expected, actual) in imported.Zip(reimported))
        {
            AssertSemanticallyEqual(expected, actual);
        }

        Assert.Equal(exported, VCardCodec.Write(reimported));
    }

    [Theory]
    [MemberData(nameof(InlineCards))]
    public void InlineCard_ExportImportExport_PreservesSemantics(string name, string text)
    {
        var imported = VCardCodec.Parse(text);
        var exported = VCardCodec.Write(imported);
        var reimported = VCardCodec.Parse(exported);

        Assert.True(imported.Count > 0, $"{name} parsed no contacts");
        Assert.Equal(imported.Count, reimported.Count);
        foreach (var (expected, actual) in imported.Zip(reimported))
        {
            AssertSemanticallyEqual(expected, actual);
        }

        Assert.Equal(exported, VCardCodec.Write(reimported));
    }

    public static TheoryData<string, string> InlineCards => new()
    {
        {
            "structured",
            """
            BEGIN:VCARD
            VERSION:3.0
            N:Doe;Jane;Q.;Dr.;
            FN:Dr. Jane Doe
            ORG:Example Inc
            TITLE:CTO
            TEL;TYPE=WORK,VOICE:+1 555 0100
            TEL;TYPE=FAX:+1 555 0101
            EMAIL:jane@example.com
            URL:https://example.com/jane
            IMPP:xmpp:jane@example.com
            ADR:;;1 Main St;Springfield;IL;62701;USA
            NOTE:Met at conference\nFollow up
            END:VCARD
            """
        },
        {
            "quoted printable utf-8",
            """
            BEGIN:VCARD
            VERSION:2.1
            FN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Jos=C3=A9 Silva
            NOTE;ENCODING=QUOTED-PRINTABLE:Long=20note
            END:VCARD
            """
        },
        {
            "grouped properties without version",
            """
            BEGIN:VCARD
            FN:Alice
            item1.TEL:+1 555 0100
            END:VCARD
            BEGIN:VCARD
            FN:Bob
            END:VCARD
            """
        }
    };

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static void AssertSemanticallyEqual(BorrowerDocument expected, BorrowerDocument actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.Company, actual.Company);
        Assert.Equal(expected.Position, actual.Position);
        Assert.Equal(expected.Phone, actual.Phone);
        Assert.Equal(expected.Fax, actual.Fax);
        Assert.Equal(expected.Email, actual.Email);
        Assert.Equal(expected.WebPage, actual.WebPage);
        Assert.Equal(expected.Messenger, actual.Messenger);
        Assert.Equal(expected.Address, actual.Address);
        Assert.Equal(NormalizeNewlines(expected.Comments), NormalizeNewlines(actual.Comments));
    }

    private static string NormalizeNewlines(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n');
}
