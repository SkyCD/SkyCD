using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OfficeIMO.Email;
using OfficeIMO.Email.Store;
using SkyCD.Plugin.Metadata.Email.Exceptions;
using SkyCD.SDK.Capabilities.Media;
using static SkyCD.Plugin.Metadata.Email.Models.EmailSchema;

namespace SkyCD.Plugin.Metadata.Email.Parsing;

internal static class EmailMetadataReader
{
    private const int MaxScannedItems = 2048;
    private const int MaxTextLength = 1024;

    private static readonly EmailDocumentReader MessageReader = new(new EmailReaderOptions(
        maxInputBytes: EmailMetadataPlugin.MaxSourceBytes, includeAttachmentContent: false,
        includeEmbeddedMessages: false, preserveRawSource: false, maxDecodedPropertyBytes: 8 * 1024 * 1024));

    private static readonly EmailStoreReaderOptions StoreOptions = new(
        maxInputBytes: EmailMetadataPlugin.MaxSourceBytes, retainAttachmentContent: false,
        maxFolderCount: 4096, maxDecodedPropertyBytesPerItem: 1024 * 1024,
        maxDecodedTableBytes: 16 * 1024 * 1024);

    public static IReadOnlyList<MediaMetadataValue> ReadMessage(Stream stream, string format, CancellationToken token)
    {
        using var result = MessageReader.ReadDocument(stream, "message." + format, token);
        var message = result.Document;
        var expected = format == "msg" ? EmailFileFormat.OutlookMsg : EmailFileFormat.Eml;
        if (result.HasErrors || message.Format != expected ||
            (expected == EmailFileFormat.Eml && message.Headers.Count == 0))
        {
            throw new EmailDecodeException(format);
        }

        var values = new List<MediaMetadataValue>
        {
            new(MessageFormat, MediaMetadataType.Text, Text: format.ToUpperInvariant()),
            new(ToCount, MediaMetadataType.Integer, Integer: message.Recipients.Count(r => r.Kind == EmailRecipientKind.To)),
            new(CcCount, MediaMetadataType.Integer, Integer: message.Recipients.Count(r => r.Kind == EmailRecipientKind.Cc)),
            new(BccCount, MediaMetadataType.Integer, Integer: message.Recipients.Count(r => r.Kind == EmailRecipientKind.Bcc)),
            new(AttachmentCount, MediaMetadataType.Integer, Integer: message.Attachments.Count),
            new(HasHtmlBody, MediaMetadataType.Boolean, Boolean: !string.IsNullOrEmpty(message.Body.Html))
        };
        AddText(values, Subject, message.Subject);
        AddText(values, From, message.From?.Address ?? message.From?.DisplayName);
        AddText(values, MessageId, message.MessageId);
        AddText(values, InReplyTo, message.MessageMetadata?.InReplyToId);
        AddText(values, ConversationTopic, message.MessageMetadata?.ConversationTopic);
        AddText(values, AttachmentNames, string.Join(", ", message.Attachments.Select(a => a.FileName ?? string.Empty)
            .Where(name => !string.IsNullOrWhiteSpace(name)).Take(16)
            .Select(name => name[..Math.Min(name.Length, 128)])));
        AddTimestamp(values, SentAt, message.Date);
        AddTimestamp(values, ReceivedAt, message.ReceivedDate);

        if ((message.Body.Text ?? message.Body.Html ?? message.Body.Rtf) is { } body)
        {
            values.Add(new MediaMetadataValue(BodyLength, MediaMetadataType.Integer, Integer: body.Length));
        }

        return values;
    }

