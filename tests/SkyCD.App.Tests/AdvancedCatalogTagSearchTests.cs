using System.Collections.Generic;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class AdvancedCatalogTagSearchTests
{
    private static CatalogDocument Entry(string id, params string[] tagIds) => new()
    {
        Id = id,
        Name = id,
        Type = CatalogDocumentType.File,
        Properties = new PropertiesCollection()
        {
            ["Tags"] = new List<string>(tagIds)
        }
    };

    private static CatalogDocument Untagged(string id) => new()
    {
        Id = id,
        Name = id,
        Type = CatalogDocumentType.File,
        Properties = new PropertiesCollection()
    };

    private static CatalogSearchIndex IndexOf(params CatalogDocument[] entries)
    {
        var index = new CatalogSearchIndex();
        index.Rebuild(entries);
        return index;
    }

    private static AdvancedCatalogQuery TagQuery(AdvancedCatalogOperator op, string? value = "t1") =>
        new()
        {
            Root = new CatalogQueryGroup
            {
                Combination = CatalogQueryCombination.All,
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.CustomProperty,
                        Operator = op,
                        Key = "Tags",
                        Value = value
                    }
                ]
            }
        };

    private static IReadOnlyList<string> Ids(IReadOnlyList<CatalogFindResult> results)
    {
        var ids = new List<string>();
        foreach (var result in results)
        {
            ids.Add(result.Document.Id);
        }

        return ids;
    }

    [Fact]
    public void Contains_MatchesEntriesCarryingTheTag()
    {
        var index = IndexOf(Entry("a", "t1", "t2"), Entry("b", "t2"), Untagged("c"));

        var results = AdvancedCatalogSearchService.Search(index, TagQuery(AdvancedCatalogOperator.Contains));

        Assert.Equal(["a"], Ids(results));
    }

    [Fact]
    public void OneOf_MatchesEntriesCarryingAnyTag()
    {
        var index = IndexOf(Entry("a", "t1"), Entry("b", "t2"), Entry("c", "t1", "t2"), Untagged("d"));
        var query = new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Combination = CatalogQueryCombination.All,
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.CustomProperty,
                        Operator = AdvancedCatalogOperator.OneOf,
                        Key = "Tags",
                        Values = ["t1", "t3"]
                    }
                ]
            }
        };

        var results = AdvancedCatalogSearchService.Search(index, query);

        Assert.Equal(["a", "c"], Ids(results));
    }

    [Fact]
    public void IsPresent_MatchesOnlyTaggedEntries()
    {
        var index = IndexOf(Entry("a", "t1"), Untagged("b"));

        var results = AdvancedCatalogSearchService.Search(index, TagQuery(AdvancedCatalogOperator.IsPresent));

        Assert.Equal(["a"], Ids(results));
    }

    [Fact]
    public void IsMissing_MatchesUntaggedAndEmptyListEntries()
    {
        var index = IndexOf(Entry("a", "t1"), Untagged("b"), Entry("c"));

        var results = AdvancedCatalogSearchService.Search(index, TagQuery(AdvancedCatalogOperator.IsMissing, value: null));

        Assert.Equal(["b", "c"], Ids(results));
    }

    [Fact]
    public void NotEquals_ExcludesEntriesCarryingTheTag()
    {
        var index = IndexOf(Entry("a", "t1", "t2"), Entry("b", "t2"), Untagged("c"));

        var results = AdvancedCatalogSearchService.Search(index, TagQuery(AdvancedCatalogOperator.NotEquals));

        Assert.Equal(["b", "c"], Ids(results));
    }

    [Fact]
    public void Equals_MatchesWhenAnyElementEquals()
    {
        var index = IndexOf(Entry("a", "t1", "t2"), Entry("b", "t2"));

        var results = AdvancedCatalogSearchService.Search(index, TagQuery(AdvancedCatalogOperator.Equals));

        Assert.Equal(["a"], Ids(results));
    }
}
