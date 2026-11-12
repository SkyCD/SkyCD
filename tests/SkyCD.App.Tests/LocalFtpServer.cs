using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SkyCD.App.Tests;

internal sealed class LocalFtpServer : IDisposable
{
    private readonly string rootFull;
    private readonly string username;
    private readonly string password;
    private readonly bool allowAnonymous;
    private readonly bool supportMlsd;
    private readonly TcpListener listener;
    private readonly Thread thread;
    private volatile bool stopping;
    private TcpClient? active;

    public LocalFtpServer(string rootPath, string username, string password, bool allowAnonymous = false, bool supportMlsd = true)
    {
        this.username = username;
        this.password = password;
        this.allowAnonymous = allowAnonymous;
        this.supportMlsd = supportMlsd;
        rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        thread = new Thread(Listen) { IsBackground = true, Name = "SkyCD FTP fixture" };
        thread.Start();
    }

    public int Port { get; }

    public void Dispose()
    {
        stopping = true;
        try
        {
            active?.Close();
        }
        catch
        {
            // The client socket may already be closed.
        }

        listener.Stop();
        thread.Join(TimeSpan.FromSeconds(3));
    }

    private void Listen()
    {
        while (!stopping)
        {
            TcpClient client;
            try
            {
                client = listener.AcceptTcpClient();
            }
            catch
            {
                break;
            }

            active = client;
            try
            {
                Handle(client);
            }
            catch
            {
                // The catalog client closes the control connection when it finishes or fails.
            }
            finally
            {
                client.Dispose();
                if (ReferenceEquals(active, client))
                {
                    active = null;
                }
            }
        }
    }

