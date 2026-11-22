using System.Linq;
using SkyCD.Plugin.Feature.Loans.Models;
using SkyCD.Plugin.Feature.Loans.Parsing;
using Xunit;

namespace SkyCD.Plugin.Feature.Loans.Tests;

public sealed class ContactVCardTests
{
    [Fact]
    public void Parse_ReadsStructuredContactDetails()
    {
        const string text = """
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
            """;

        var contact = Assert.Single(VCardCodec.Parse(text));

        Assert.Equal("Dr. Jane Doe", contact.Name);
        Assert.Equal("Dr. Jane Doe", contact.DisplayName);
        Assert.Equal("Example Inc", contact.Company);
        Assert.Equal("CTO", contact.Position);
        Assert.Equal("+1 555 0100", contact.Phone);
        Assert.Equal("+1 555 0101", contact.Fax);
        Assert.Equal("jane@example.com", contact.Email);
        Assert.Equal("https://example.com/jane", contact.WebPage);
        Assert.Equal("xmpp:jane@example.com", contact.Messenger);
        Assert.Equal("1 Main St, Springfield, IL, 62701, USA", contact.Address);
        Assert.Equal("Met at conference\nFollow up", contact.Comments);
    }

    [Fact]
    public void WriteAndParse_RoundTripsEscapedValuesAndLongLines()
    {
        var comments = string.Join(" ", Enumerable.Repeat("Follow up about the archived collection.", 8));
        var contact = new BorrowerDocument
        {
            Id = "contact",
            Name = "Jane Doe",
            DisplayName = "Dr. Jane Doe",
            Comments = comments,
            Phone = "+1 555 0100",
            Fax = "+1 555 0101",
            Email = "jane@example.com",
            WebPage = "https://example.com",
            Messenger = "xmpp:jane@example.com",
            Address = "1 Main St; Suite 5",
            Company = "ACME; Inc",
            Position = "CTO"
        };

        var parsed = Assert.Single(VCardCodec.Parse(VCardCodec.Write([contact])));

        Assert.Equal(contact.Name, parsed.Name);
        Assert.Equal(contact.DisplayName, parsed.DisplayName);
        Assert.Equal(contact.Comments, parsed.Comments);
        Assert.Equal(contact.Phone, parsed.Phone);
        Assert.Equal(contact.Fax, parsed.Fax);
        Assert.Equal(contact.Email, parsed.Email);
        Assert.Equal(contact.WebPage, parsed.WebPage);
        Assert.Equal(contact.Messenger, parsed.Messenger);
        Assert.Equal(contact.Address, parsed.Address);
        Assert.Equal(contact.Company, parsed.Company);
        Assert.Equal(contact.Position, parsed.Position);
    }

    [Fact]
    public void Parse_UsesStructuredNameWhenFormattedNameIsMissing()
    {
        const string text = """
            BEGIN:VCARD
            VERSION:3.0
            N:Silva;Jose;;;
            END:VCARD
            """;

        var contact = Assert.Single(VCardCodec.Parse(text));

        Assert.Equal("Jose Silva", contact.Name);
        Assert.Equal("Jose Silva", contact.DisplayName);
    }

    [Fact]
    public void Parse_DecodesQuotedPrintableValues()
    {
        const string text = """
            BEGIN:VCARD
            VERSION:2.1
            FN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:Jos=C3=A9 Silva
            NOTE;ENCODING=QUOTED-PRINTABLE:Long=20note
            END:VCARD
            """;

        var contact = Assert.Single(VCardCodec.Parse(text));

        Assert.Equal("José Silva", contact.Name);
        Assert.Equal("Long note", contact.Comments);
    }

    [Fact]
    public void Parse_HandlesMultipleCardsAndGroupedProperties()
    {
        const string text = """
            BEGIN:VCARD
            FN:Alice
            item1.TEL:+1 555 0100
            END:VCARD
            BEGIN:VCARD
            FN:Bob
            END:VCARD
            """;

        var contacts = VCardCodec.Parse(text);

        Assert.Equal(2, contacts.Count);
        Assert.Equal("Alice", contacts[0].Name);
        Assert.Equal("+1 555 0100", contacts[0].Phone);
        Assert.Equal("Bob", contacts[1].Name);
    }
}
