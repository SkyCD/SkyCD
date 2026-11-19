using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using FxSsh;
using FxSsh.Services;

namespace SkyCD.App.Tests;

/// <summary>
/// A scripted SSH/SFTP server on the loopback interface, used to exercise the real SSH.NET
/// transport of <c>SftpCatalogConnector</c> against controlled server behaviour (#624): nested
/// listings with sizes and timestamps, password and public-key authentication, rejected
/// credentials, host-key verification and dropped connections.
/// </summary>
/// <remarks>
/// The SSH transport is served by FxSsh. FxSsh ships no SFTP service in the package itself, so the
/// "sftp" subsystem is answered by a minimal read-only SFTP v3 handler (draft-ietf-secsh-filexfer-02)
/// rooted at a seeded temporary directory.
/// </remarks>
internal sealed class ScriptedSftpServer : IDisposable
{
    private readonly SshServer server;
    private readonly string rootPath;
    private readonly object gate = new();
    private int userAuthAttempts;
    private bool sawPublicKeyAuth;
    private bool stopped;

    public ScriptedSftpServer(string username = "alice", string password = "secret-pass")
    {
        Username = username;
        Password = password;
        rootPath = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "skycd-sftp-" + Guid.NewGuid().ToString("N"))).FullName;
        HostKeyPem = KeyGenerator.GenerateRsaKeyPem(2048);
        ExpectedHostKeyFingerprint = ComputeHostKeyFingerprint(HostKeyPem);

        // FxSsh cannot report an OS-assigned port when started on port 0, so a free loopback port
        // is reserved first; the retry covers the small window between the two binds.
        for (var attempt = 0; ; attempt++)
        {
            var port = ReserveLoopbackPort();
            var candidate = new SshServer(new StartingInfo(IPAddress.Loopback, port, "SSH-2.0-SkyCD-ScriptedSftp"));
            candidate.AddHostKey("rsa-sha2-256", HostKeyPem);
            candidate.AddHostKey("rsa-sha2-512", HostKeyPem);
            candidate.ConnectionAccepted += OnConnectionAccepted;
            try
            {
                candidate.Start();
                server = candidate;
                Port = port;
                break;
            }
            catch (SocketException) when (attempt < 3)
            {
                try
                {
                    candidate.Dispose();
                }
                catch (InvalidOperationException)
                {
                    // A server that never started has nothing to stop.
                }
            }
        }
    }

    public int Port { get; }

    /// <summary>Root of the seeded temporary directory served by the SFTP subsystem.</summary>
    public string Root => rootPath;

    /// <summary>PEM of the RSA host key presented during key exchange.</summary>
    public string HostKeyPem { get; }

    /// <summary>OpenSSH-style fingerprint ("SHA256:" + unpadded base64) of <see cref="HostKeyPem"/>.</summary>
    public string ExpectedHostKeyFingerprint { get; }

    public string Username { get; }

    public string Password { get; }

    /// <summary>Whether authentication attempts are accepted; set false to simulate rejected credentials.</summary>
    public bool AcceptAuthentication { get; set; } = true;

    /// <summary>Whether public-key attempts from <see cref="Username"/> carrying any key are accepted.</summary>
    public bool AcceptPublicKey { get; set; }

    /// <summary>Number of user-auth attempts the server has seen.</summary>
    public int UserAuthAttempts
    {
        get
        {
            lock (gate)
            {
                return userAuthAttempts;
            }
        }
    }

    /// <summary>Whether the server has seen a public-key auth attempt.</summary>
    public bool SawPublicKeyAuth
    {
        get
        {
            lock (gate)
            {
                return sawPublicKeyAuth;
            }
        }
    }

    /// <summary>Writes a file of the given size below the root and pins its UTC timestamp.</summary>
    public void SeedFile(string remotePath, int size, DateTime modifiedUtc)
    {
        var local = ToLocalPath(remotePath);
        Directory.CreateDirectory(Path.GetDirectoryName(local)!);
        File.WriteAllBytes(local, new byte[size]);
        File.SetLastWriteTimeUtc(local, modifiedUtc);
        File.SetLastAccessTimeUtc(local, modifiedUtc);
    }

    /// <summary>Creates a directory below the root.</summary>
    public void SeedDirectory(string remotePath) => Directory.CreateDirectory(ToLocalPath(remotePath));

    /// <summary>Stops the server and drops all live sessions, simulating an interrupted connection.</summary>
    public void StopAbruptly()
    {
        lock (gate)
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
        }

        server.Stop();
    }

    public void Dispose()
    {
        StopAbruptly();
        try
        {
            server.Dispose();
        }
        catch (InvalidOperationException)
        {
            // Already stopped; disposal has nothing left to do.
        }

        try
        {
            Directory.Delete(rootPath, recursive: true);
        }
        catch (IOException)
        {
            // A stray handle must not fail the test run; the temp directory is expendable.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static int ReserveLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private void OnConnectionAccepted(object? sender, Session session) =>
        session.ServiceRegistered += OnServiceRegistered;

    private void OnServiceRegistered(object? sender, SshService service)
    {
        if (service is UserAuthService userAuth)
        {
            userAuth.UserAuth += OnUserAuth;
        }
        else if (service is ConnectionService connection)
        {
            connection.CommandOpened += OnCommandOpened;
        }
    }

    private void OnUserAuth(object? sender, UserAuthArgs e)
    {
        lock (gate)
        {
            userAuthAttempts++;
            if (e.AuthMethod == "publickey")
            {
                sawPublicKeyAuth = true;
            }
        }

        if (!AcceptAuthentication)
        {
            e.Result = false;
            return;
        }

        if (e.AuthMethod == "publickey")
        {
            e.Result = AcceptPublicKey &&
                string.Equals(e.Username, Username, StringComparison.Ordinal) &&
                e.Key is { Length: > 0 };
            return;
        }

        e.Result = e.AuthMethod == "password" &&
            string.Equals(e.Username, Username, StringComparison.Ordinal) &&
            string.Equals(e.Password, Password, StringComparison.Ordinal);
    }

    private void OnCommandOpened(object? sender, CommandRequestedArgs e)
    {
        if (e.ShellType != "subsystem" || e.CommandText != "sftp")
        {
            e.Agreed = false;
            return;
        }

        e.Agreed = true;
        var sftp = new SftpSubsystem(rootPath, bytes => e.Channel.SendData(bytes));
        e.Channel.DataReceived += (_, data) => sftp.OnData(data);
        e.Channel.CloseReceived += (_, _) =>
        {
            sftp.OnClose();
            e.Channel.SendClose(0);
        };
    }

    private string ToLocalPath(string remotePath)
    {
        var trimmed = SftpSubsystem.CanonicalizeRemote(remotePath).TrimStart('/');
        return Path.GetFullPath(Path.Combine(rootPath, trimmed));
    }

    /// <summary>
    /// Computes the fingerprint of an RSA PEM the way OpenSSH displays it: "SHA256:" followed by
    /// the unpadded base64 of the SHA-256 hash of the SSH public key blob ("ssh-rsa" + mpint e + mpint n).
    /// </summary>
    private static string ComputeHostKeyFingerprint(string pem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        var parameters = rsa.ExportParameters(includePrivateParameters: false);

        using var blob = new MemoryStream();
        WriteSshString(blob, "ssh-rsa");
        WriteSshMpint(blob, parameters.Exponent!);
        WriteSshMpint(blob, parameters.Modulus!);
        var hash = SHA256.HashData(blob.ToArray());
        return "SHA256:" + Convert.ToBase64String(hash).TrimEnd('=');
    }

    private static void WriteSshString(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        WriteUInt32(stream, (uint)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteSshMpint(Stream stream, byte[] value)
    {
        var start = 0;
        while (start < value.Length - 1 && value[start] == 0)
        {
            start++;
        }

        var significant = value.AsSpan(start);
        var pad = significant.Length > 0 && (significant[0] & 0x80) != 0;
        WriteUInt32(stream, (uint)(significant.Length + (pad ? 1 : 0)));
        if (pad)
        {
            stream.WriteByte(0);
        }

        stream.Write(significant);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>
    /// Minimal read-only SFTP v3 server for one "sftp" subsystem channel: directory listings with
    /// sizes, permissions and UTC timestamps, stat queries and canonical paths below a seeded root.
    /// Mutating requests are answered with SSH_FX_OP_UNSUPPORTED.
    /// </summary>
    private sealed class SftpSubsystem
    {
        private const byte FxpInit = 1;
        private const byte FxpVersion = 2;
        private const byte FxpClose = 4;
        private const byte FxpLstat = 7;
        private const byte FxpFstat = 8;
        private const byte FxpOpendir = 11;
        private const byte FxpReaddir = 12;
        private const byte FxpRealpath = 16;
        private const byte FxpStat = 17;
        private const byte FxpStatus = 101;
        private const byte FxpHandle = 102;
        private const byte FxpName = 104;
        private const byte FxpAttrs = 105;

        private const uint SsH_Fx_Ok = 0;
        private const uint SsH_Fx_Eof = 1;
        private const uint SsH_Fx_NoSuchFile = 2;
        private const uint SsH_Fx_OpUnsupported = 8;

        private const uint SftpAttrSize = 0x00000001;
        private const uint SftpAttrPermissions = 0x00000004;
        private const uint SftpAttrAcModTime = 0x00000008;

        private const uint DirectoryPermissions = 0x41ED; // S_IFDIR | 0755
        private const uint FilePermissions = 0x81A4; // S_IFREG | 0644

        private readonly string rootPath;
        private readonly Action<byte[]> send;
        private readonly Dictionary<string, HandleState> handles = new(StringComparer.Ordinal);
        private byte[]? pending;
        private int nextHandle;

        public SftpSubsystem(string rootPath, Action<byte[]> send)
        {
            this.rootPath = rootPath;
            this.send = send;
        }

        public void OnData(ReadOnlyMemory<byte> data)
        {
            var chunk = data.ToArray();
            pending = pending is null ? chunk : Concat(pending, chunk);

            while (pending is not null)
            {
                if (pending.Length < 4)
                {
                    return;
                }

                var length = BinaryPrimitives.ReadInt32BigEndian(pending);
                if (length <= 0 || length > 4 * 1024 * 1024)
                {
                    pending = null;
                    return;
                }

                var total = 4 + length;
                if (pending.Length < total)
                {
                    return;
                }

                var frame = pending.AsSpan(0, total).ToArray();
                pending = pending.Length == total ? null : pending[total..];
                ProcessFrame(frame);
            }
        }

        public void OnClose() => handles.Clear();

        public static string CanonicalizeRemote(string path)
        {
            var parts = (path ?? string.Empty).Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            var stack = new List<string>(parts.Length);
            foreach (var part in parts)
            {
                if (part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    if (stack.Count > 0)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }

                    continue;
                }

                stack.Add(part);
            }

            return stack.Count == 0 ? "/" : "/" + string.Join("/", stack);
        }

        private void ProcessFrame(byte[] frame)
        {
            var reader = new SshDataReader(frame.AsMemory(4));
            var type = reader.ReadByte();
            switch (type)
            {
                case FxpInit:
                    SendVersion();
                    break;
                case FxpClose:
                    HandleClose(reader);
                    break;
                case FxpLstat:
                case FxpStat:
                    HandleStat(reader);
                    break;
                case FxpFstat:
                    HandleFStat(reader);
                    break;
                case FxpOpendir:
                    HandleOpenDir(reader);
                    break;
                case FxpReaddir:
                    HandleReadDir(reader);
                    break;
                case FxpRealpath:
                    HandleRealPath(reader);
                    break;
                default:
                    var requestId = reader.DataAvailable >= 4 ? reader.ReadUInt32() : 0u;
                    SendStatus(requestId, SsH_Fx_OpUnsupported, "Operation not supported by the read-only fixture.");
                    break;
            }
        }

        private void HandleRealPath(SshDataReader reader)
        {
            var requestId = reader.ReadUInt32();
            var remotePath = reader.ReadString(Encoding.UTF8);
            Send(writer =>
            {
                writer.Write(FxpName);
                writer.Write(requestId);
                writer.Write(1u);
                writer.Write(CanonicalizeRemote(remotePath), Encoding.UTF8);
                writer.Write(string.Empty, Encoding.UTF8);
                writer.Write(0u); // the entry attributes are not consumed by the client
            });
        }

        private void HandleOpenDir(SshDataReader reader)
        {
            var requestId = reader.ReadUInt32();
            var remotePath = reader.ReadString(Encoding.UTF8);
            var local = ToLocalPath(remotePath);
            if (!Directory.Exists(local))
            {
                SendStatus(requestId, SsH_Fx_NoSuchFile, "No such directory.");
                return;
            }

            var entries = new List<KeyValuePair<string, FileSystemInfo>>();
            foreach (var info in new DirectoryInfo(local).EnumerateFileSystemInfos())
            {
                entries.Add(new KeyValuePair<string, FileSystemInfo>(info.Name, info));
            }

            var handle = Interlocked.Increment(ref nextHandle).ToString();
            handles.Add(handle, new HandleState { LocalPath = local, Entries = entries });
            Send(writer =>
            {
                writer.Write(FxpHandle);
                writer.Write(requestId);
                writer.Write(handle, Encoding.UTF8);
            });
        }

        private void HandleReadDir(SshDataReader reader)
        {
            var requestId = reader.ReadUInt32();
            var handle = reader.ReadString(Encoding.UTF8);
            if (!handles.TryGetValue(handle, out var state) || state.Entries is null || state.ListingSent)
            {
                SendStatus(requestId, SsH_Fx_Eof);
                return;
            }

            state.ListingSent = true;
            Send(writer =>
            {
                writer.Write(FxpName);
                writer.Write(requestId);
                writer.Write((uint)state.Entries.Count);
                foreach (var entry in state.Entries)
                {
                    writer.Write(entry.Key, Encoding.UTF8);
                    writer.Write(string.Empty, Encoding.UTF8);
                    WriteAttrs(writer, entry.Value);
                }
            });
        }

        private void HandleClose(SshDataReader reader)
        {
            var requestId = reader.ReadUInt32();
            var handle = reader.ReadString(Encoding.UTF8);
            handles.Remove(handle);
            SendStatus(requestId, SsH_Fx_Ok);
        }

        private void HandleStat(SshDataReader reader)
        {
            var requestId = reader.ReadUInt32();
            var remotePath = reader.ReadString(Encoding.UTF8);
            var info = ResolveFileSystemInfo(ToLocalPath(remotePath));
            if (info is null)
            {
                SendStatus(requestId, SsH_Fx_NoSuchFile, "No such file.");
                return;
            }

            SendAttrs(requestId, info);
        }

        private void HandleFStat(SshDataReader reader)
        {
            var requestId = reader.ReadUInt32();
            var handle = reader.ReadString(Encoding.UTF8);
            if (!handles.TryGetValue(handle, out var state))
            {
                SendStatus(requestId, SsH_Fx_NoSuchFile, "Unknown handle.");
                return;
            }

            var info = ResolveFileSystemInfo(state.LocalPath);
            if (info is null)
            {
                SendStatus(requestId, SsH_Fx_NoSuchFile, "No such file.");
                return;
            }

            SendAttrs(requestId, info);
        }

        private static FileSystemInfo? ResolveFileSystemInfo(string localPath)
        {
            if (Directory.Exists(localPath))
            {
                return new DirectoryInfo(localPath);
            }

            return File.Exists(localPath) ? new FileInfo(localPath) : null;
        }

        private void SendAttrs(uint requestId, FileSystemInfo info) =>
            Send(writer =>
            {
                writer.Write(FxpAttrs);
                writer.Write(requestId);
                WriteAttrs(writer, info);
            });

        private static void WriteAttrs(SshDataWriter writer, FileSystemInfo info)
        {
            var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
            var size = !isDirectory && info is FileInfo file ? file.Length : 0L;
            writer.Write(SftpAttrSize | SftpAttrPermissions | SftpAttrAcModTime);
            writer.Write((ulong)size);
            writer.Write(isDirectory ? DirectoryPermissions : FilePermissions);
            writer.Write(ToUnixSeconds(info.LastAccessTimeUtc));
            writer.Write(ToUnixSeconds(info.LastWriteTimeUtc));
        }

        private static uint ToUnixSeconds(DateTime value) =>
            (uint)new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeSeconds();

        private void SendVersion() =>
            Send(writer =>
            {
                writer.Write(FxpVersion);
                writer.Write(3u);
            });

        private void SendStatus(uint requestId, uint code, string message = "") =>
            Send(writer =>
            {
                writer.Write(FxpStatus);
                writer.Write(requestId);
                writer.Write(code);
                writer.Write(message, Encoding.UTF8);
                writer.Write(string.Empty, Encoding.UTF8);
            });

        private void Send(Action<SshDataWriter> body)
        {
            var writer = new SshDataWriter(256);
            writer.Write(0u); // packet length placeholder
            body(writer);
            var packet = writer.ToByteArray();
            BinaryPrimitives.WriteUInt32BigEndian(packet, (uint)(packet.Length - 4));
            send(packet);
        }

        private string ToLocalPath(string remotePath)
        {
            var trimmed = CanonicalizeRemote(remotePath).TrimStart('/');
            return Path.GetFullPath(Path.Combine(rootPath, trimmed));
        }

        private static byte[] Concat(byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            first.CopyTo(result, 0);
            second.CopyTo(result, first.Length);
            return result;
        }

        private sealed class HandleState
        {
            public string LocalPath { get; init; } = string.Empty;

            public List<KeyValuePair<string, FileSystemInfo>>? Entries { get; init; }

            public bool ListingSent { get; set; }
        }
    }
}
