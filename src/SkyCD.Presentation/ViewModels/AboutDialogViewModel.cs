using SkyCD.UI.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Formatting;

namespace SkyCD.Presentation.ViewModels;

public partial class AboutDialogViewModel : ObservableObject
{
    public const string SystemInfoTabKey = "system-info";
    public const string LoadedAssembliesTabKey = "loaded-assemblies";
    public const string ProjectLicenseTabKey = "project-license";
    public const string AboutStoryTabKey = "about-story";
    public const string DefaultTabKey = AboutStoryTabKey;

    private static readonly string[] TabKeys =
    [
        AboutStoryTabKey,
        SystemInfoTabKey,
        LoadedAssembliesTabKey,
        ProjectLicenseTabKey
    ];

    private const string FallbackProductName = "SkyCD";
    private const string FallbackVersion = "0.0.0";
    private const string FallbackWebsite = "";
    private readonly Process currentProcess;
    private readonly TimeProvider timeProvider;
    private TimeSpan lastTotalProcessorTime;
    private DateTimeOffset lastSampleTimeUtc;

    public AboutDialogViewModel()
        : this(
            ResolveProductName(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()),
            ResolveVersion(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()),
            ResolveWebsite(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()),
            AppDomain.CurrentDomain.GetAssemblies(),
            AppContext.BaseDirectory,
            Process.GetCurrentProcess(),
            TimeProvider.System)
    {
    }

