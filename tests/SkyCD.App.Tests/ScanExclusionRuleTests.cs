using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class ScanExclusionRuleTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"skycd-exclusions-{Guid.NewGuid():N}");
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-cblite-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public ScanExclusionRuleTests()
    {
        Directory.CreateDirectory(appDataRoot);
        Environment.SetEnvironmentVariable("APPDATA", appDataRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", appDataRoot);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", Path.Combine(appDataRoot, "SkyCD"));
    }

    private CatalogScanResult ScanWith(Action<FakeScanFileSystem> populate, ScanTraversalOptions traversal)
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        populate(fs);
        return new FolderCatalogScanner(fs).ScanDetailed(new FolderScanOptions(root, Traversal: traversal));
    }

    [Fact]
    public void DefaultExclusionRules_ArePlatformAppropriateAndEntryTyped()
    {
        var defaults = WellKnownScanExclusions.GetDefaultExclusionRules();

        Assert.NotEmpty(defaults);
        Assert.All(defaults, rule => Assert.False(rule.IsInclude));
        Assert.All(defaults, rule => Assert.True(rule.EntryType is "File" or "Folder",
            $"Default rule '{rule.Pattern}' must be typed as File or Folder, not '{rule.EntryType}'."));

        if (OperatingSystem.IsWindows())
        {
            AssertWindowsDefaultRules(defaults);
        }
        else if (OperatingSystem.IsLinux())
        {
            AssertLinuxDefaultRules(defaults);
        }
        else if (OperatingSystem.IsMacOS())
        {
            AssertMacOsDefaultRules(defaults);
        }
    }

    private static void AssertWindowsDefaultRules(IReadOnlyList<ScanExclusionRuleDocument> defaults)
    {
        Assert.Contains(defaults, rule => rule.Pattern == "$RECYCLE.BIN" && rule.EntryType == "Folder");
        Assert.Contains(defaults, rule => rule.Pattern == "System Volume Information" && rule.EntryType == "Folder");
        Assert.Contains(defaults, rule => rule.Pattern == "Thumbs.db" && rule.EntryType == "File");
        Assert.Contains(defaults, rule => rule.Pattern == "desktop.ini" && rule.EntryType == "File");
        Assert.Contains(defaults, rule => rule.Pattern == "pagefile.sys" && rule.EntryType == "File");
        Assert.DoesNotContain(defaults, rule => rule.Pattern == "lost+found");
        Assert.DoesNotContain(defaults, rule => rule.Pattern == ".Trash");
        Assert.DoesNotContain(defaults, rule => rule.Pattern == ".DS_Store");
    }

    private static void AssertLinuxDefaultRules(IReadOnlyList<ScanExclusionRuleDocument> defaults)
    {
        Assert.Contains(defaults, rule => rule.Pattern == ".Trash" && rule.EntryType == "Folder");
        Assert.Contains(defaults, rule => rule.Pattern == ".Trash-1000" && rule.EntryType == "Folder");
        Assert.Contains(defaults, rule => rule.Pattern == "lost+found" && rule.EntryType == "Folder");
        Assert.DoesNotContain(defaults, rule => rule.Pattern == "Thumbs.db");
        Assert.DoesNotContain(defaults, rule => rule.Pattern == "System Volume Information");
        Assert.DoesNotContain(defaults, rule => rule.Pattern == ".Trashes");
    }

    private static void AssertMacOsDefaultRules(IReadOnlyList<ScanExclusionRuleDocument> defaults)
    {
        Assert.Contains(defaults, rule => rule.Pattern == ".Trashes" && rule.EntryType == "Folder");
        Assert.Contains(defaults, rule => rule.Pattern == ".Spotlight-V100" && rule.EntryType == "Folder");
        Assert.Contains(defaults, rule => rule.Pattern == ".fseventsd" && rule.EntryType == "Folder");
        Assert.Contains(defaults, rule => rule.Pattern == ".TemporaryItems" && rule.EntryType == "Folder");
        Assert.Contains(defaults, rule => rule.Pattern == ".DS_Store" && rule.EntryType == "File");
        Assert.DoesNotContain(defaults, rule => rule.Pattern == "Thumbs.db");
        Assert.DoesNotContain(defaults, rule => rule.Pattern == "$RECYCLE.BIN");
    }

    [Fact]
    public void ExclusionRule_FileOnly_DoesNotExcludeFolderWithSameName()
    {
        var result = ScanWith(
            fs =>
            {
                fs.AddDirectory(Path.Combine(root, "data"));
                fs.AddFile(Path.Combine(root, "data", "inside.txt"), length: 1);
                fs.AddFile(Path.Combine(root, "data.tmp"), length: 1);
            },
            new ScanTraversalOptions
            {
                ExclusionRules = [new ScanExclusionRule("data", ScanEntryType.File, IsInclude: false)]
            });

        Assert.Contains(result.Documents, d => d.Name == "data" && d.Type == CatalogDocumentType.Folder);
        Assert.Contains(result.Documents, d => d.Name == "inside.txt");
        Assert.Contains(result.Documents, d => d.Name == "data.tmp");
    }

    [Fact]
    public void ExclusionRule_FolderOnly_DoesNotExcludeFileWithSameName()
    {
        var result = ScanWith(
            fs =>
            {
                fs.AddFile(Path.Combine(root, "data"), length: 1);
                fs.AddDirectory(Path.Combine(root, "data-folder"));
            },
            new ScanTraversalOptions
            {
                ExclusionRules =
                [
                    new ScanExclusionRule("data", ScanEntryType.Folder, IsInclude: false),
                    new ScanExclusionRule("data-folder", ScanEntryType.Folder, IsInclude: false)
                ]
            });

        Assert.Contains(result.Documents, d => d.Name == "data" && d.Type == CatalogDocumentType.File);
        Assert.DoesNotContain(result.Documents, d => d.Name == "data-folder");
    }

    [Fact]
    public void ExclusionRule_MatchingEntryType_IsSkipped()
    {
        var result = ScanWith(
            fs =>
            {
                fs.AddFile(Path.Combine(root, "data"), length: 1);
                fs.AddFile(Path.Combine(root, "keep.bin"), length: 1);
            },
            new ScanTraversalOptions
            {
                ExclusionRules = [new ScanExclusionRule("data", ScanEntryType.Both, IsInclude: false)]
            });

        Assert.DoesNotContain(result.Documents, d => d.Name == "data");
        Assert.Contains(result.Documents, d => d.Name == "keep.bin");
    }

    [Fact]
    public void InclusionRules_WhitelistOnlyMatchingEntries()
    {
        var result = ScanWith(
            fs =>
            {
                fs.AddFile(Path.Combine(root, "keep.txt"), length: 1);
                fs.AddFile(Path.Combine(root, "drop.bin"), length: 1);
            },
            new ScanTraversalOptions
            {
                InclusionRules = [new ScanExclusionRule("*.txt", ScanEntryType.Both, IsInclude: true)]
            });

        Assert.Contains(result.Documents, d => d.Name == "keep.txt");
        Assert.DoesNotContain(result.Documents, d => d.Name == "drop.bin");
    }

    [Fact]
    public void InclusionRules_EntryTypeScoped_KeepsOtherKindsOut()
    {
        var result = ScanWith(
            fs =>
            {
                fs.AddDirectory(Path.Combine(root, "docs"));
                fs.AddFile(Path.Combine(root, "note.txt"), length: 1);
            },
            new ScanTraversalOptions
            {
                InclusionRules = [new ScanExclusionRule("docs", ScanEntryType.Folder, IsInclude: true)]
            });

        Assert.Contains(result.Documents, d => d.Name == "docs" && d.Type == CatalogDocumentType.Folder);
        Assert.DoesNotContain(result.Documents, d => d.Name == "note.txt");
    }

    [Fact]
    public void SkipRecycleBins_ToggleControlsRecycleBinEntries()
    {
        Action<FakeScanFileSystem> populate = fs =>
        {
            fs.AddDirectory(Path.Combine(root, "$RECYCLE.BIN"));
            fs.AddFile(Path.Combine(root, "$RECYCLE.BIN", "junk.dat"), length: 1);
            fs.AddFile(Path.Combine(root, "visible.txt"), length: 1);
        };

        var skipped = ScanWith(populate, new ScanTraversalOptions { SkipRecycleBins = true });
        Assert.DoesNotContain(skipped.Documents, d => d.Name == "$RECYCLE.BIN");
        Assert.DoesNotContain(skipped.Documents, d => d.Name == "junk.dat");
        Assert.Contains(skipped.Documents, d => d.Name == "visible.txt");

        var included = ScanWith(populate, new ScanTraversalOptions { SkipRecycleBins = false });
        Assert.Contains(included.Documents, d => d.Name == "$RECYCLE.BIN");
    }

    [Fact]
    public void SkipWellKnownSystemFolders_ToggleControlsSystemEntries()
    {
        Action<FakeScanFileSystem> populate = fs =>
        {
            fs.AddDirectory(Path.Combine(root, "System Volume Information"));
            fs.AddFile(Path.Combine(root, "Thumbs.db"), length: 1);
            fs.AddFile(Path.Combine(root, "visible.txt"), length: 1);
        };

        var skipped = ScanWith(populate, new ScanTraversalOptions { SkipWellKnownSystemFolders = true });
        Assert.DoesNotContain(skipped.Documents, d => d.Name == "System Volume Information");
        Assert.DoesNotContain(skipped.Documents, d => d.Name == "Thumbs.db");
        Assert.Contains(skipped.Documents, d => d.Name == "visible.txt");

        var included = ScanWith(populate, new ScanTraversalOptions
        {
            SkipWellKnownSystemFolders = false,
            ExclusionPatterns = [],
            ExclusionRules = []
        });
        Assert.Contains(included.Documents, d => d.Name == "System Volume Information");
        Assert.Contains(included.Documents, d => d.Name == "Thumbs.db");
    }

    [Fact]
    public void SkipEmptyFolders_SuppressesEmptyAndCascadeEmptyFolders()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.AddDirectory(Path.Combine(root, "empty"));
        fs.AddDirectory(Path.Combine(root, "nested"));
        fs.AddDirectory(Path.Combine(root, "nested", "inner"));
        fs.AddDirectory(Path.Combine(root, "full"));
        fs.AddFile(Path.Combine(root, "full", "a.txt"), length: 1);

        var scanner = new FolderCatalogScanner(fs);
        var skipped = scanner.ScanDetailed(new FolderScanOptions(
            root,
            Traversal: new ScanTraversalOptions { SkipEmptyFolders = true }));
        Assert.DoesNotContain(skipped.Documents, d => d.Name == "empty");
        Assert.DoesNotContain(skipped.Documents, d => d.Name == "inner");
        Assert.DoesNotContain(skipped.Documents, d => d.Name == "nested");
        Assert.Contains(skipped.Documents, d => d.Name == "full" && d.Type == CatalogDocumentType.Folder);
        Assert.Contains(skipped.Documents, d => d.Name == "a.txt");

        var included = scanner.ScanDetailed(new FolderScanOptions(
            root,
            Traversal: new ScanTraversalOptions { SkipEmptyFolders = false }));
        Assert.Contains(included.Documents, d => d.Name == "empty" && d.Type == CatalogDocumentType.Folder);
    }

    [Fact]
    public void SkipEmptyFolders_KeepsFolderWithSuppressedSiblingChildrenCounts()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.AddDirectory(Path.Combine(root, "parent"));
        fs.AddDirectory(Path.Combine(root, "parent", "child"));
        fs.AddFile(Path.Combine(root, "parent", "child", "leaf.txt"), length: 4);

        var result = new FolderCatalogScanner(fs).ScanDetailed(new FolderScanOptions(
            root,
            Traversal: new ScanTraversalOptions { SkipEmptyFolders = true }));

        var parent = Assert.Single(result.Documents, d => d.Name == "parent");
        var child = Assert.Single(result.Documents, d => d.Name == "child");
        Assert.Equal(1, parent.ChildrenCount);
        Assert.Equal(1, child.ChildrenCount);
    }

    [Fact]
    public void SkipOnlineOnlyCloudFiles_SkipsOfflineAttributedFiles()
    {
        var fs = new FakeScanFileSystem();
        fs.AddDirectory(root);
        fs.AddFile(Path.Combine(root, "cloud.txt"), length: 1, attributes: FileAttributes.Offline);
        fs.AddFile(Path.Combine(root, "local.txt"), length: 1);

        var scanner = new FolderCatalogScanner(fs);
        var skipped = scanner.ScanDetailed(new FolderScanOptions(
            root,
            Traversal: new ScanTraversalOptions { SkipOnlineOnlyCloudFiles = true }));
        Assert.DoesNotContain(skipped.Documents, d => d.Name == "cloud.txt");
        Assert.Contains(skipped.Documents, d => d.Name == "local.txt");

        var included = scanner.ScanDetailed(new FolderScanOptions(
            root,
            Traversal: new ScanTraversalOptions { SkipOnlineOnlyCloudFiles = false }));
        Assert.Contains(included.Documents, d => d.Name == "cloud.txt");
    }

    [Fact]
    public void BuildTraversalOptions_MapsPersistedSettingsAndSplitsRules()
    {
        var options = new AppOptionsDocument
        {
            SkipSymbolicLinks = true,
            HiddenSystemFilePolicy = "SkipHidden",
            SkipRecycleBins = false,
            SkipWellKnownSystemFolders = false,
            SkipEmptyFolders = true,
            SkipOnlineOnlyCloudFiles = true
        };
        var rules = new List<ScanExclusionRuleDocument>
        {
            new() { Pattern = "*.tmp", EntryType = "File", IsInclude = false },
            new() { Pattern = "*.keep", EntryType = "Folder", IsInclude = true },
            new() { Pattern = "   ", EntryType = "File", IsInclude = false }
        };

        var traversal = ScanSettingsApplicator.BuildTraversalOptions(options, rules);

        Assert.Equal(LinkFollowPolicy.NeverFollowLinks, traversal.LinkFollowPolicy);
        Assert.Equal(HiddenSystemFilePolicy.SkipHidden, traversal.HiddenSystemFilePolicy);
        Assert.False(traversal.SkipRecycleBins);
        Assert.False(traversal.SkipWellKnownSystemFolders);
        Assert.True(traversal.SkipEmptyFolders);
        Assert.True(traversal.SkipOnlineOnlyCloudFiles);

        var exclusion = Assert.Single(traversal.ExclusionRules);
        Assert.Equal("*.tmp", exclusion.Pattern);
        Assert.Equal(ScanEntryType.File, exclusion.EntryType);
        Assert.False(exclusion.IsInclude);

        var inclusion = Assert.Single(traversal.InclusionRules);
        Assert.Equal("*.keep", inclusion.Pattern);
        Assert.Equal(ScanEntryType.Folder, inclusion.EntryType);
        Assert.True(inclusion.IsInclude);
    }

    [Fact]
    public void ScanRuleRepository_EnsureDefaultReplaceAll_RoundTrips()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositories = provider.Resolve<RepositoryManager>();
        var ruleRepository =
            (ScanExclusionRuleDocumentRepository)repositories.For<ScanExclusionRuleDocument>();

        Assert.Empty(ruleRepository.GetAll());
        ruleRepository.EnsureDefaultRules();
        var defaults = ruleRepository.GetOrdered();
        Assert.Equal(ruleRepository.CreateDefaultEntries().Count, defaults.Count);
        Assert.All(defaults, rule => Assert.False(string.IsNullOrWhiteSpace(rule.Id)));

        ruleRepository.ReplaceAll(
        [
            new ScanExclusionRuleDocument { Pattern = "*.tmp", EntryType = "File", IsInclude = false },
            new ScanExclusionRuleDocument { Pattern = "*.keep", EntryType = "Both", IsInclude = true },
            new ScanExclusionRuleDocument { Pattern = "   ", EntryType = "File", IsInclude = false }
        ]);

        var replaced = ruleRepository.GetOrdered();
        Assert.Equal(2, replaced.Count);
        var tmpRule = Assert.Single(replaced, rule => rule.Pattern == "*.tmp");
        Assert.Equal("File", tmpRule.EntryType);
        Assert.False(tmpRule.IsInclude);
        var keepRule = Assert.Single(replaced, rule => rule.Pattern == "*.keep");
        Assert.Equal("Both", keepRule.EntryType);
        Assert.True(keepRule.IsInclude);

        // Existing rules are preserved when defaults are ensured again.
        ruleRepository.EnsureDefaultRules();
        Assert.Equal(2, ruleRepository.GetOrdered().Count);

        ruleRepository.ReplaceAll(ruleRepository.CreateDefaultEntries());
        Assert.Equal(ruleRepository.CreateDefaultEntries().Count, ruleRepository.GetOrdered().Count);
    }

    [Fact]
    public void AppOptionsDocument_PersistsScanTogglesAcrossRepositoryInstances()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositories = provider.Resolve<RepositoryManager>();
        var optionsRepository = (AppOptionsDocumentRepository)repositories.For<AppOptionsDocument>();

        var options = optionsRepository.GetOrCreateAppOptions();
        options.SkipSymbolicLinks = true;
        options.HiddenSystemFilePolicy = "SkipHiddenAndSystem";
        options.SkipRecycleBins = false;
        options.SkipWellKnownSystemFolders = false;
        options.SkipEmptyFolders = true;
        options.SkipOnlineOnlyCloudFiles = true;
        optionsRepository.Save(AppOptionsDocument.DocumentId, options);

        var restored = optionsRepository.GetOrCreateAppOptions();
        Assert.True(restored.SkipSymbolicLinks);
        Assert.Equal("SkipHiddenAndSystem", restored.HiddenSystemFilePolicy);
        Assert.False(restored.SkipRecycleBins);
        Assert.False(restored.SkipWellKnownSystemFolders);
        Assert.True(restored.SkipEmptyFolders);
        Assert.True(restored.SkipOnlineOnlyCloudFiles);
    }

    [Fact]
    public void OptionsDialogViewModel_ScanSettingsRoundTrip()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SetScanOptions(new ScanSettingsSnapshot(true, "SkipHiddenAndSystem", false, false, true, true));
        vm.SetScanExclusionRules(
        [
            new ScanExclusionRuleDocument { Pattern = "*.tmp", EntryType = "File", IsInclude = false },
            new ScanExclusionRuleDocument { Pattern = "*.keep", EntryType = "Folder", IsInclude = true }
        ]);

        var scanOptions = vm.GetScanOptions();
        Assert.True(scanOptions.SkipSymbolicLinks);
        Assert.Equal("SkipHiddenAndSystem", scanOptions.HiddenSystemFilePolicy);
        Assert.False(scanOptions.SkipRecycleBins);
        Assert.False(scanOptions.SkipWellKnownSystemFolders);
        Assert.True(scanOptions.SkipEmptyFolders);
        Assert.True(scanOptions.SkipOnlineOnlyCloudFiles);

        var rules = vm.GetScanExclusionRules();
        Assert.Equal(2, rules.Count);
        var tmpRule = Assert.Single(rules, rule => rule.Pattern == "*.tmp");
        Assert.Equal("File", tmpRule.EntryType);
        Assert.False(tmpRule.IsInclude);
        var keepRule = Assert.Single(rules, rule => rule.Pattern == "*.keep");
        Assert.Equal("Folder", keepRule.EntryType);
        Assert.True(keepRule.IsInclude);
    }

    [Fact]
    public void OptionsDialogViewModel_AddRemoveResetScanRules()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetScanExclusionRules(
            [new ScanExclusionRuleDocument { Pattern = "keep", EntryType = "Both", IsInclude = false }]);

        vm.AddScanExclusionRuleCommand.Execute(null);
        Assert.Equal(2, vm.ScanExclusionRules.Count);
        Assert.Same(vm.ScanExclusionRules[1], vm.SelectedScanExclusionRule);

        vm.SelectedScanExclusionRule = vm.ScanExclusionRules[0];
        vm.RemoveScanExclusionRuleCommand.Execute(null);
        Assert.Single(vm.ScanExclusionRules);

        var resetRaised = false;
        vm.ResetScanExclusionRulesRequested += (_, _) => resetRaised = true;
        vm.ResetScanExclusionRulesCommand.Execute(null);
        Assert.True(resetRaised);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", previousAppData);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousXdgConfig);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);

        if (Directory.Exists(appDataRoot))
        {
            try
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
            catch (IOException)
            {
                // Couchbase Lite can release file handles slightly after dispose on some systems.
            }
        }
    }
}