    private void Handle(TcpClient client)
    {
        client.NoDelay = true;
        using var stream = client.GetStream();
        stream.ReadTimeout = 20000;
        stream.WriteTimeout = 20000;
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
        {
            NewLine = "\r\n",
            AutoFlush = true
        };

        writer.WriteLine("220 SkyCD FTP fixture ready");
        var authenticated = false;
        string? pendingUser = null;
        TcpListener? passive = null;

        try
        {
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var space = line.IndexOf(' ');
                var verb = (space < 0 ? line : line[..space]).ToUpperInvariant();
                var argument = space < 0 ? string.Empty : line[(space + 1)..];

                if (verb == "QUIT")
                {
                    writer.WriteLine("221 Goodbye");
                    break;
                }

                if (!authenticated && verb is not ("USER" or "PASS" or "FEAT" or "SYST" or "NOOP" or "OPTS" or "TYPE"))
                {
                    writer.WriteLine("530 Please login");
                    continue;
                }

                switch (verb)
                {
                    case "USER":
                        pendingUser = argument;
                        writer.WriteLine("331 Password required");
                        break;
                    case "PASS":
                        authenticated = Accepts(pendingUser, argument);
                        writer.WriteLine(authenticated ? "230 Logged in" : "530 Login incorrect");
                        break;
                    case "SYST":
                        writer.WriteLine("215 UNIX Type: L8");
                        break;
                    case "FEAT":
                        writer.WriteLine("211-Features:");
                        writer.WriteLine(supportMlsd ? " MLSD" : " LIST");
                        writer.WriteLine(" EPSV");
                        writer.WriteLine(" PASV");
                        writer.WriteLine(" UTF8");
                        writer.WriteLine("211 End");
                        break;
                    case "OPTS":
                    case "TYPE":
                    case "NOOP":
                        writer.WriteLine("200 OK");
                        break;
                    case "EPSV":
                        passive = RestartPassive(passive);
                        writer.WriteLine($"229 Entering Extended Passive Mode (|||{((IPEndPoint)passive.LocalEndpoint).Port}|)");
                        break;
                    case "PASV":
                        passive = RestartPassive(passive);
                        var port = ((IPEndPoint)passive.LocalEndpoint).Port;
                        writer.WriteLine($"227 Entering Passive Mode (127,0,0,1,{port / 256},{port % 256})");
                        break;
                    case "MLSD" when !supportMlsd:
                        passive?.Stop();
                        passive = null;
                        writer.WriteLine("502 Command not implemented");
                        break;
                    case "MLSD":
                    case "LIST":
                        ServeListing(writer, ref passive, verb, string.IsNullOrWhiteSpace(argument) ? "/" : argument);
                        break;
                    default:
                        writer.WriteLine("502 Command not implemented");
                        break;
                }
            }
        }
        finally
        {
            passive?.Stop();
        }
    }

    private bool Accepts(string? user, string suppliedPassword)
    {
        if (allowAnonymous &&
            user is not null &&
            (user.Equals("anonymous", StringComparison.OrdinalIgnoreCase) || user.Equals("ftp", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return string.Equals(user, username, StringComparison.Ordinal) &&
               string.Equals(suppliedPassword, password, StringComparison.Ordinal);
    }

    private static TcpListener RestartPassive(TcpListener? passive)
    {
        passive?.Stop();
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }

    private void ServeListing(StreamWriter writer, ref TcpListener? passive, string verb, string ftpPath)
    {
        if (passive is null)
        {
            writer.WriteLine("425 Use EPSV or PASV first");
            return;
        }

        var dataListener = passive;
        passive = null;
        var transferred = false;
        try
        {
            var local = ToLocal(ftpPath);
            if (local.Length == 0 || !Directory.Exists(local))
            {
                writer.WriteLine("550 Directory not found");
                return;
            }

            writer.WriteLine("150 Opening data connection");
            using var dataClient = AcceptData(dataListener);
            using var dataStream = dataClient.GetStream();
            var bytes = new UTF8Encoding(false).GetBytes(BuildListing(verb, local));
            dataStream.Write(bytes);
            dataStream.Flush();
            dataClient.Client.Shutdown(SocketShutdown.Send);
            transferred = true;
        }
        catch
        {
            try
            {
                writer.WriteLine("425 Data connection failed");
            }
            catch
            {
                // The control connection is already gone.
            }
        }
        finally
        {
            dataListener.Stop();
        }

        if (transferred)
        {
            writer.WriteLine("226 Transfer complete");
        }
    }

    private static TcpClient AcceptData(TcpListener passive)
    {
        var accept = passive.AcceptTcpClientAsync();
        if (!accept.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new IOException("Timed out waiting for an FTP data connection.");
        }

        return accept.GetAwaiter().GetResult();
    }

    private string ToLocal(string ftpPath)
    {
        var relative = ftpPath.Replace('\\', '/').Trim().TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(rootFull, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!combined.Equals(rootFull, comparison) &&
            !combined.StartsWith(rootFull + Path.DirectorySeparatorChar, comparison))
        {
            return string.Empty;
        }

        return combined;
    }

    private static string BuildListing(string verb, string local)
    {
        var builder = new StringBuilder();
        foreach (var entry in Directory.EnumerateFileSystemEntries(local))
        {
            var name = Path.GetFileName(entry);
            var modified = File.GetLastWriteTimeUtc(entry);
            var isDirectory = (File.GetAttributes(entry) & FileAttributes.Directory) != 0;
            if (verb == "MLSD")
            {
                builder.Append(isDirectory ? "type=dir;" : "type=file;");
                if (!isDirectory)
                {
                    builder.Append("size=").Append(new FileInfo(entry).Length.ToString(CultureInfo.InvariantCulture)).Append(';');
                }

                builder.Append("modify=").Append(modified.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)).Append(';');
                builder.Append(isDirectory ? "unix.mode=0755;perm=el; " : "unix.mode=0644;perm=r; ");
                builder.Append(name).Append("\r\n");
                continue;
            }

            var mode = isDirectory ? "drwxr-xr-x" : "-rw-r--r--";
            var size = isDirectory ? 0 : new FileInfo(entry).Length;
            builder.Append(mode).Append(" 1 owner group ").Append(size.ToString(CultureInfo.InvariantCulture)).Append(' ');
            builder.Append(modified.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture)).Append(' ');
            builder.Append(name).Append("\r\n");
        }

        return builder.ToString();
    }
}
