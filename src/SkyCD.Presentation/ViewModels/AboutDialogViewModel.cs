using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;

namespace SkyCD.Presentation.ViewModels;

public partial class AboutDialogViewModel : ObservableObject
{
    private readonly Process currentProcess;
    private readonly TimeProvider timeProvider;
    private TimeSpan lastTotalProcessorTime;
    private DateTimeOffset lastSampleTimeUtc;

    public AboutDialogViewModel()
        : this(
            "SkyCD",
            "3.0.0",
            "https://github.com/SkyCD/SkyCD",
            AppDomain.CurrentDomain.GetAssemblies(),
            AppContext.BaseDirectory,
            Process.GetCurrentProcess(),
            TimeProvider.System)
    {
    }

    public AboutDialogViewModel(
        string productName,
        string version,
        string website,
        IEnumerable<Assembly>? loadedAssemblies = null,
        string? baseDirectory = null,
        Process? process = null,
        TimeProvider? timeProvider = null)
    {
        ProductName = productName;
        Version = version;
        Website = website;

        var normalizedBaseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? AppContext.BaseDirectory
            : baseDirectory;

        LicensePath = Path.Combine(normalizedBaseDirectory, "LICENSE.md");
        LicenseText = LoadLicenseText(normalizedBaseDirectory);

        LoadedAssemblies = new ObservableCollection<LoadedAssemblyEntry>(
            BuildLoadedAssemblyEntries(loadedAssemblies ?? AppDomain.CurrentDomain.GetAssemblies()));

        currentProcess = process ?? Process.GetCurrentProcess();
        this.timeProvider = timeProvider ?? TimeProvider.System;
        lastTotalProcessorTime = currentProcess.TotalProcessorTime;
        lastSampleTimeUtc = this.timeProvider.GetUtcNow();
        RefreshSystemInfo();
    }

    public string ProductName { get; }

    public string Version { get; }

    public string Website { get; }

    public string LicensePath { get; }

    public string LicenseText { get; }

    public ObservableCollection<LoadedAssemblyEntry> LoadedAssemblies { get; }

    [ObservableProperty]
    private bool dialogAccepted;

    [ObservableProperty]
    private string cpuUsage = "0.0 %";

    [ObservableProperty]
    private double cpuPercent;

    [ObservableProperty]
    private string workingSet = "0 B";

    [ObservableProperty]
    private string managedHeap = "0 B";

    [ObservableProperty]
    private double memoryPercent;

    [ObservableProperty]
    private string threadInfo = "0 threads";

    [ObservableProperty]
    private string uptimeFriendly = "0s";

    [ObservableProperty]
    private string startTime = string.Empty;

    [RelayCommand]
    private void Confirm()
    {
        DialogAccepted = true;
    }

    public void RefreshSystemInfo()
    {
        try
        {
            currentProcess.Refresh();

            var now = timeProvider.GetUtcNow();
            var elapsedMs = (now - lastSampleTimeUtc).TotalMilliseconds;
            var currentTotalProcessorTime = currentProcess.TotalProcessorTime;

            var cpu = 0d;
            if (elapsedMs > 1d)
            {
                var usedProcessorMs = (currentTotalProcessorTime - lastTotalProcessorTime).TotalMilliseconds;
                cpu = (usedProcessorMs / elapsedMs) / Environment.ProcessorCount * 100d;
            }

            lastSampleTimeUtc = now;
            lastTotalProcessorTime = currentTotalProcessorTime;

            CpuPercent = Math.Clamp(cpu, 0d, 100d);
            CpuUsage = $"{CpuPercent:0.0} %";
            WorkingSet = AboutDialogFormatting.FormatBytes(currentProcess.WorkingSet64);
            ManagedHeap = AboutDialogFormatting.FormatBytes(GC.GetTotalMemory(false));

            var totalAvailableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            MemoryPercent = totalAvailableMemory > 0
                ? Math.Clamp((double)currentProcess.WorkingSet64 / totalAvailableMemory * 100d, 0d, 100d)
                : 0d;

            ThreadInfo = $"{currentProcess.Threads.Count} threads";

            var uptime = DateTime.Now - currentProcess.StartTime;
            UptimeFriendly = AboutDialogFormatting.FormatFriendlyTime(uptime);
            StartTime = AboutDialogFormatting.FormatStartTime(currentProcess.StartTime);
        }
        catch
        {
            // Ignore transient process probing issues in About diagnostics.
        }
    }

    public static string LoadLicenseText(string baseDirectory)
    {
        var licensePath = Path.Combine(baseDirectory, "LICENSE.md");
        return File.Exists(licensePath)
            ? File.ReadAllText(licensePath)
            : $"Not found. Expected at: {licensePath}";
    }

    private static IReadOnlyList<LoadedAssemblyEntry> BuildLoadedAssemblyEntries(IEnumerable<Assembly> assemblies)
    {
        return assemblies
            .Distinct()
            .Select(CreateAssemblyEntry)
            .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LoadedAssemblyEntry CreateAssemblyEntry(Assembly assembly)
    {
        var assemblyName = assembly.GetName();
        var metadataAttributes = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        var repositoryUrl = metadataAttributes
            .FirstOrDefault(static attribute =>
                string.Equals(attribute.Key, "RepositoryUrl", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(attribute.Key, "Repository", StringComparison.OrdinalIgnoreCase))
            ?.Value ?? string.Empty;

        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

        return new LoadedAssemblyEntry(
            assemblyName.Name ?? "Unknown",
            assemblyName.Version?.ToString() ?? "Unknown",
            copyright,
            repositoryUrl);
    }
}
