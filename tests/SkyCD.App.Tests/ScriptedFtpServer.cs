using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.App.Tests;

/// <summary>
/// A minimal scripted FTP server on the loopback interface, used to exercise the real FluentFTP
/// transport of <c>FtpCatalogConnector</c> against controlled server behaviour (#580): nested
/// listings, MLSD-less servers, refused listings, rejected credentials, missing directories and
/// dropped connections.
/// </summary>
/// <remarks>
/// The server answers the way a real FTP server does: it advertises MLST/MLSD in FEAT (unless
/// <see cref="SupportsMlsd"/> is false) and it always answers a <c>LIST</c> command. FluentFTP
/// falls back from MLSD to LIST on its own, so a refusal has to be served by both commands to be
/// observable by the client.
/// </remarks>
internal sealed class ScriptedFtpServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource stop = new();
    private readonly Task acceptLoop;
    private readonly List<string> commands = [];
    private readonly object gate = new();
    private int connections;

    public ScriptedFtpServer(string username = "alice", string password = "secret-pass")
    {
        Username = username;
        Password = password;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public string Username { get; }

    public string Password { get; }

    /// <summary>Directories served by the fake server; the key is the normalized remote path.</summary>
    public Dictionary<string, ScriptedDirectory> Directories { get; } = new(StringComparer.Ordinal);

    /// <summary>Whether the server advertises and accepts MLSD (a modern server).</summary>
    public bool SupportsMlsd { get; set; } = true;

    /// <summary>Whether the server answers FEAT at all.</summary>
    public bool SupportsFeat { get; set; } = true;

    /// <summary>Paths whose listing must be dropped mid-transfer, simulating an interrupted connection.</summary>
    public HashSet<string> DropListings { get; } = new(StringComparer.Ordinal);

    /// <summary>Paths the server answers with 550, simulating a directory that vanished.</summary>
    public HashSet<string> MissingDirectories { get; } = new(StringComparer.Ordinal);

    /// <summary>Number of listing commands the server has served.</summary>
    public int ListingCount
    {
        get
        {
            lock (gate)
            {
                return commands.FindAll(IsListingCommand).Count;
            }
        }
    }

    /// <summary>Number of control connections the server has accepted (a reconnect shows up here).</summary>
    public int ConnectionCount
    {
        get
        {
            lock (gate)
            {
                return connections;
            }
        }
    }

    /// <summary>Whether any command line contained the password (never true for a compliant client).</summary>
    public bool SawPasswordInCommand
    {
        get
        {
            lock (gate)
            {
                return commands.Exists(command => command.Contains(Password, StringComparison.Ordinal));
            }
        }
    }

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (gate)
            {
                return [.. commands];
            }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        try
        {
            acceptLoop.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }

        stop.Dispose();
    }

    private static bool IsListingCommand(string command) =>
        command.StartsWith("MLSD", StringComparison.OrdinalIgnoreCase) ||
        command.StartsWith("LIST", StringComparison.OrdinalIgnoreCase);

    private async Task AcceptLoopAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(stop.Token);
            }
            catch (Exception)
            {
                return;
            }

            lock (gate)
            {
                connections++;
            }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var reader = new StreamReader(stream, new UTF8Encoding(false), false, 1024, leaveOpen: true);
            var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true,
            };

            TcpListener? passive = null;

            try
            {
                await writer.WriteLineAsync("220 scripted ftp ready");

                while (!stop.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(stop.Token);
                    if (line is null)
                    {
                        return;
                    }

                    lock (gate)
                    {
                        commands.Add(line);
                    }

                    var space = line.IndexOf(' ');
                    var verb = (space < 0 ? line : line[..space]).ToUpperInvariant();
                    var argument = space < 0 ? string.Empty : line[(space + 1)..];

                    switch (verb)
                    {
                        case "USER":
                            await writer.WriteLineAsync(argument == Username ? "331 need password" : "530 unknown user");
                            break;

                        case "PASS":
                            await writer.WriteLineAsync(argument == Password ? "230 logged in" : "530 login incorrect");
                            break;

                        case "FEAT":
                            if (!SupportsFeat)
                            {
                                await writer.WriteLineAsync("500 command not understood");
                                break;
                            }

                            await writer.WriteLineAsync("211-Features:");
                            if (SupportsMlsd)
                            {
                                await writer.WriteLineAsync(" MLST type*;size*;modify*;perm*;unix.mode*;");
                            }

                            await writer.WriteLineAsync(" UTF8");
                            await writer.WriteLineAsync(" MDTM");
                            await writer.WriteLineAsync("211 End");
                            break;

                        case "OPTS":
                        case "TYPE":
                        case "MODE":
                            await writer.WriteLineAsync("200 ok");
                            break;

                        case "SYST":
                            await writer.WriteLineAsync("215 UNIX Type: L8");
                            break;

                        case "MDTM":
                            var itemPath = Normalize(argument);
                            var stamp = Directories.Values.SelectMany(directory => directory.Files)
                                .FirstOrDefault(file => itemPath.EndsWith("/" + file.Name, StringComparison.Ordinal))?.ModifiedUtc
                                ?? Directories.GetValueOrDefault(itemPath)?.ModifiedUtc;
                            await writer.WriteLineAsync(stamp is null ? "550 not found" : "213 " + stamp.Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
                            break;

                        case "PWD":
                            await writer.WriteLineAsync("257 \"/\" is the current directory");
                            break;

                        case "NOOP":
                            await writer.WriteLineAsync("200 noop");
                            break;

                        case "EPSV":
                        case "PASV":
                            {
                                passive?.Stop();
                                passive = new TcpListener(IPAddress.Loopback, 0);
                                passive.Start();
                                var dataPort = ((IPEndPoint)passive.LocalEndpoint).Port;
                                await writer.WriteLineAsync(verb == "EPSV"
                                    ? $"229 Entering Extended Passive Mode (|||{dataPort}|)"
                                    : $"227 Entering Passive Mode (127,0,0,1,{dataPort / 256},{dataPort % 256})");
                                break;
                            }

                        case "MLSD":
                        case "LIST":
                            await ServeListingAsync(verb, argument, writer, passive);
                            passive?.Stop();
                            passive = null;
                            break;

                        case "QUIT":
                            await writer.WriteLineAsync("221 bye");
                            return;

                        default:
                            await writer.WriteLineAsync("502 command not implemented");
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // The client went away (or the server is stopping): drop the connection silently.
            }
            finally
            {
                passive?.Stop();
            }
        }
    }

    private async Task ServeListingAsync(string verb, string argument, StreamWriter writer, TcpListener? passive)
    {
        var path = Normalize(argument);

        if (verb == "MLSD" && !SupportsMlsd)
        {
            await writer.WriteLineAsync("502 MLSD not implemented");
            return;
        }

        if (passive is null)
        {
            await writer.WriteLineAsync("425 use PASV first");
            return;
        }

        if (MissingDirectories.Contains(path) || !Directories.TryGetValue(path, out var directory))
        {
            // Like a real server, the refusal is answered only after the data channel was opened
            // and closed: some clients report a transfer failure as an empty listing otherwise.
            await writer.WriteLineAsync("150 opening data connection");
            await writer.FlushAsync();
            using (var refused = await passive.AcceptTcpClientAsync(stop.Token))
            {
                await refused.GetStream().FlushAsync(stop.Token);
            }

            await writer.WriteLineAsync("550 no such directory");
            return;
        }

        if (DropListings.Contains(path))
        {
            if (path != "/")
            {
                DropListings.Remove(path);
            }
            // Announce the transfer, then drop the control connection mid-listing.
            await writer.WriteLineAsync("150 opening data connection");
            await writer.FlushAsync();
            throw new IOException("Simulated interrupted connection.");
        }

        await writer.WriteLineAsync("150 opening data connection");
        await writer.FlushAsync();

        using var data = await passive.AcceptTcpClientAsync(stop.Token);
        var payload = verb == "MLSD" ? directory.ToMlsd() : directory.ToList();
        var bytes = Encoding.UTF8.GetBytes(payload);
        await data.GetStream().WriteAsync(bytes, stop.Token);
        await data.GetStream().FlushAsync(stop.Token);
        await writer.WriteLineAsync("226 transfer complete");
    }

    internal static string Normalize(string path)
    {
        var trimmed = (path ?? string.Empty).Replace('\\', '/').Trim();
        if (trimmed.Length == 0)
        {
            return "/";
        }

        trimmed = "/" + trimmed.Trim('/');
        return trimmed.Length == 1 ? "/" : trimmed.TrimEnd('/');
    }
}

