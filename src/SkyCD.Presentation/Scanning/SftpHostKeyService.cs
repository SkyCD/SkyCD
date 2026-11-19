using System;
using System.Threading;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Host-key trust decisions for SFTP connections. Keys are verified against
/// <see cref="SftpHostKeyStore"/>; an unknown or changed key is never trusted implicitly.
/// </summary>
public sealed class SftpHostKeyService
{
    private readonly SftpHostKeyStore store;
    private readonly Func<string, int, CancellationToken, SftpHostKeyInfo> probe;

    public SftpHostKeyService(SftpHostKeyStore store)
        : this(store, ProbeServer)
    {
    }

    public SftpHostKeyService(SftpHostKeyStore store, Func<string, int, CancellationToken, SftpHostKeyInfo> probe)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public static SftpHostKeyService Default { get; } = new(SftpHostKeyStore.Default);

    public SftpHostKeyState Evaluate(string host, int port, string fingerprint)
    {
        var record = store.Find(host, port);
        if (record is null)
        {
            return SftpHostKeyState.Unknown;
        }

        return string.Equals(record.Fingerprint, fingerprint, StringComparison.Ordinal)
            ? SftpHostKeyState.Trusted
            : SftpHostKeyState.Changed;
    }

    public SftpHostKeyRecord? Find(string host, int port) => store.Find(host, port);

    /// <summary>Reads the fingerprint the server currently presents. Never persists trust.</summary>
    public SftpHostKeyInfo FetchHostKey(string host, int port, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        cancellationToken.ThrowIfCancellationRequested();
        return probe(host, port, cancellationToken);
    }

    /// <summary>Records explicit user confirmation (or replacement) of a presented host key.</summary>
    public void Trust(string host, int port, SftpHostKeyInfo key) => store.Trust(host, port, key);

    private static SftpHostKeyInfo ProbeServer(string host, int port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SftpHostKeyInfo? presented = null;
        using var client = new SftpClient(new PasswordConnectionInfo(host, port, "skycd-hostkey-probe", string.Empty)
        {
            Timeout = TimeSpan.FromSeconds(SftpCatalogConnector.ConnectTimeoutSeconds),
        })
        {
            OperationTimeout = TimeSpan.FromSeconds(SftpCatalogConnector.TransferTimeoutSeconds),
        };

        client.HostKeyReceived += (_, e) =>
        {
            presented = new SftpHostKeyInfo(SftpHostKeyInfo.FormatFingerprint(e.FingerPrintSHA256), e.HostKeyName);
            e.CanTrust = true;
        };

        try
        {
            client.Connect();
            client.Disconnect();
        }
        catch (SshAuthenticationException) when (presented is not null)
        {
            // The probe only needs the key exchange; a rejected probe login is expected.
        }
        catch (SshException) when (presented is not null)
        {
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new RemoteConnectionException("sftp", $"{host}:{port}", ex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return presented ?? throw new RemoteConnectionException("sftp", $"{host}:{port}");
    }
}
