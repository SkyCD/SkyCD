using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.Search.Exceptions;
using Xunit;
using Xunit.Abstractions;

namespace SkyCD.App.Tests;

public sealed class AdvancedCatalogSearchTests
{
    private readonly ITestOutputHelper output;

    public AdvancedCatalogSearchTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static List<CatalogDocument> CreateCatalog()
    {
        return
        [
            new() { Id = "disc-a", Name = "Disc A", Type = CatalogDocumentType.Media },
            new() { Id = "photos-a", ParentId = "disc-a", Name = "Photos", Type = CatalogDocumentType.Folder },
            new() { Id = "file-a", ParentId = "photos-a", Name = "Sunset.JPG", Type = CatalogDocumentType.File, Size = 2_500_000, Status = "Archived" },
            new() { Id = "file-b", ParentId = "photos-a", Name = "Notes.txt", Type = CatalogDocumentType.File, Size = 512 },
            new() { Id = "disc-b", ParentId = null, Name = "Disc B", Type = CatalogDocumentType.Media },
            new() { Id = "photos-b", ParentId = "disc-b", Name = "Photos", Type = CatalogDocumentType.Folder },
            new() { Id = "file-c", ParentId = "photos-b", Name = "sunset.jpg", Type = CatalogDocumentType.File, Size = 3_100_000 },
        ];
    }

    private static CatalogSearchIndex CreateIndex(IEnumerable<CatalogDocument> documents)
    {
        var index = new CatalogSearchIndex();
        index.Rebuild(documents);
        return index;
    }