/// <summary>One scripted directory: a name, its files and its child directories.</summary>
internal sealed class ScriptedDirectory(string name, DateTime modifiedUtc, string unixMode = "0755")
{
    public string Name { get; } = name;

    public DateTime ModifiedUtc { get; } = modifiedUtc;

    public string UnixMode { get; } = unixMode;

    public List<ScriptedFile> Files { get; } = [];

    public List<ScriptedDirectory> Children { get; } = [];

    /// <summary>The permission field of a LIST record, derived from <see cref="UnixMode"/>.</summary>
    public string ListMode => "d" + ScriptedFile.SymbolicMode(UnixMode);

    public string ToMlsd()
    {
        var builder = new StringBuilder();
        builder.Append("type=cdir;perm=el; .\r\n");
        builder.Append("type=pdir;perm=el; ..\r\n");
        foreach (var child in Children)
        {
            builder.Append($"type=dir;modify={child.ModifiedUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)};unix.mode={child.UnixMode};perm=el; {child.Name}\r\n");
        }

        foreach (var file in Files)
        {
            builder.Append($"type=file;size={file.Size.ToString(CultureInfo.InvariantCulture)};modify={file.ModifiedUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)};unix.mode={file.UnixMode};perm=r; {file.Name}\r\n");
        }

        return builder.ToString();
    }

