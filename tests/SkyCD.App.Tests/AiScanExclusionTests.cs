using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.ViewModels;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class AiScanExclusionTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();

    private static CatalogDocument Entry(string id, string name, long size = 128) => new()
    {
        Id = id,
        Name = name,
        Type = CatalogDocumentType.File,
        Size = size
    };

    [Fact]
    public void FileMimeTypeResolver_PrefersStoredContentTypeOverExtension()
    {
        var entry = Entry("entry", "photo.png");
        entry.Properties["ContentType"] = "application/pdf";

        Assert.Equal("application/pdf", FileMimeTypeResolver.Resolve(entry));
    }

    [Fact]
    public void FileMimeTypeResolver_ResolvesCommonExtensionsAndIgnoresUnknown()
    {
        Assert.Equal("application/zip", FileMimeTypeResolver.Resolve(Entry("a", "archive.zip")));
        Assert.Equal("application/x-msdownload", FileMimeTypeResolver.Resolve(Entry("b", "tool.EXE")));
        Assert.Equal("image/png", FileMimeTypeResolver.Resolve(Entry("c", "photo.png")));
        Assert.Equal("video/mp4", FileMimeTypeResolver.Resolve(Entry("d", "clip.mp4")));
        Assert.Equal("application/zip", FileMimeTypeResolver.FromExtension("zip"));
        Assert.Null(FileMimeTypeResolver.Resolve(Entry("e", "mystery.qz32")));
        Assert.Null(FileMimeTypeResolver.FromExtension(".qz32"));
    }

    [Fact]
    public void DefaultMimeExclusions_CoverExecutableAndArchiveTypes()
    {
        var defaults = WellKnownAiScanExclusions.GetDefaultExcludedMimeTypes();

        Assert.Contains("application/x-msdownload", defaults);
        Assert.Contains("application/x-executable", defaults);
        Assert.Contains("application/x-sharedlib", defaults);
        Assert.Contains("application/vnd.android.package-archive", defaults);
        Assert.Contains("application/zip", defaults);
        Assert.Contains("application/x-7z-compressed", defaults);
        Assert.Contains("application/vnd.rar", defaults);
        Assert.Contains("application/gzip", defaults);
        Assert.Contains("application/x-tar", defaults);
        Assert.Contains("application/java-archive", defaults);
        Assert.All(defaults, mimeType => Assert.Contains('/', mimeType));
        Assert.Equal(defaults.Count, defaults.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void AiScanPolicy_DefaultExclusionsSkipExecutablesAndArchives()
    {
        var policy = new AiScanPolicy(WellKnownAiScanExclusions.GetDefaultExcludedMimeTypes(), AiScanPolicy.DefaultMaxFileSizeBytes);

        Assert.False(policy.ShouldScan(Entry("exe", "setup.exe")));
        Assert.False(policy.ShouldScan(Entry("zip", "backup.zip")));
        Assert.False(policy.ShouldScan(Entry("jar", "library.jar")));
        Assert.False(policy.ShouldScan(1, "APPLICATION/X-MSDOWNLOAD"));
        Assert.True(policy.ShouldScan(Entry("txt", "notes.txt")));
        Assert.True(policy.ShouldScan(Entry("png", "photo.png")));
        Assert.True(policy.ShouldScan(1, null));
    }

    [Fact]
    public void AiScanPolicy_CustomExclusionsSkipMatchingMimeTypes()
    {
        var policy = new AiScanPolicy(["image/png", "video/mp4"], AiScanPolicy.DefaultMaxFileSizeBytes);

        Assert.False(policy.ShouldScan(Entry("png", "photo.png")));
        Assert.False(policy.ShouldScan(Entry("mp4", "clip.mp4")));
        Assert.True(policy.ShouldScan(Entry("jpg", "photo.jpg")));
    }

    [Fact]
    public void AiScanPolicy_SkipsFilesLargerThanLimit()
    {
        var policy = new AiScanPolicy(null, maxFileSizeBytes: 1000);

        Assert.True(policy.ShouldScan(Entry("exact", "file.bin", size: 1000)));
        Assert.False(policy.ShouldScan(Entry("over", "file.bin", size: 1001)));
        Assert.Equal(1024L * 1024L * 1024L, new AiScanPolicy(null, 0).MaxFileSizeBytes);
    }

    [Fact]
    public async Task AiDescriptionService_SkipsExcludedAndOversizedEntries()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositories = provider.Resolve<RepositoryManager>();
        var catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
        using var store = new CouchbaseMediaCacheStore(catalog.Collection.Database);
        catalog.Save("excluded", Entry("excluded", "setup.exe"));
        catalog.Save("oversized", Entry("oversized", "huge.txt", size: 2048));
        catalog.Save("allowed", Entry("allowed", "notes.txt"));

        var capability = new CountingCapability();
        var policy = new AiScanPolicy(WellKnownAiScanExclusions.GetDefaultExcludedMimeTypes(), maxFileSizeBytes: 1024);
        var service = new AiDescriptionService(catalog, store, capability, () => policy);

        var excluded = await service.GenerateAsync("excluded", regenerate: true, CancellationToken.None);
        var oversized = await service.GenerateAsync("oversized", regenerate: true, CancellationToken.None);
        Assert.True(excluded.Skipped);
        Assert.True(oversized.Skipped);
        Assert.Equal(0, capability.Calls);

        var allowed = await service.GenerateAsync("allowed", regenerate: true, CancellationToken.None);
        Assert.False(allowed.Skipped);
        Assert.Equal(1, capability.Calls);
    }

    [Fact]
    public void AiScanExclusionRepository_EnsureDefaultReplaceAll_RoundTrips()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositories = provider.Resolve<RepositoryManager>();
        var repository =
            (AiScanExclusionDocumentRepository)repositories.For<AiScanExclusionDocument>();

        Assert.Empty(repository.GetAll());
        repository.EnsureDefaultEntries();
        var defaults = repository.GetOrdered();
        Assert.Equal(repository.CreateDefaultEntries().Count, defaults.Count);
        Assert.All(defaults, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Id)));
        Assert.Contains(defaults, entry => entry.MimeType == "application/zip");

        repository.ReplaceAll(
        [
            new AiScanExclusionDocument { MimeType = "image/png" },
            new AiScanExclusionDocument { MimeType = "   " }
        ]);

        var replaced = repository.GetOrdered();
        var exclusion = Assert.Single(replaced);
        Assert.Equal("image/png", exclusion.MimeType);

        repository.EnsureDefaultEntries();
        Assert.Single(repository.GetOrdered());

        repository.ReplaceAll(repository.CreateDefaultEntries());
        Assert.Equal(repository.CreateDefaultEntries().Count, repository.GetOrdered().Count);
    }

    [Fact]
    public void AppOptionsDocument_PersistsAiScanMaxFileSizeAcrossRepositoryInstances()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositories = provider.Resolve<RepositoryManager>();
        var optionsRepository = (AppOptionsDocumentRepository)repositories.For<AppOptionsDocument>();

        var options = optionsRepository.GetOrCreateAppOptions();
        Assert.Equal(AiScanPolicy.DefaultMaxFileSizeBytes, options.AiScanMaxFileSizeBytes);
        options.AiScanMaxFileSizeBytes = 42L * 1024 * 1024;
        optionsRepository.Save(AppOptionsDocument.DocumentId, options);

        var restored = optionsRepository.GetOrCreateAppOptions();
        Assert.Equal(42L * 1024 * 1024, restored.AiScanMaxFileSizeBytes);
    }

    [Fact]
    public void AppOptionsDocument_PersistsIncludeExtendedInfoAcrossRepositoryInstances()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);
        var repositories = provider.Resolve<RepositoryManager>();
        var optionsRepository = (AppOptionsDocumentRepository)repositories.For<AppOptionsDocument>();

        var options = optionsRepository.GetOrCreateAppOptions();
        Assert.False(options.IncludeExtendedInfo);
        options.IncludeExtendedInfo = true;
        optionsRepository.Save(AppOptionsDocument.DocumentId, options);

        var restored = optionsRepository.GetOrCreateAppOptions();
        Assert.True(restored.IncludeExtendedInfo);
    }

    [Fact]
    public void OptionsDialogViewModel_AiScanSettingsRoundTrip()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SetAiDescriptionAvailable(true);
        Assert.Contains(vm.SettingCategories, category => category.Id == "ai-description" && category.Title == "Description");
        Assert.Contains(vm.SettingCategories, category => category.Id == "ai-provider" && category.Title == "Provider");

        vm.AiScanMaxFileSizeMegabytes = 2048;
        vm.SetAiScanExclusions(
        [
            new AiScanExclusionDocument { MimeType = "image/png" },
            new AiScanExclusionDocument { MimeType = "video/mp4" }
        ]);

        Assert.Equal(2048, vm.AiScanMaxFileSizeMegabytes);
        var exclusions = vm.GetAiScanExclusions();
        Assert.Equal(2, exclusions.Count);
        Assert.Contains(exclusions, entry => entry.MimeType == "image/png");
        Assert.Contains(exclusions, entry => entry.MimeType == "video/mp4");

        vm.SetAiDescriptionAvailable(false);
        Assert.DoesNotContain(vm.SettingCategories, category => category.Id == "ai-description");
        Assert.DoesNotContain(vm.SettingCategories, category => category.Id == "ai-provider");
    }

    [Fact]
    public void OptionsDialogViewModel_AddRemoveResetAiScanExclusions()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetAiScanExclusions([new AiScanExclusionDocument { MimeType = "keep/type" }]);

        vm.AddAiScanExclusionCommand.Execute(null);
        Assert.Equal(2, vm.AiScanExclusions.Count);
        Assert.Same(vm.AiScanExclusions[1], vm.SelectedAiScanExclusion);

        vm.SelectedAiScanExclusion = vm.AiScanExclusions[0];
        vm.RemoveAiScanExclusionCommand.Execute(null);
        Assert.Single(vm.AiScanExclusions);

        var resetRaised = false;
        vm.ResetAiScanExclusionsRequested += (_, _) => resetRaised = true;
        vm.ResetAiScanExclusionsCommand.Execute(null);
        Assert.True(resetRaised);
    }

    private sealed class CountingCapability : IGeneratedDescriptionCapability, IDescriptionGenerationPlanner
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public MediaMetadataSchema Schema => new("test.ai-scan.schema", 1, []);

        public Task<GeneratedDescription> GenerateAsync(DescriptionContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new GeneratedDescription(
                "generated", "fake-agent", "fake-model", "1.0.0", "prompt-v1", "English",
                "content-based AI description", "cloud", DateTimeOffset.UtcNow));
        }

        public DescriptionGenerationPlan GetPlan() => new(
            true, "fake-provider", "2.0.0", "fake-model", "cloud",
            new DescriptionContextOptions(Filename: true));
    }

    public void Dispose() => sandbox.Dispose();
}