    public static IReadOnlyList<MediaMetadataValue> ReadStore(Stream stream, CancellationToken token)
    {
        var values = new List<MediaMetadataValue> { new(StoreKind, MediaMetadataType.Text, Text: "PST") };
        EmailStoreSession session;
        try
        {
            session = EmailStoreSession.Open(stream, "mailbox.pst", StoreOptions, leaveOpen: true, cancellationToken: token);
        }
        catch (EmailStorePasswordException exception)
        {
            values.Add(new MediaMetadataValue(Protected, MediaMetadataType.Boolean, Boolean: true));
            values.Add(new MediaMetadataValue(ScanIncomplete, MediaMetadataType.Boolean, Boolean: true));
            AddText(values, StoreDiagnosticCode, exception.GetType().Name);
            return values;
        }
        catch (NotSupportedException exception) when (
            exception.Message.StartsWith("Unsupported PST/OST encryption method ", StringComparison.Ordinal))
        {
            // OfficeIMO 3.6.2 exposes this condition as a NotSupportedException without a diagnostic code.
            values.Add(new MediaMetadataValue(IsEncrypted, MediaMetadataType.Boolean, Boolean: true));
            values.Add(new MediaMetadataValue(Protected, MediaMetadataType.Boolean, Boolean: true));
            values.Add(new MediaMetadataValue(ScanIncomplete, MediaMetadataType.Boolean, Boolean: true));
            AddText(values, StoreDiagnosticCode, "UnsupportedPstEncryptionMethod");
            return values;
        }

        using (session)
        {
            if (session.Format != EmailStoreFormat.Pst)
            {
                throw new EmailDecodeException("pst");
            }

            values.Add(new MediaMetadataValue(Protected, MediaMetadataType.Boolean, Boolean: false));
            values.Add(new MediaMetadataValue(FolderCount, MediaMetadataType.Integer, Integer: session.Folders.Count));
            AddStoreSummary(session, values, token);
        }

        return values;
    }

    private static void AddStoreSummary(EmailStoreSession session, List<MediaMetadataValue> values, CancellationToken token)
    {
        var count = 0;
        var incomplete = false;
        var hasAttachments = false;
        var attachmentStateKnown = true;
        DateTimeOffset? earliest = null;
        DateTimeOffset? latest = null;
        string? diagnostic = null;
        try
        {
            foreach (var item in session.EnumerateItems(new EmailStoreEnumerationOptions(maxItems: MaxScannedItems + 1), token))
            {
                token.ThrowIfCancellationRequested();
                if (count == MaxScannedItems)
                {
                    incomplete = true;
                    diagnostic = "ItemScanLimit";
                    break;
                }

                var summary = session.ReadSummary(item, token);
                count++;
                hasAttachments |= summary.HasAttachments == true;
                attachmentStateKnown &= summary.HasAttachments.HasValue;

                if (summary.ReceivedAt is { } received)
                {
                    earliest = earliest is null || received < earliest ? received : earliest;
                    latest = latest is null || received > latest ? received : latest;
                }
            }
        }
        catch (EmailStoreLimitExceededException exception)
        {
            incomplete = true;
            diagnostic = exception.LimitName;
        }

        var warning = session.Diagnostics.FirstOrDefault(d => d.Severity != EmailStoreDiagnosticSeverity.Information);
        incomplete |= warning is not null;
        AddText(values, StoreDiagnosticCode, warning?.Code ?? diagnostic);
        values.Add(new MediaMetadataValue(MessageCount, MediaMetadataType.Integer, Integer: count));
        values.Add(new MediaMetadataValue(ScanIncomplete, MediaMetadataType.Boolean, Boolean: incomplete));
        if (hasAttachments || (!incomplete && attachmentStateKnown))
        {
            values.Add(new MediaMetadataValue(HasAttachments, MediaMetadataType.Boolean, Boolean: hasAttachments));
        }

        AddTimestamp(values, EarliestReceivedAt, earliest);
        AddTimestamp(values, LatestReceivedAt, latest);
    }

    private static void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            text = text.Trim();
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: text[..Math.Min(text.Length, MaxTextLength)]));
        }
    }

    private static void AddTimestamp(List<MediaMetadataValue> values, string key, DateTimeOffset? date)
    {
        if (date is { } timestamp)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Timestamp, Timestamp: timestamp));
        }
    }
}