    public string ToList()
    {
        var builder = new StringBuilder();
        builder.Append("total " + (Children.Count + Files.Count).ToString(CultureInfo.InvariantCulture) + "\r\n");
        foreach (var child in Children)
        {
            builder.Append($"{child.ListMode} 1 owner group 0 {child.ModifiedUtc.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture)} {child.Name}\r\n");
        }

        foreach (var file in Files)
        {
            builder.Append($"{file.ListMode} 1 owner group {file.Size.ToString(CultureInfo.InvariantCulture)} {file.ModifiedUtc.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture)} {file.Name}\r\n");
        }

        return builder.ToString();
    }
}

/// <summary>One scripted file entry.</summary>
internal sealed class ScriptedFile(string name, long size, DateTime modifiedUtc, string unixMode = "0644")
{
    public string Name { get; } = name;

    public long Size { get; } = size;

    public DateTime ModifiedUtc { get; } = modifiedUtc;

    public string UnixMode { get; } = unixMode;

    /// <summary>The permission field of a LIST record, derived from <see cref="UnixMode"/>.</summary>
    public string ListMode => "-" + SymbolicMode(UnixMode);

    internal static string SymbolicMode(string mode)
    {
        var digits = mode.TrimStart('0');
        if (digits.Length == 0)
        {
            digits = "0";
        }

        digits = digits.PadLeft(3, '0');
        digits = digits[^3..];
        var symbols = new char[9];
        for (var i = 0; i < 3; i++)
        {
            var value = digits[i] is >= '0' and <= '7' ? digits[i] - '0' : 0;
            symbols[i * 3] = (value & 4) != 0 ? 'r' : '-';
            symbols[(i * 3) + 1] = (value & 2) != 0 ? 'w' : '-';
            symbols[(i * 3) + 2] = (value & 1) != 0 ? 'x' : '-';
        }

        return new string(symbols);
    }
}