    public static AboutDialogViewModel CreateFromMainAssembly(
        Assembly mainAssembly,
        IEnumerable<Assembly>? loadedAssemblies = null,
        string? baseDirectory = null,
        Process? process = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(mainAssembly);

        return new AboutDialogViewModel(
            ResolveProductName(mainAssembly),
            ResolveVersion(mainAssembly),
            ResolveWebsite(mainAssembly),
            loadedAssemblies,
            baseDirectory,
            process,
            timeProvider);
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

        LicensePath = ResolveLicensePath(normalizedBaseDirectory) ??
                      Path.Combine(normalizedBaseDirectory, "LICENSE.md");
        LicenseText = LoadLicenseText(normalizedBaseDirectory);

        LoadedAssemblies = new ObservableCollection<LoadedAssemblyEntry>(
            BuildLoadedAssemblyEntries(loadedAssemblies ?? AppDomain.CurrentDomain.GetAssemblies()));

        FilteredAssemblies = new ObservableCollection<LoadedAssemblyEntry>(LoadedAssemblies);

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

    public ObservableCollection<LoadedAssemblyEntry> FilteredAssemblies { get; }

    [ObservableProperty] private string assemblyFilterText = string.Empty;

    public bool ShowAssembliesEmptyState => FilteredAssemblies.Count == 0;

    [ObservableProperty] private bool dialogAccepted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTabKey))]
    private int selectedTabIndex = Array.IndexOf(TabKeys, DefaultTabKey);

    public string SelectedTabKey => TabKeys[Math.Clamp(SelectedTabIndex, 0, TabKeys.Length - 1)];

    public void RestoreSelectedTab(string? tabKey)
    {
        var index = Array.IndexOf(TabKeys, tabKey);
        SelectedTabIndex = index >= 0 ? index : Array.IndexOf(TabKeys, DefaultTabKey);
    }

    [ObservableProperty] private string cpuUsage = "0.0 %";

    [ObservableProperty] private double cpuPercent;

    [ObservableProperty] private string workingSet = "0 B";

    [ObservableProperty] private string managedHeap = "0 B";

    [ObservableProperty] private double memoryPercent;

    [ObservableProperty] private string threadInfo = LocalizationService.Current.Format(TextKeys.Threads, 0);

    [ObservableProperty] private string uptimeFriendly = "0s";

    [ObservableProperty] private string startTime = string.Empty;

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
            WorkingSet = SizeFormatting.FormatAboutDialogBytes(currentProcess.WorkingSet64);
            ManagedHeap = SizeFormatting.FormatAboutDialogBytes(GC.GetTotalMemory(false));

            var totalAvailableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            MemoryPercent = totalAvailableMemory > 0
                ? Math.Clamp((double)currentProcess.WorkingSet64 / totalAvailableMemory * 100d, 0d, 100d)
                : 0d;

            ThreadInfo = LocalizationService.Current.Format(TextKeys.Threads, currentProcess.Threads.Count);

            var uptime = DateTime.Now - currentProcess.StartTime;
            UptimeFriendly = TimeFormatting.FormatAboutDialogDuration(uptime);
            StartTime = currentProcess.StartTime.ToString("f", CultureInfo.CurrentCulture);
        }
        catch
        {
            // Ignore transient process probing issues in About diagnostics.
        }
    }

    public static string LoadLicenseText(string baseDirectory)
    {
        var resolvedPath = ResolveLicensePath(baseDirectory);
        if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
        {
            return File.ReadAllText(resolvedPath);
        }

        var expectedMarkdownPath = Path.Combine(baseDirectory, "LICENSE.md");
        var expectedPlainPath = Path.Combine(baseDirectory, "LICENSE");
        return LocalizationService.Current.Format(TextKeys.NotFoundExpectedAtOr, expectedMarkdownPath, expectedPlainPath);
    }

    private static string? ResolveLicensePath(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return null;
        }

        var current = new DirectoryInfo(baseDirectory);
        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            var markdownPath = Path.Combine(current.FullName, "LICENSE.md");
            if (File.Exists(markdownPath))
            {
                return markdownPath;
            }

            var plainPath = Path.Combine(current.FullName, "LICENSE");
            if (File.Exists(plainPath))
            {
                return plainPath;
            }

            current = current.Parent;
        }

        return null;
    }

    partial void OnAssemblyFilterTextChanged(string value) => RefreshFilteredAssemblies();

    private void RefreshFilteredAssemblies()
    {
        var matching = LoadedAssemblies
            .Where(entry => MatchesAssemblyFilter(entry, AssemblyFilterText))
            .ToArray();

        // Update the visible list in place so rows that still match keep their selection.
        var wanted = new HashSet<LoadedAssemblyEntry>(matching);
        for (var index = FilteredAssemblies.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(FilteredAssemblies[index]))
            {
                FilteredAssemblies.RemoveAt(index);
            }
        }

        var insertAt = 0;
        foreach (var entry in matching)
        {
            if (insertAt >= FilteredAssemblies.Count || !FilteredAssemblies[insertAt].Equals(entry))
            {
                FilteredAssemblies.Insert(insertAt, entry);
            }

            insertAt++;
        }

        OnPropertyChanged(nameof(ShowAssembliesEmptyState));
    }

    private static bool MatchesAssemblyFilter(LoadedAssemblyEntry entry, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        var text = filter.Trim();
        return entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || entry.Version.Contains(text, StringComparison.OrdinalIgnoreCase)
            || entry.Copyright.Contains(text, StringComparison.OrdinalIgnoreCase)
            || entry.RepositoryUrl.Contains(text, StringComparison.OrdinalIgnoreCase);
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
            assemblyName.Name ?? LocalizationService.Current.Get(TextKeys.Unknown),
            assemblyName.Version?.ToString() ?? LocalizationService.Current.Get(TextKeys.Unknown),
            copyright,
            repositoryUrl);
    }

    private static string ResolveProductName(Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
               ?? assembly.GetName().Name
               ?? FallbackProductName;
    }

    private static string ResolveVersion(Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString(3)
               ?? FallbackVersion;
    }

    private static string ResolveWebsite(Assembly assembly)
    {
        var metadataAttributes = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
        foreach (var metadata in metadataAttributes)
        {
            if (metadata.Key.Equals("RepositoryUrl", StringComparison.OrdinalIgnoreCase) ||
                metadata.Key.Equals("Repository", StringComparison.OrdinalIgnoreCase))
            {
                return metadata.Value ?? FallbackWebsite;
            }
        }

        return FallbackWebsite;
    }
}