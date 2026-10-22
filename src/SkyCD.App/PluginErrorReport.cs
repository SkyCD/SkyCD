using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions;

namespace SkyCD.App;

/// <summary>The entire diagnostics package is this allowlisted JSON document.</summary>
public sealed record PluginErrorReport(int SchemaVersion, Guid ReportId, Guid InstallationId,
    PluginFailure Failure, string HostVersion, bool FollowFixes)
{
    public static PluginErrorReport Create(PluginFailure failure, string directory)
    {
        Directory.CreateDirectory(directory);
        var identityPath = Path.Combine(directory, "installation-id");
        Guid installationId;
        try
        {
            using var stream = new FileStream(identityPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            installationId = Guid.NewGuid();
            using var writer = new StreamWriter(stream);
            writer.Write(installationId.ToString("D"));
        }
        catch (IOException) when (File.Exists(identityPath))
        {
            installationId = Guid.Parse(File.ReadAllText(identityPath));
        }

        return new PluginErrorReport(1, Guid.NewGuid(), installationId, failure,
            typeof(PluginErrorReport).Assembly.GetName().Version?.ToString() ?? "0.0", false);
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    [JsonIgnore]
    public string ReviewUrl => "https://github.com/SkyCD/SkyCD/issues/new?title=" +
        Uri.EscapeDataString($"Plugin failure {Failure.Fingerprint}") + "&body=" +
        Uri.EscapeDataString("Reviewed diagnostics (please avoid adding private information):\n```json\n" + ToJson() + "\n```");

    [JsonIgnore]
    public string FixesUrl => "https://github.com/SkyCD/SkyCD/issues?q=" +
        Uri.EscapeDataString(Failure.Fingerprint);

    public async Task WritePackageAsync(Stream destination)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, true);
        var entry = archive.CreateEntry("report.json");
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(ToJson());
    }
}