    [Fact]
    public void Search_CombinedPredicates_MatchOnlyEntriesSatisfyingAll()
    {
        var index = CreateIndex(CreateCatalog());
        var query = new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Combination = CatalogQueryCombination.All,
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.Extension,
                        Operator = AdvancedCatalogOperator.Equals,
                        Value = "jpg"
                    },
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.SizeBytes,
                        Operator = AdvancedCatalogOperator.GreaterThan,
                        Value = 2_000_000L
                    }
                ]
            }
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Equal(2, results.Count);
        Assert.All(results, match => Assert.Equal("jpg", GetExtension(match.Document.Name)));
    }

    [Fact]
    public void Search_NestedOrGroup_WithSubtreeScope()
    {
        var index = CreateIndex(CreateCatalog());
        var query = new AdvancedCatalogQuery
        {
            Scope = new CatalogQueryScope { RootId = "disc-a", IncludeDescendants = true },
            Root = new CatalogQueryGroup
            {
                Combination = CatalogQueryCombination.Any,
                Groups =
                [
                    new CatalogQueryGroup
                    {
                        Predicates =
                        [
                            new AdvancedCatalogPredicate
                            {
                                Field = AdvancedCatalogField.Name,
                                Operator = AdvancedCatalogOperator.Contains,
                                Value = "sunset"
                            }
                        ]
                    },
                    new CatalogQueryGroup
                    {
                        Predicates =
                        [
                            new AdvancedCatalogPredicate
                            {
                                Field = AdvancedCatalogField.Extension,
                                Operator = AdvancedCatalogOperator.Equals,
                                Value = "txt"
                            }
                        ]
                    }
                ]
            }
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, match => match.Document.Id == "file-c");
    }

    [Fact]
    public void Search_ScopeExcludingDescendants_ReturnsOnlyRoot()
    {
        var index = CreateIndex(CreateCatalog());
        var query = new AdvancedCatalogQuery
        {
            Scope = new CatalogQueryScope { RootId = "photos-a", IncludeDescendants = false }
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        var single = Assert.Single(results);
        Assert.Equal("photos-a", single.Document.Id);
    }

    [Fact]
    public void Search_MediaTypeScope_LimitsToMediaEntries()
    {
        var index = CreateIndex(CreateCatalog());
        var query = new AdvancedCatalogQuery
        {
            Scope = new CatalogQueryScope { Types = [CatalogDocumentType.Media] }
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Equal(2, results.Count);
        Assert.All(results, match => Assert.Equal(CatalogDocumentType.Media, match.Document.Type));
    }

    [Fact]
    public void Search_MissingStatus_IsMissingMatchesOnlyUnsetEntries()
    {
        var index = CreateIndex(CreateCatalog());
        var query = new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.Status,
                        Operator = AdvancedCatalogOperator.IsMissing
                    }
                ]
            }
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.NotEmpty(results);
        Assert.DoesNotContain(results, match => match.Document.Id == "file-a");
        Assert.All(results, match => Assert.True(string.IsNullOrWhiteSpace(match.Document.Status)));
    }

    [Fact]
    public void Search_UnknownMetadataKey_IsMissingAndNeverThrows()
    {
        var index = CreateIndex(CreateCatalog());
        var missing = new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.Metadata,
                        Operator = AdvancedCatalogOperator.IsMissing,
                        Key = "plugin.unknownField.v99"
                    }
                ]
            }
        };
        var present = new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.Metadata,
                        Operator = AdvancedCatalogOperator.IsPresent,
                        Key = "plugin.unknownField.v99"
                    }
                ]
            }
        };

        var missingResults = AdvancedCatalogSearchService.Search(index, missing);
        var presentResults = AdvancedCatalogSearchService.Search(index, present);

        Assert.Equal(index.Count, missingResults.Count);
        Assert.Empty(presentResults);
    }

    [Fact]
    public void Search_MetadataTimestamp_ComparesInstantsAcrossTimezones()
    {
        var documents = new List<CatalogDocument>
        {
            new() { Id = "root", Name = "Root", Type = CatalogDocumentType.Folder },
            new() { Id = "a", ParentId = "root", Name = "a.jpg", Type = CatalogDocumentType.File, Size = 10 },
            new() { Id = "b", ParentId = "root", Name = "b.jpg", Type = CatalogDocumentType.File, Size = 10 },
        };
        var index = CreateIndex(documents);
        var captured = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var sameInstantOtherZone = new DateTimeOffset(2026, 5, 1, 14, 0, 0, TimeSpan.FromHours(2));
        IReadOnlyDictionary<string, object?> Lookup(CatalogDocument document) => document.Id switch
        {
            "a" => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["capturedAt"] = captured
            },
            "b" => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["capturedAt"] = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
            },
            _ => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        };
        var query = new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.Metadata,
                        Operator = AdvancedCatalogOperator.Equals,
                        Key = "capturedAt",
                        Value = sameInstantOtherZone
                    }
                ]
            }
        };

        var results = AdvancedCatalogSearchService.Search(index, query, Lookup);

        var single = Assert.Single(results);
        Assert.Equal("a", single.Document.Id);
    }

    [Fact]
    public void Search_Pagination_IsDeterministicAndStable()
    {
        var index = CreateIndex(CreateCatalog());
        var query = new AdvancedCatalogQuery { Take = 2 };
        var first = AdvancedCatalogSearchService.Search(index, query with { Skip = 0 });
        var second = AdvancedCatalogSearchService.Search(index, query with { Skip = 2 });

        Assert.Equal(2, first.Count);
        Assert.DoesNotContain(second, match => first.Any(previous => previous.Document.Id == match.Document.Id));
        var full = AdvancedCatalogSearchService.Search(index, new AdvancedCatalogQuery { Take = 1000 });
        Assert.Equal(
            full.Select(match => match.Document.Id),
            first.Concat(second).Select(match => match.Document.Id).Concat(full.Skip(4).Select(match => match.Document.Id)));
    }

    [Fact]
    public void Search_CancelledToken_ThrowsOperationCanceled()
    {
        var index = CreateIndex(CreateCatalog());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            AdvancedCatalogSearchService.Search(index, new AdvancedCatalogQuery(), cancellationToken: cancelled.Token));
    }

    [Fact]
    public void Index_UpsertAndRemove_KeepEvaluationConsistent()
    {
        var index = CreateIndex(CreateCatalog());
        var added = new CatalogDocument
        {
            Id = "file-new",
            ParentId = "photos-a",
            Name = "Evening.jpg",
            Type = CatalogDocumentType.File,
            Size = 9_000_000
        };
        index.Upsert(added);
        var large = new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.SizeBytes,
                        Operator = AdvancedCatalogOperator.GreaterThan,
                        Value = 5_000_000L
                    }
                ]
            }
        };

        Assert.Contains(
            AdvancedCatalogSearchService.Search(index, large),
            match => match.Document.Id == "file-new");

        Assert.True(index.Remove("file-new"));

        Assert.DoesNotContain(
            AdvancedCatalogSearchService.Search(index, large),
            match => match.Document.Id == "file-new");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Validate_UnsupportedVersion_ThrowsQueryException(int version)
    {
        var query = new AdvancedCatalogQuery { Version = version };

        var exception = Assert.Throws<AdvancedCatalogQueryException>(() =>
            AdvancedCatalogSearchService.Search(CreateIndex(CreateCatalog()), query));

        Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_MetadataPredicateWithoutKey_ThrowsQueryException()
    {
        var query = new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.Metadata,
                        Operator = AdvancedCatalogOperator.Contains,
                        Value = "x"
                    }
                ]
            }
        };

        Assert.Throws<AdvancedCatalogQueryException>(() =>
            AdvancedCatalogSearchService.Search(CreateIndex(CreateCatalog()), query));
    }

    [Fact]
    public void Search_LargeCatalog_RecordsBaselineLatency()
    {
        const int count = 5000;
        var documents = new List<CatalogDocument>(count + 1)
        {
            new() { Id = "root", Name = "Root", Type = CatalogDocumentType.Folder }
        };
        for (var i = 0; i < count; i++)
        {
            documents.Add(new CatalogDocument
            {
                Id = $"file-{i:0000}",
                ParentId = "root",
                Name = $"photo-{i:0000}.jpg",
                Type = CatalogDocumentType.File,
                Size = i * 1024L,
                Status = i % 2 == 0 ? "Archived" : null
            });
        }

        var index = CreateIndex(documents);
        var query = new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Combination = CatalogQueryCombination.All,
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.Extension,
                        Operator = AdvancedCatalogOperator.Equals,
                        Value = "jpg"
                    },
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.Status,
                        Operator = AdvancedCatalogOperator.Equals,
                        Value = "archived"
                    },
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.SizeBytes,
                        Operator = AdvancedCatalogOperator.GreaterThanOrEqual,
                        Value = 1024L * 100
                    }
                ]
            },
            Take = 1000
        };

        var stopwatch = Stopwatch.StartNew();
        var results = AdvancedCatalogSearchService.Search(index, query);
        stopwatch.Stop();

        output.WriteLine($"Advanced search baseline: {results.Count} matches over {count} entries in {stopwatch.ElapsedMilliseconds} ms.");
        Assert.NotEmpty(results);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Baseline query took {stopwatch.Elapsed}.");
    }

    private static string GetExtension(string name)
    {
        var extension = System.IO.Path.GetExtension(name ?? string.Empty);
        return extension.Length <= 1 ? string.Empty : extension.Substring(1).ToLowerInvariant();
    }
}
