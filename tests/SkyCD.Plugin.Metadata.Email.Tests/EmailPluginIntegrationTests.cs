using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using OfficeIMO.Email;
using OfficeIMO.Email.Store;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Metadata.Email.Tests;

public sealed class EmailPluginIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostCapturesEmlMetadata(bool html)
    {
        using var remote = new TcpListener(IPAddress.Loopback, 0);
        remote.Start();
        var port = ((IPEndPoint)remote.LocalEndpoint).Port;
        var body = html ? $"<img src=\"http://127.0.0.1:{port}/remote.png\"><p>Hello world</p>" : "Hello";
        var bytes = Encoding.ASCII.GetBytes(
            "From: Example <sender@example.test>\r\nTo: to@example.test\r\n" +
            "Cc: copy@example.test\r\nSubject: Example\r\nMessage-ID: <sample@example.test>\r\n" +
            "Date: Tue, 10 Mar 2026 12:00:00 +0000\r\nIn-Reply-To: <parent@example.test>\r\n" +
            $"Content-Type: text/{(html ? "html" : "plain")}; charset=utf-8\r\n\r\n{body}");
        var values = await CaptureAsync(".eml", bytes);
        Assert.Equal("EML", values("MessageFormat")?.Text);
        Assert.Equal("Example", values("Subject")?.Text);
        Assert.Equal("sender@example.test", values("From")?.Text);
        Assert.Equal(1, values("ToCount")?.Integer);
        Assert.Equal(1, values("CcCount")?.Integer);
        Assert.Equal(0, values("BccCount")?.Integer);
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero), values("SentAt")?.Timestamp);
        Assert.Contains("parent@example.test", values("InReplyTo")?.Text);
        Assert.Equal(html, values("HasHtmlBody")?.Boolean);
        Assert.Equal(body.Length, values("BodyLength")?.Integer);
        Assert.Null(values("AttachmentNames"));
        Assert.Null(values("ReceivedAt"));
        Assert.Null(values("StoreKind"));
        Assert.False(remote.Pending());
    }

    [Fact]
    public async Task HostCapturesMsgAttachmentNamesWithoutReadingReferencedFiles()
    {
        var document = CreateMessage("Outlook message");
        document.Attachments.Add(new EmailAttachment { FileName = "invoice.txt", Content = "data"u8.ToArray() });
        document.Attachments.Add(new EmailAttachment { FileName = "linked.txt", LinkedPath = @"Z:\unavailable\private.txt" });
        var values = await CaptureAsync(".msg", document.ToBytes(EmailFileFormat.OutlookMsg));

        Assert.Equal("MSG", values("MessageFormat")?.Text);
        Assert.Equal("Outlook message", values("Subject")?.Text);
        Assert.Equal(2, values("AttachmentCount")?.Integer);
        Assert.Equal("invoice.txt, linked.txt", values("AttachmentNames")?.Text);
    }

    [Fact]
    public async Task HostCapturesPstMailboxFacts()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("mailbox.pst");

        using (var writer = EmailStorePstWriter.Create(path))
        {
            var inbox = writer.AddFolder("Inbox");
            var archive = writer.AddFolder("Archive");
            var first = CreateMessage("First");
            first.ReceivedDate = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero);
            first.Attachments.Add(new EmailAttachment { FileName = "data.txt", Content = "data"u8.ToArray() });
            var last = CreateMessage("Last");
            last.ReceivedDate = first.ReceivedDate.Value.AddDays(7);
            writer.AddItem(inbox, first);
            writer.AddItem(archive, last);
            writer.Complete();
        }

        var values = await CaptureAsync(".pst", await File.ReadAllBytesAsync(path));
        Assert.Equal("PST", values("StoreKind")?.Text);
        Assert.True(values("FolderCount")?.Integer >= 2);
        Assert.Equal(2, values("MessageCount")?.Integer);
        Assert.False(values("ScanIncomplete")?.Boolean);
        Assert.True(values("HasAttachments")?.Boolean);
        Assert.Equal(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero), values("EarliestReceivedAt")?.Timestamp);
        Assert.Equal(new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero), values("LatestReceivedAt")?.Timestamp);
        Assert.Null(values("Subject"));
    }

    [Theory]
    [InlineData(".eml")]
    [InlineData(".msg")]
    [InlineData(".pst")]
    public async Task CorruptFilesFailInsideTheHost(string extension)
    {
        var bytes = "This is not an email file."u8.ToArray();
        var result = Assert.Single(await CreateManager().ProcessAsync(new MediaItem(extension, null, bytes.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(bytes))));
        Assert.True(result.Failed);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task ProtectedPstReportsTheLibraryConditionWithoutPublishingMailboxFacts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "protected.pst");
        var values = await CaptureAsync(".pst", await File.ReadAllBytesAsync(path));

        Assert.Equal("PST", values("StoreKind")?.Text);
        Assert.True(values("Protected")?.Boolean);
        Assert.True(values("ScanIncomplete")?.Boolean);
        Assert.Equal(nameof(EmailStorePasswordException), values("StoreDiagnosticCode")?.Text);
        Assert.Null(values("IsEncrypted"));
        Assert.Null(values("FolderCount"));
        Assert.Null(values("MessageCount"));
    }

    [Fact]
    public async Task UnsupportedPstEncryptionReportsProtectedState()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "protected.pst");
        var bytes = await File.ReadAllBytesAsync(path);
        // The Unicode PST header stores bCryptMethod at offset 513.
        bytes[513] = 3;
        var values = await CaptureAsync(".pst", bytes);

        Assert.True(values("IsEncrypted")?.Boolean);
        Assert.True(values("Protected")?.Boolean);
        Assert.True(values("ScanIncomplete")?.Boolean);
        Assert.Equal("UnsupportedPstEncryptionMethod", values("StoreDiagnosticCode")?.Text);
        Assert.Null(values("MessageCount"));
    }

    [Fact]
    public async Task OversizePstFailsWithinTheHostBudget()
    {
        var result = Assert.Single(await CreateManager(new MediaExecutionLimits(SourceBytes: 1024))
            .ProcessAsync(new MediaItem(".pst", null, 2048),
                _ => Task.FromResult<Stream>(new MemoryStream(new byte[2048]))));
        Assert.True(result.Failed);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task PstWithMoreThanTheScanLimitReportsPartialCounts()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("large.pst");

        using (var writer = EmailStorePstWriter.Create(path))
        {
            var inbox = writer.AddFolder("Inbox");
            var document = CreateMessage("Synthetic message");
            for (var index = 0; index < 2049; index++)
            {
                writer.AddItem(inbox, document);
            }

            writer.Complete();
        }

        var values = await CaptureAsync(".pst", await File.ReadAllBytesAsync(path));
        Assert.Equal(2048, values("MessageCount")?.Integer);
        Assert.True(values("ScanIncomplete")?.Boolean);
        Assert.Equal("ItemScanLimit", values("StoreDiagnosticCode")?.Text);
        Assert.Null(values("HasAttachments"));
    }

    private static EmailDocument CreateMessage(string subject) => new()
    {
        Subject = subject
    };

    private static async Task<Func<string, MediaMetadataValue?>> CaptureAsync(string extension, byte[] bytes)
    {
        var result = Assert.Single(await CreateManager().ProcessAsync(new MediaItem(extension, null, bytes.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(bytes))));
        Assert.False(result.Failed);
        Assert.Equal("skycd.email.metadata", result.Schema?.Id);
        return key => result.Metadata!.Values.SingleOrDefault(value => value.Key == key);
    }

    private static MediaExtensionManager CreateManager(MediaExecutionLimits? limits = null)
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(EmailMetadataPlugin).Assembly);
        Assert.Equal("SkyCD.Plugin.Email", plugin.Id);
        var capability = Assert.IsAssignableFrom<IMediaMetadataCapability>(Assert.Single(plugin.Capabilities));
        return new MediaExtensionManager([new MediaProvider(plugin.Id, "1.0.0", capability)], limits);
    }
}
