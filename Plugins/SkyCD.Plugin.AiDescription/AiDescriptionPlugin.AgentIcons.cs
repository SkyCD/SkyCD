using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using SkyCD.Plugin.AiDescription.Models;
using SkyCD.Plugin.AiDescription.Sources;

namespace SkyCD.Plugin.AiDescription;

public sealed partial class AiDescriptionPlugin
{
    private const int MaxIconBytes = 32 * 1024;

    private static async Task RefreshAgentIconsAsync(AcpAgentRegistry registry, string directory, CancellationToken cancellationToken)
    {
        var icons = Path.Combine(directory, "acp-icons");
        Directory.CreateDirectory(icons);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await Parallel.ForEachAsync(registry.Agents, new ParallelOptions
            {
                MaxDegreeOfParallelism = 8,
                CancellationToken = deadline.Token
            }, async (agent, token) =>
            {
                if (!Uri.TryCreate(agent.IconUrl, UriKind.Absolute, out var uri) ||
                    uri.Scheme != Uri.UriSchemeHttps || uri.Host != "cdn.agentclientprotocol.com" ||
                    !uri.AbsolutePath.StartsWith("/registry/v1/", StringComparison.Ordinal)) return;
                var path = ReceiptPath(icons, agent.Id) + ".svg";
                if (File.Exists(path)) return;
                try
                {
                    using var response = await RegistryClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                    if (!response.IsSuccessStatusCode) return;
                    await using var source = await response.Content.ReadAsStreamAsync(token);
                    var buffer = new byte[MaxIconBytes + 1];
                    var count = await source.ReadAtLeastAsync(buffer, buffer.Length, false, token);
                    if (count <= MaxIconBytes)
                        await File.WriteAllBytesAsync(path, buffer.AsMemory(0, count), token);
                }
                catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException)
                {
                    // The agent card still renders its initials when its icon is unavailable.
                }
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Slow icons must not delay the registry beyond its refresh deadline.
        }
    }

    private static IReadOnlyList<string> ReadAgentIcon(AcpRegistryAgent agent, string directory)
    {
        try
        {
            var file = new FileInfo(ReceiptPath(Path.Combine(directory, "acp-icons"), agent.Id) + ".svg");
            if (!file.Exists || file.Length > MaxIconBytes) return [];
            using var reader = XmlReader.Create(file.FullName, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                MaxCharactersInDocument = MaxIconBytes,
                XmlResolver = null
            });
            var icon = XElement.Load(reader);
            if (icon.Name.LocalName != "svg") return [];
            return icon.Elements().Where(element => element.Name.LocalName == "path" && element.Attribute("transform") is null)
                .Select(element => element.Attribute("d")?.Value).OfType<string>().Take(32).ToArray();
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
