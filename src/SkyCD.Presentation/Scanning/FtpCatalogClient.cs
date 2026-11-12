using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SkyCD.Presentation.Operations;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class FtpCatalogConnector : IFtpCatalogConnector
{
    public IFtpRemoteSession Connect(FtpLocation location, string username, string password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        var session = new FtpControlSession();
        try
        {
            session.Connect(location, username, password, cancellationToken);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private sealed class FtpControlSession : IFtpRemoteSession
    {
        private TcpClient? control;
        private StreamReader? reader;
        private StreamWriter? writer;
        private TcpClient? dataClient;
        private string host = string.Empty;
        private int port;
        private CancellationToken cancellationToken;

        public void Connect(FtpLocation location, string username, string password, CancellationToken cancellationToken)
        {
            this.cancellationToken = cancellationToken;
            host = location.Host;
            port = location.Port;
            RejectLineBreaks(username, password);

            control = new TcpClient { NoDelay = true };
            Wait(control.ConnectAsync(location.Host, location.Port, cancellationToken).AsTask(), location);
            var stream = control.GetStream();
            stream.ReadTimeout = 20000;
            stream.WriteTimeout = 20000;
            reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            writer = new StreamWriter(stream, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };

            var greeting = ReadReplyCode();
            if (greeting != 220)
            {
                throw ConnectionFailed(location);
            }

            Send("FEAT");
            ReadReplyCode();
            Send("OPTS UTF8 ON");
            ReadReplyCode();
            Send("TYPE I");
            ReadReplyCode();

            Send("USER " + username);
            var userCode = ReadReplyCode();
            if (userCode == 331)
            {
                Send("PASS " + password);
                var passCode = ReadReplyCode();
                if (passCode == 530)
                {
                    throw new FtpAuthenticationException(LocalizationService.Current.Get(TextKeys.FtpAuthenticationFailed));
                }

                if (passCode != 230 && passCode != 202)
                {
                    throw ConnectionFailed(location);
                }
            }
            else if (userCode == 530)
            {
                throw new FtpAuthenticationException(LocalizationService.Current.Get(TextKeys.FtpAuthenticationFailed));
            }
            else if (userCode != 230)
            {
                throw ConnectionFailed(location);
            }
        }

        public IReadOnlyList<FtpRemoteEntry> ListDirectory(string path)
        {
            var normalized = FtpLocation.NormalizePath(path);
            try
            {
                var listing = Transfer("MLSD " + normalized);
                return FtpListingParser.Parse(listing, normalized);
            }
            catch (FtpCommandNotSupportedException)
            {
                var listing = Transfer("LIST " + normalized);
                return FtpListingParser.Parse(listing, normalized);
            }
        }

        public void Dispose()
        {
            CloseDataChannel();
            writer?.Dispose();
            reader?.Dispose();
            control?.Dispose();
            writer = null;
            reader = null;
            control = null;
        }

        private string Transfer(string command)
        {
            OpenDataChannel();
            Send(command);
            var code = ReadReplyCode();
            if (code == 550)
            {
                CloseDataChannel();
                throw new FtpDirectoryNotFoundException(LocalizationService.Current.Format(TextKeys.RemoteDirectoryNotFound, command[(command.IndexOf(' ') + 1)..]));
            }

            if (code is 500 or 502)
            {
                CloseDataChannel();
                throw new FtpCommandNotSupportedException();
            }

            if (code is not (150 or 125))
            {
                CloseDataChannel();
                throw ConnectionFailed();
            }

            var listing = ReadDataText();
            var completed = ReadReplyCode();
            if (completed is not (226 or 250))
            {
                throw ConnectionFailed();
            }

            return listing;
        }

        private void OpenDataChannel()
        {
            CloseDataChannel();
            Send("EPSV");
            var code = ReadReplyCode(out var reply);
            if (code == 229 && TryGetEpsvPort(reply, out var epsvPort))
            {
                ConnectData(host, epsvPort);
                return;
            }

            Send("PASV");
            code = ReadReplyCode(out reply);
            if (code != 227 || !TryGetPasvEndpoint(reply, out var pasvHost, out var pasvPort))
            {
                throw ConnectionFailed();
            }

            try
            {
                ConnectData(pasvHost, pasvPort);
            }
            catch (IOException) when (!pasvHost.Equals(host, StringComparison.OrdinalIgnoreCase))
            {
                ConnectData(host, pasvPort);
            }
        }

        private void ConnectData(string dataHost, int dataPort)
        {
            dataClient?.Dispose();
            dataClient = new TcpClient { NoDelay = true };
            try
            {
                Wait(dataClient.ConnectAsync(dataHost, dataPort, cancellationToken).AsTask(), null);
            }
            catch
            {
                dataClient.Dispose();
                dataClient = null;
                throw;
            }
        }

        private string ReadDataText()
        {
            if (dataClient is null)
            {
                throw ConnectionFailed();
            }

            using var stream = dataClient.GetStream();
            stream.ReadTimeout = 20000;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            CloseDataChannel();
            return Encoding.UTF8.GetString(memory.ToArray());
        }

        private void CloseDataChannel()
        {
            dataClient?.Dispose();
            dataClient = null;
        }

        private void Send(string command)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (writer is null)
            {
                throw ConnectionFailed();
            }

            writer.WriteLine(command);
        }

        private int ReadReplyCode() => ReadReplyCode(out _);

        private int ReadReplyCode(out string text)
        {
            if (reader is null)
            {
                throw ConnectionFailed();
            }

            var lines = new System.Collections.Generic.List<string>();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? line;
                try
                {
                    line = reader.ReadLine();
                }
                catch (IOException ex)
                {
                    throw ConnectionFailed(ex);
                }

                if (line is null)
                {
                    throw ConnectionFailed();
                }

                lines.Add(line);
                if (line.Length >= 3 &&
                    int.TryParse(line.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var code) &&
                    (line.Length == 3 || line[3] != '-'))
                {
                    text = string.Join("\n", lines);
                    return code;
                }
            }
        }

        private void Wait(System.Threading.Tasks.Task task, FtpLocation? location)
        {
            try
            {
                if (!task.Wait(TimeSpan.FromSeconds(15), cancellationToken))
                {
                    throw location is null ? ConnectionFailed() : ConnectionFailed(location);
                }
            }
            catch (AggregateException ex) when (cancellationToken.IsCancellationRequested || ex.InnerException is OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw ConnectionFailed(ex.InnerException ?? ex);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AggregateException ex) when (ex.InnerException is not null)
            {
                throw ConnectionFailed(ex.InnerException);
            }
        }

        private static bool TryGetEpsvPort(string reply, out int port)
        {
            port = 0;
            var marker = reply.LastIndexOf("|||", StringComparison.Ordinal);
            if (marker < 0)
            {
                return false;
            }

            var start = marker + 3;
            var end = reply.IndexOf('|', start);
            if (end < 0)
            {
                return false;
            }

            return int.TryParse(reply[start..end], NumberStyles.None, CultureInfo.InvariantCulture, out port) && port > 0;
        }

        private static bool TryGetPasvEndpoint(string reply, out string host, out int port)
        {
            host = string.Empty;
            port = 0;
            var open = reply.LastIndexOf('(');
            var close = reply.LastIndexOf(')');
            if (open < 0 || close <= open)
            {
                return false;
            }

            var parts = reply[(open + 1)..close].Split(',');
            if (parts.Length != 6 ||
                !int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var high) ||
                !int.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out var low))
            {
                return false;
            }

            host = string.Join('.', parts[0], parts[1], parts[2], parts[3]);
            port = (high * 256) + low;
            return port > 0;
        }

        private static void RejectLineBreaks(string username, string password)
        {
            if (username.IndexOfAny(['\r', '\n']) >= 0 || password.IndexOfAny(['\r', '\n']) >= 0)
            {
                throw new FtpAuthenticationException(LocalizationService.Current.Get(TextKeys.FtpAuthenticationFailed));
            }
        }

        private FtpConnectionException ConnectionFailed(Exception inner)
        {
            var detail = SecretSanitizer.Sanitize(inner.Message);
            var target = string.IsNullOrWhiteSpace(host) ? detail : $"{host}:{port}";
            var message = LocalizationService.Current.Format(TextKeys.FtpConnectionFailed, target);
            if (!string.IsNullOrWhiteSpace(detail) &&
                detail.IndexOf(host, StringComparison.OrdinalIgnoreCase) < 0)
            {
                message = message + " " + detail;
            }

            return new FtpConnectionException(message, inner);
        }

        private FtpConnectionException ConnectionFailed() =>
            new(LocalizationService.Current.Format(TextKeys.FtpConnectionFailed, host));

        private static FtpConnectionException ConnectionFailed(FtpLocation location) =>
            new(LocalizationService.Current.Format(TextKeys.FtpConnectionFailed, $"{location.Host}:{location.Port}"));
    }

    private sealed class FtpCommandNotSupportedException : IOException;
}
