using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Email.Models;

internal static class EmailSchema
{
    public const string MessageFormat = nameof(MessageFormat);
    public const string Subject = nameof(Subject);
    public const string From = nameof(From);
    public const string MessageId = nameof(MessageId);
    public const string InReplyTo = nameof(InReplyTo);
    public const string AttachmentNames = nameof(AttachmentNames);
    public const string ConversationTopic = nameof(ConversationTopic);
    public const string StoreKind = nameof(StoreKind);
    public const string StoreDiagnosticCode = nameof(StoreDiagnosticCode);
    public const string ToCount = nameof(ToCount);
    public const string CcCount = nameof(CcCount);
    public const string BccCount = nameof(BccCount);
    public const string AttachmentCount = nameof(AttachmentCount);
    public const string BodyLength = nameof(BodyLength);
    public const string FolderCount = nameof(FolderCount);
    public const string MessageCount = nameof(MessageCount);
    public const string HasHtmlBody = nameof(HasHtmlBody);
    public const string ScanIncomplete = nameof(ScanIncomplete);
    public const string HasAttachments = nameof(HasAttachments);
    public const string Protected = nameof(Protected);
    public const string IsEncrypted = nameof(IsEncrypted);
    public const string SentAt = nameof(SentAt);
    public const string ReceivedAt = nameof(ReceivedAt);
    public const string EarliestReceivedAt = nameof(EarliestReceivedAt);
    public const string LatestReceivedAt = nameof(LatestReceivedAt);

    public static MediaDescriptor Descriptor { get; } = new(
        [".eml", ".msg", ".pst"],
        ["message/rfc822", "application/vnd.ms-outlook", "application/vnd.ms-outlook-pst"],
        new MediaMetadataSchema("skycd.email.metadata", 1,
        [
            new(MessageFormat, MediaMetadataType.Text),
            new(Subject, MediaMetadataType.Text),
            new(From, MediaMetadataType.Text),
            new(MessageId, MediaMetadataType.Text),
            new(InReplyTo, MediaMetadataType.Text),
            new(AttachmentNames, MediaMetadataType.Text),
            new(ConversationTopic, MediaMetadataType.Text),
            new(StoreKind, MediaMetadataType.Text),
            new(StoreDiagnosticCode, MediaMetadataType.Text),
            new(ToCount, MediaMetadataType.Integer),
            new(CcCount, MediaMetadataType.Integer),
            new(BccCount, MediaMetadataType.Integer),
            new(AttachmentCount, MediaMetadataType.Integer),
            new(BodyLength, MediaMetadataType.Integer),
            new(FolderCount, MediaMetadataType.Integer),
            new(MessageCount, MediaMetadataType.Integer),
            new(HasHtmlBody, MediaMetadataType.Boolean),
            new(ScanIncomplete, MediaMetadataType.Boolean),
            new(HasAttachments, MediaMetadataType.Boolean),
            new(Protected, MediaMetadataType.Boolean),
            new(IsEncrypted, MediaMetadataType.Boolean),
            new(SentAt, MediaMetadataType.Timestamp),
            new(ReceivedAt, MediaMetadataType.Timestamp),
            new(EarliestReceivedAt, MediaMetadataType.Timestamp),
            new(LatestReceivedAt, MediaMetadataType.Timestamp),
        ]));
}
