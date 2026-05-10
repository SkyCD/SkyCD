using System;
using System.Collections.Generic;
using System.IO;
using Couchbase.Lite;
using DryIoc;
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
        Assert.Equal(typeof(DefaultRepository), mapping.RepositoryType);
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
        Assert.Equal("Nested", mutable.GetDictionary(nameof(MappedPayload.Child))?.GetString(nameof(ChildPayload.Name)));
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

        var repository = repositories.GetOrAdd(typeof(AnnotatedDocument));

        Assert.IsType<TestTreeRepository>(repository);
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

        var loaded = typedRepository.Get<AnnotatedDocument>("root");
        var created = typedRepository.GetOrCreate<AnnotatedDocument>("new-id");
        var roots = typedRepository.GetRoots<AnnotatedDocument>();
        var children = typedRepository.GetChildrenOf<AnnotatedDocument>("root");
        var descendants = typedRepository.GetDescendantsOf<AnnotatedDocument>("root");

        Assert.NotNull(loaded);
        Assert.Equal("root", loaded!.Id);
        Assert.Equal("new-id", created.Id);
        Assert.Single(roots);
        Assert.Single(children);
        Assert.Single(descendants);
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
        [Id]
        public string Id { get; set; } = string.Empty;

        [ParentId]
        public string? ParentId { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class TestTreeRepository : TreeRepository
    {
    }

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
