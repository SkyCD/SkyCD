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

    private static List<CatalogDocument> CreateDuplicateCatalog()
    {
        return
        [
            new() { Id = "disc-a", Name = "Disc A", Type = CatalogDocumentType.Media },
            new() { Id = "folder-a", ParentId = "disc-a", Name = "Docs", Type = CatalogDocumentType.Folder },
            new() { Id = "dup-1", ParentId = "folder-a", Name = "report.pdf", Type = CatalogDocumentType.File, Size = 1024,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "aabbccdd00112233445566778899aabbccddeeff00112233445566778899aabb" } },
            new() { Id = "dup-2", ParentId = "folder-a", Name = "report-copy.pdf", Type = CatalogDocumentType.File, Size = 1024,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "AABBCCDD00112233445566778899AABBCCDDEEFF00112233445566778899AABB" } },
            new() { Id = "unique-1", ParentId = "folder-a", Name = "notes.txt", Type = CatalogDocumentType.File, Size = 512,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "1122334455667788990011223344556677889900112233445566778899001122" } },
            new() { Id = "no-hash", ParentId = "folder-a", Name = "readme.md", Type = CatalogDocumentType.File, Size = 256 },
            new() { Id = "diff-size", ParentId = "folder-a", Name = "report-v2.pdf", Type = CatalogDocumentType.File, Size = 2048,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "aabbccdd00112233445566778899aabbccddeeff00112233445566778899aabb" } },
        ];
    }

    [Fact]
    public void Search_Duplicates_Sha256_ReturnsOnlyMatchingGroups()
    {
        var index = CreateIndex(CreateDuplicateCatalog());
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Sha256,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Equal(2, results.Count);
        var ids = results.Select(r => r.Document.Id).OrderBy(id => id).ToArray();
        Assert.Equal(["dup-1", "dup-2"], ids);
    }

    [Fact]
    public void Search_Uniques_Sha256_ReturnsOnlySingletons()
    {
        var index = CreateIndex(CreateDuplicateCatalog());
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Uniques,
            HashAlgorithm = ComparisonAlgorithm.Sha256,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Equal(2, results.Count);
        var ids = results.Select(r => r.Document.Id).OrderBy(id => id).ToArray();
        Assert.Equal(["diff-size", "unique-1"], ids);
    }

    [Fact]
    public void Search_NoDuplicateFilter_ReturnsAllFiles()
    {
        var index = CreateIndex(CreateDuplicateCatalog());
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.None,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        // All 7 entries in scope (media + folder + 5 files)
        Assert.Equal(7, results.Count);
    }

    [Fact]
    public void Search_Duplicates_ExcludesFilesWithoutHash()
    {
        var index = CreateIndex(CreateDuplicateCatalog());
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Sha256,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.DoesNotContain(results, r => r.Document.Id == "no-hash");
    }

    [Fact]
    public void Search_Duplicates_AppliedBeforePredicates()
    {
        var index = CreateIndex(CreateDuplicateCatalog());
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Sha256,
            Root = new CatalogQueryGroup
            {
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.Name,
                        Operator = AdvancedCatalogOperator.Contains,
                        Value = "report.pdf"
                    }
                ]
            },
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Single(results);
        Assert.Equal("dup-1", results[0].Document.Id);
    }

    [Fact]
    public void Search_Duplicates_ScopeLimiting()
    {
        var docs = new List<CatalogDocument>
        {
            new() { Id = "disc-a", Name = "Disc A", Type = CatalogDocumentType.Media },
            new() { Id = "folder-a", ParentId = "disc-a", Name = "Docs", Type = CatalogDocumentType.Folder },
            new() { Id = "dup-a1", ParentId = "folder-a", Name = "file1.bin", Type = CatalogDocumentType.File, Size = 100,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } },
            new() { Id = "dup-a2", ParentId = "folder-a", Name = "file2.bin", Type = CatalogDocumentType.File, Size = 100,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } },
            new() { Id = "disc-b", Name = "Disc B", Type = CatalogDocumentType.Media },
            new() { Id = "dup-b1", ParentId = "disc-b", Name = "file3.bin", Type = CatalogDocumentType.File, Size = 100,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } },
        };

        var index = CreateIndex(docs);
        var query = new AdvancedCatalogQuery
        {
            Scope = new CatalogQueryScope { RootId = "disc-a", IncludeDescendants = true },
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Sha256,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Equal(2, results.Count);
        var ids = results.Select(r => r.Document.Id).OrderBy(id => id).ToArray();
        Assert.Equal(["dup-a1", "dup-a2"], ids);
    }

    [Fact]
    public void Search_Duplicates_FoldersAndMediaNotClassified()
    {
        var docs = new List<CatalogDocument>
        {
            new() { Id = "media", Name = "Disc", Type = CatalogDocumentType.Media, Size = 100 },
            new() { Id = "folder", ParentId = "media", Name = "Folder", Type = CatalogDocumentType.Folder, Size = 100 },
            new() { Id = "file", ParentId = "folder", Name = "f.bin", Type = CatalogDocumentType.File, Size = 100,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } },
        };

        var index = CreateIndex(docs);
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Uniques,
            HashAlgorithm = ComparisonAlgorithm.Sha256,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Single(results);
        Assert.Equal("file", results[0].Document.Id);
    }

    [Fact]
    public void Search_Duplicates_ZeroByteFiles()
    {
        var docs = new List<CatalogDocument>
        {
            new() { Id = "empty1", Name = "a.txt", Type = CatalogDocumentType.File, Size = 0,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" } },
            new() { Id = "empty2", Name = "b.txt", Type = CatalogDocumentType.File, Size = 0,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" } },
        };

        var index = CreateIndex(docs);
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Sha256,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Search_Validate_ThrowsWhenDuplicateFilterWithoutAlgorithm()
    {
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.None,
            Take = 200
        };

        Assert.Throws<AdvancedCatalogQueryException>(() => AdvancedCatalogSearchService.Validate(query));
    }

    [Fact]
    public void Search_Duplicates_Crc32_HexFormat()
    {
        var docs = new List<CatalogDocument>
        {
            new() { Id = "f1", Name = "a.bin", Type = CatalogDocumentType.File, Size = 512,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["Crc32"] = "DEADBEEF" } },
            new() { Id = "f2", Name = "b.bin", Type = CatalogDocumentType.File, Size = 512,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["CRC32"] = "deadbeef" } },
            new() { Id = "f3", Name = "c.bin", Type = CatalogDocumentType.File, Size = 512,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["Crc32"] = "CAFEBABE" } },
        };

        var index = CreateIndex(docs);
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Crc32,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Equal(2, results.Count);
        var ids = results.Select(r => r.Document.Id).OrderBy(id => id).ToArray();
        Assert.Equal(["f1", "f2"], ids);
    }

    [Fact]
    public void Search_Duplicates_Crc32_DecimalFormat()
    {
        var docs = new List<CatalogDocument>
        {
            new() { Id = "f1", Name = "a.bin", Type = CatalogDocumentType.File, Size = 512,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["Crc32"] = "3735928559" } },
            new() { Id = "f2", Name = "b.bin", Type = CatalogDocumentType.File, Size = 512,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["Crc32"] = "DEADBEEF" } },
        };

        var index = CreateIndex(docs);
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Crc32,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Search_Duplicates_Crc32_Ambiguous8DigitExcluded()
    {
        var docs = new List<CatalogDocument>
        {
            new() { Id = "f1", Name = "a.bin", Type = CatalogDocumentType.File, Size = 512,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["Crc32"] = "12345678" } },
            new() { Id = "f2", Name = "b.bin", Type = CatalogDocumentType.File, Size = 512,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["Crc32"] = "12345678" } },
        };

        var index = CreateIndex(docs);
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Crc32,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_Duplicates_Crc32_ZeroPrefixedHex()
    {
        var docs = new List<CatalogDocument>
        {
            new() { Id = "f1", Name = "a.bin", Type = CatalogDocumentType.File, Size = 256,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["Crc32"] = "0x0A1B2C3D" } },
            new() { Id = "f2", Name = "b.bin", Type = CatalogDocumentType.File, Size = 256,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["Crc32"] = "0a1b2c3d" } },
        };

        var index = CreateIndex(docs);
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Crc32,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Search_Duplicates_WithMetadataLookup()
    {
        var docs = new List<CatalogDocument>
        {
            new() { Id = "f1", Name = "a.bin", Type = CatalogDocumentType.File, Size = 100 },
            new() { Id = "f2", Name = "b.bin", Type = CatalogDocumentType.File, Size = 100 },
        };

        var index = CreateIndex(docs);
        var sha = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        AdvancedCatalogSearchService.MetadataLookup lookup = doc =>
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["sha256"] = sha };

        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Sha256,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query, lookup);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Search_Duplicates_InvalidSha256Length_Excluded()
    {
        var docs = new List<CatalogDocument>
        {
            new() { Id = "f1", Name = "a.bin", Type = CatalogDocumentType.File, Size = 100,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "tooshort" } },
            new() { Id = "f2", Name = "b.bin", Type = CatalogDocumentType.File, Size = 100,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "tooshort" } },
        };

        var index = CreateIndex(docs);
        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Sha256,
            Take = 1000
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Empty(results);
    }

    [Fact]
    public void Search_Uniques_ScopeRelative()
    {
        var docs = new List<CatalogDocument>
        {
            new() { Id = "disc-a", Name = "Disc A", Type = CatalogDocumentType.Media },
            new() { Id = "fa", ParentId = "disc-a", Name = "x.bin", Type = CatalogDocumentType.File, Size = 10,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd" } },
            new() { Id = "disc-b", Name = "Disc B", Type = CatalogDocumentType.Media },
            new() { Id = "fb", ParentId = "disc-b", Name = "x.bin", Type = CatalogDocumentType.File, Size = 10,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection { ["sha256"] = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd" } },
        };

        var index = CreateIndex(docs);

        var dupQuery = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Sha256,
            Take = 1000
        };
        var dupResults = AdvancedCatalogSearchService.Search(index, dupQuery);
        Assert.Equal(2, dupResults.Count);

        var uniqueQuery = new AdvancedCatalogQuery
        {
            Scope = new CatalogQueryScope { RootId = "disc-a", IncludeDescendants = true },
            DuplicateFilter = DuplicateFilterMode.Uniques,
            HashAlgorithm = ComparisonAlgorithm.Sha256,
            Take = 1000
        };
        var uniqueResults = AdvancedCatalogSearchService.Search(index, uniqueQuery);
        Assert.Single(uniqueResults);
        Assert.Equal("fa", uniqueResults[0].Document.Id);
    }

    [Fact]
    public void Search_Duplicates_Cancellation()
    {
        var docs = new List<CatalogDocument>();
        for (var i = 0; i < 500; i++)
        {
            docs.Add(new CatalogDocument
            {
                Id = $"f{i}",
                Name = $"file{i}.bin",
                Type = CatalogDocumentType.File,
                Size = 100,
                Properties = new SkyCD.Documents.Collections.PropertiesCollection
                {
                    ["sha256"] = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"
                }
            });
        }

        var index = CreateIndex(docs);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var query = new AdvancedCatalogQuery
        {
            DuplicateFilter = DuplicateFilterMode.Duplicates,
            HashAlgorithm = ComparisonAlgorithm.Sha256,
            Take = 1000
        };

        Assert.Throws<OperationCanceledException>(() =>
            AdvancedCatalogSearchService.Search(index, query, null, cts.Token));
    }
}
