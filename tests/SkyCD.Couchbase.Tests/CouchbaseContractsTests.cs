using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Couchbase.Lite;
using DryIoc;
using Microsoft.Extensions.Logging;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Attributes;
using SkyCD.Couchbase.Collections;
using SkyCD.Couchbase.Decorators;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Couchbase.Helpers;
using SkyCD.Couchbase.Mapping;
using SkyCD.Couchbase.Models;
using SkyCD.Couchbase.Repository;
using Xunit;

namespace SkyCD.Couchbase.Tests;

public class CouchbaseContractsTests
{
    [Fact]
    public void CouchbaseServiceRegistrator_IsolatesDefaultDatabasesBetweenTestContainers()
    {
        var previousDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", null);
            using var first = new Container();
            using var second = new Container();
            new CouchbaseServiceRegistrator().RegisterServices(first);
            new CouchbaseServiceRegistrator().RegisterServices(second);

            Assert.NotEqual(first.Resolve<DatabaseManager>().GetDatabase().Path,
                second.Resolve<DatabaseManager>().GetDatabase().Path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDirectory);
        }
    }

    [Fact]
    public void AttributeMarkers_AreDiscoverable()
    {
        var idAttribute = typeof(AnnotatedDocument).GetProperty(nameof(AnnotatedDocument.Id))!
            .GetCustomAttributes(typeof(Id), inherit: true);
        var parentAttribute = typeof(AnnotatedDocument).GetProperty(nameof(AnnotatedDocument.ParentId))!
            .GetCustomAttributes(typeof(ParentId), inherit: true);

        Assert.Single(idAttribute);
        Assert.Single(parentAttribute);
    }

    [Fact]
    public void CouchbaseDocument_UsesDefaultsAndRejectsInvalidRepositoryType()
    {
        var mapping = new CouchbaseDocument("docs");
        Assert.Equal("docs", mapping.CollectionName);
        Assert.Equal(typeof(DefaultRepository<>), mapping.RepositoryType);
        Assert.Equal("default", mapping.Database);

        Assert.ThrowsAny<ArgumentException>(() => new CouchbaseDocument("docs", typeof(string)));
    }

    [Fact]
    public void DocumentPropertyBinding_StoresNameAndProperty()
    {
        var property = typeof(AnnotatedDocument).GetProperty(nameof(AnnotatedDocument.Id));
        var binding = new DocumentPropertyBinding("Id", property);

        Assert.Equal("Id", binding.Name);
        Assert.Same(property, binding.Property);
    }

    [Fact]
    public void AttributeHelper_ResolvesAttributedAndFallbackProperties()
    {
        var idBinding = AttributeHelper.ResolveStringPropertyWithAttributeOrDefault(
            typeof(AnnotatedDocument), typeof(Id), "Id");
        var parentBinding = AttributeHelper.ResolveStringPropertyWithAttributeOrDefault(
            typeof(AnnotatedDocument), typeof(ParentId), "ParentId");

        Assert.Equal(nameof(AnnotatedDocument.Id), idBinding.Name);
        Assert.Equal(nameof(AnnotatedDocument.ParentId), parentBinding.Name);
        Assert.NotNull(idBinding.Property);
        Assert.NotNull(parentBinding.Property);
    }

    [Fact]
    public void MutableDictionaryDecorator_WritesSimpleAndNestedObjects()
    {
        var mutable = new MutableDictionaryObject();
        var decorator = new MutableDictionaryDecorator(mutable);
        var source = new MappedPayload
        {
            Name = "Root",
            Count = 2,
            Tags = ["a", "b"],
            Child = new ChildPayload { Name = "Nested" }
        };

        decorator.WriteObject(source);

        Assert.Equal("Root", mutable.GetString(nameof(MappedPayload.Name)));
        Assert.Equal(2, mutable.GetInt(nameof(MappedPayload.Count)));
        Assert.NotNull(mutable.GetArray(nameof(MappedPayload.Tags)));
        Assert.Equal("Nested",
            mutable.GetDictionary(nameof(MappedPayload.Child))?.GetString(nameof(ChildPayload.Name)));
    }

    [Fact]
    public void DocumentMappingExtensions_RoundTripsObjects()
    {
        var payload = new MappedPayload
        {
            Name = "Test",
            Count = 3,
            Tags = ["x", "y"]
        };

        using var mutable = payload.ToMutableDocument("doc-1");
        var mapped = mutable.FromDocument<MappedPayload>();

        Assert.NotNull(mapped);
        Assert.Equal("Test", mapped!.Name);
        Assert.Equal(3, mapped.Count);
        Assert.Equal(2, mapped.Tags.Count);
    }

    [Fact]
    public void DatabaseCollection_AddAndRemove_Works()
    {
        using var database = CreateDatabase("collection-test");
        var collection = new DatabaseCollection();

        collection.Add("default", database);

        Assert.True(collection.ContainsKey("default"));
        Assert.True(collection.Remove("default"));
    }

    [Fact]
    public void RepositoryCollection_GetOrAdd_CreatesConfiguredRepository()
    {
        using var database = CreateDatabase("repo-collection");
        var databases = new DatabaseCollection();
        databases.Add("default", database);
        var repositories = new RepositoryCollection(databases);

        var repository = Assert.IsType<TestTreeRepository>(repositories.GetOrAdd(typeof(AnnotatedDocument)));

        Assert.Equal("annotated", repository.CollectionName);
    }

    [Fact]
    public void DatabaseManager_ConnectGetForAndDisconnect_Work()
    {
        const string databaseName = "default";
        var directory = CreateTempDirectory();
        using var manager = new DatabaseManager();

        var connected = manager.Connect(databaseName, directory);
        var fetched = manager.GetDatabase(databaseName);
        var resolvedForType = manager.GetFor<AnnotatedDocument>();

        Assert.Same(connected, fetched);
        Assert.Same(connected, resolvedForType);
        Assert.True(manager.Disconnect(databaseName));
    }

    [Fact]
    public void DatabaseManager_Disconnect_ReleasesTheDatabaseFiles()
    {
        var directory = CreateTempDirectory();
        using var manager = new DatabaseManager();
        manager.Connect("disconnect-test", directory);

        Assert.True(manager.Disconnect("disconnect-test"));

        // The recursive delete only succeeds once the native handle is released, which is what lets a test
        // sandbox remove the database directory it owns.
        Directory.Delete(directory, recursive: true);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void DatabaseManager_Dispose_ReleasesTheDatabaseFiles()
    {
        var directory = CreateTempDirectory();
        var manager = new DatabaseManager();
        manager.Connect("dispose-test", directory);

        manager.Dispose();

        Directory.Delete(directory, recursive: true);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void RepositoryManager_ProvidesRepositoryWithCrudAndTreeApis()
    {
        var databaseName = "default";
        var directory = CreateTempDirectory();
        using var manager = new DatabaseManager();
        manager.Connect(databaseName, directory);
        var repositories = new RepositoryManager(manager);

        var repository = repositories.For<AnnotatedDocument>();
        var typedRepository = Assert.IsType<TestTreeRepository>(repository);

        typedRepository.Save("root", new AnnotatedDocument { Id = "root", Name = "Root" });
        typedRepository.Save("child", new AnnotatedDocument { Id = "child", ParentId = "root", Name = "Child" });

        var loaded = typedRepository.Get("root");
        var created = typedRepository.GetOrCreate("new-id");
        var roots = typedRepository.GetRoots();
        var children = typedRepository.GetChildrenOf("root");
        var descendants = typedRepository.GetDescendantsOf("root");
        var batchDescendants = typedRepository.GetDescendantsOf(["root", "non-existent"]);

        Assert.NotNull(loaded);
        Assert.Equal("root", loaded!.Id);
        Assert.Equal("new-id", created.Id);
        Assert.Single(roots);
        Assert.Single(children);
        Assert.Single(descendants);
        Assert.Single(batchDescendants);
        Assert.Equal("child", batchDescendants[0].Id);
    }

    [Fact]
    public void TreeRepository_GetChildrenOf_UsesAParentScopedQuery()
    {
        using var database = CreateDatabase("tree-children");
        var databases = new DatabaseCollection();
        databases.Add("default", database);
        var repositories = new RepositoryCollection(databases);
        var repository = Assert.IsType<CountingTreeRepository>(repositories.GetOrAdd(typeof(CountingDocument)));

        repository.Save("root", new CountingDocument { Id = "root", Name = "Root" });
        repository.Save("folder", new CountingDocument { Id = "folder", ParentId = "root", Name = "Folder" });
        repository.Save("other", new CountingDocument { Id = "other", ParentId = "root", Name = "Other" });
        for (var index = 0; index < 500; index++)
        {
            repository.Save($"noise-{index}",
                new CountingDocument { Id = $"noise-{index}", ParentId = "noise", Name = $"Noise {index}" });
        }

        for (var index = 0; index < 3; index++)
        {
            repository.Save($"child-{index}",
                new CountingDocument { Id = $"child-{index}", ParentId = "folder", Name = $"Child {index}" });
        }

        repository.GetAllCalls = 0;

        var roots = repository.GetRoots();
        var children = repository.GetChildrenOf("folder");
        var descendants = repository.GetDescendantsOf("root");

        Assert.Equal(["root"], roots.Select(static document => document.Id));
        Assert.Equal(3, children.Count);
        Assert.All(children, static child => Assert.Equal("folder", child.ParentId));
        Assert.Equal(5, descendants.Count);
        // Only GetDescendantsOf needs the whole tree, and it reuses the cached snapshot.
        Assert.Equal(1, repository.GetAllCalls);
    }

    [Fact]
    public void TreeRepository_GetAll_CachesTheSnapshotUntilItIsInvalidated()
    {
        using var database = CreateDatabase("tree-cache");
        var databases = new DatabaseCollection();
        databases.Add("default", database);
        var repositories = new RepositoryCollection(databases);
        var repository = Assert.IsType<TestTreeRepository>(repositories.GetOrAdd(typeof(AnnotatedDocument)));

        // The collection's change listener clears the cached snapshot on a background queue, a moment after
        // Save already invalidated it. Wait for that notification so it cannot land between the two reads.
        using var changeObserved = new ManualResetEventSlim(false);
        var changeToken = repository.Collection.AddChangeListener((_, _) => changeObserved.Set());

        repository.Save("a", new AnnotatedDocument { Id = "a", Name = "A" });
        Assert.True(
            changeObserved.Wait(TimeSpan.FromSeconds(10)),
            "The collection change notification did not arrive.");
        changeToken.Remove();

        var first = repository.GetAll();
        var second = repository.GetAll();

        Assert.Same(first, second);
        Assert.Single(first);

        repository.Save("b", new AnnotatedDocument { Id = "b", Name = "B" });
        var afterSave = repository.GetAll();

        Assert.NotSame(first, afterSave);
        Assert.Equal(2, afterSave.Count);

        // Imports and bulk deletes write through the collection; they invalidate explicitly afterwards.
        using (var imported = new AnnotatedDocument { Id = "c", Name = "C" }.ToMutableDocument("c"))
        {
            repository.Collection.Save(imported);
        }
        repository.InvalidateSnapshot();

        Assert.Equal(3, repository.GetAll().Count);

        using (var deleted = Assert.IsAssignableFrom<Document>(repository.Collection.GetDocument("c")))
        {
            repository.Collection.Delete(deleted);
        }
        repository.InvalidateSnapshot();

        var afterDelete = repository.GetAll();
        Assert.Equal(2, afterDelete.Count);
        Assert.DoesNotContain(afterDelete, static document => document.Id == "c");
    }

    [Fact]
    public void CouchbaseServiceRegistrator_RegistersManagers()
    {
        using var provider = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(provider);

        var databaseManager = provider.Resolve<DatabaseManager>(ifUnresolved: IfUnresolved.ReturnDefault);
        var repositoryManager = provider.Resolve<RepositoryManager>(ifUnresolved: IfUnresolved.ReturnDefault);

        Assert.NotNull(databaseManager);
        Assert.NotNull(repositoryManager);
    }

    [Fact]
    public void TreeRepository_ReportsTheParentIndexWhenItIsCreated()
    {
        using var database = CreateDatabase("tree-index-created");
        var databases = new DatabaseCollection();
        databases.Add("default", database);
        var loggerFactory = new CapturingLoggerFactory();
        var repositories = new RepositoryCollection(databases, loggerFactory);

        var repository = Assert.IsType<TestTreeRepository>(repositories.GetOrAdd(typeof(AnnotatedDocument)));

        Assert.True(repository.IsParentIndexAvailable);
        Assert.DoesNotContain(loggerFactory.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public void TreeRepository_OpensUnindexedWhenIndexCreationFails()
    {
        // Issue #1097: SQLite error 14 leaves the parent index unbuilt; the catalog still has to open.
        using var database = CreateDatabase("tree-index-failure");
        var databases = new DatabaseCollection();
        databases.Add("default", database);
        var loggerFactory = new CapturingLoggerFactory();
        var repositories = new RepositoryCollection(databases, loggerFactory);

        var repository = Assert.IsType<FailingIndexTreeRepository>(repositories.GetOrAdd(typeof(FailingIndexDocument)));

        Assert.False(repository.IsParentIndexAvailable);
        Assert.Equal(2, repository.CreateParentIndexCalls);

        var retries = loggerFactory.Entries.Where(entry => entry.Level == LogLevel.Warning).ToArray();
        Assert.Equal(2, retries.Length);
        Assert.All(retries, entry => Assert.Contains("idx_tree_parent_failingindex", entry.Message, StringComparison.Ordinal));

        var diagnostic = Assert.Single(loggerFactory.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("idx_tree_parent_failingindex", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("unavailable", diagnostic.Message, StringComparison.Ordinal);
        Assert.NotNull(diagnostic.Exception);

        // The unindexed repository still reads and writes; folder queries fall back to a scan.
        repository.Save("root", new FailingIndexDocument { Id = "root", Name = "Root" });
        repository.Save("child", new FailingIndexDocument { Id = "child", ParentId = "root", Name = "Child" });

        var children = repository.GetChildrenOf("root");

        Assert.Single(children);
        Assert.Equal("child", children[0].Id);
    }

    private static Database CreateDatabase(string prefix)
    {
        var name = $"{prefix}-{Guid.NewGuid():N}";
        var directory = CreateTempDirectory();
        return new Database(name, new DatabaseConfiguration { Directory = directory });
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "skycd-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [CouchbaseDocument("annotated", typeof(TestTreeRepository))]
    private sealed class AnnotatedDocument
    {
        [Id] public string Id { get; set; } = string.Empty;

        [ParentId] public string? ParentId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestTreeRepository : TreeRepository<AnnotatedDocument>
    {
    }

    [CouchbaseDocument("counting", typeof(CountingTreeRepository))]
    private sealed class CountingDocument
    {
        [Id] public string Id { get; set; } = string.Empty;

        [ParentId] public string? ParentId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class CountingTreeRepository : TreeRepository<CountingDocument>
    {
        public int GetAllCalls { get; set; }

        public override IReadOnlyList<CountingDocument> GetAll()
        {
            GetAllCalls++;
            return base.GetAll();
        }
    }

    [CouchbaseDocument("failingindex", typeof(FailingIndexTreeRepository))]
    private sealed class FailingIndexDocument
    {
        [Id] public string Id { get; set; } = string.Empty;

        [ParentId] public string? ParentId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class FailingIndexTreeRepository : TreeRepository<FailingIndexDocument>
    {
        public int CreateParentIndexCalls { get; private set; }

        protected override void CreateParentIndex(string indexName)
        {
            CreateParentIndexCalls++;
            throw new InvalidOperationException("unable to open database file (14/14)");
        }
    }

    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly List<LogEntry> entries = [];

        public IReadOnlyList<LogEntry> Entries
        {
            get
            {
                lock (entries)
                {
                    return entries.ToArray();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        internal void Add(LogLevel level, string message, Exception? exception)
        {
            lock (entries)
            {
                entries.Add(new LogEntry(level, message, exception));
            }
        }

        private sealed class CapturingLogger(CapturingLoggerFactory owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                owner.Add(logLevel, formatter(state, exception), exception);
            }
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class MappedPayload
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public List<string> Tags { get; set; } = [];
        public ChildPayload? Child { get; set; }
    }

    private sealed class ChildPayload
    {
        public string Name { get; set; } = string.Empty;
    }
}
