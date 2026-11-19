using SkyCD.Presentation.Operations;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteScanOptions
{
    public RemoteScanOptions(
        string address,
        string? username,
        string? password,
        bool anonymous,
        string? mediaName,
        string? targetParentId,
        bool includeExtendedInfo,
        ScanTraversalOptions? traversal = null,
        RemoteAuthMethod authMethod = RemoteAuthMethod.Password,
        string? keyFilePath = null,
        string? keyFilePassphrase = null)
    {
        Address = address;
        Username = username;
        Password = password;
        Anonymous = anonymous;
        MediaName = mediaName;
        TargetParentId = targetParentId;
        IncludeExtendedInfo = includeExtendedInfo;
        Traversal = traversal;
        AuthMethod = authMethod;
        KeyFilePath = keyFilePath;
        KeyFilePassphrase = keyFilePassphrase;
    }

    public string Address { get; }

    public string? Username { get; }

    public string? Password { get; }

    public bool Anonymous { get; }

    public string? MediaName { get; }

    public string? TargetParentId { get; }

    public bool IncludeExtendedInfo { get; }

    /// <summary>
    /// Bounded traversal policy (retries, timeout, exclusion patterns) used by the resilient
    /// remote traversal. Null falls back to <see cref="ScanTraversalOptions.Default"/>.
    /// </summary>
    public ScanTraversalOptions? Traversal { get; }

    /// <summary>How the remote server is authenticated.</summary>
    public RemoteAuthMethod AuthMethod { get; }

    /// <summary>Path of the private key file; used only when <see cref="AuthMethod"/> is <see cref="RemoteAuthMethod.PrivateKey"/>.</summary>
    public string? KeyFilePath { get; }

    /// <summary>Optional passphrase of the private key file; never included in any string representation.</summary>
    public string? KeyFilePassphrase { get; }

    public override string ToString()
    {
        var protocol = "Remote";
        string location;
        try
        {
            var parsed = RemoteLocation.Parse(Address);
            protocol = parsed.Scheme.ToUpperInvariant();
            location = parsed.PublicUrl;
        }
        catch (System.Exception)
        {
            location = SecretSanitizer.Sanitize(Address);
        }

        return $"{protocol} {location} anonymous={Anonymous} extended={IncludeExtendedInfo}";
    }
}
