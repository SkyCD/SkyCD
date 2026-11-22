using System;
using System.Collections.Generic;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class ContributedAdvancedSearchTests
{
    private static readonly ContributedSearchFieldDefinition Loaned =
        new("plugin.loaned", "Loaned", static document => document.Properties.ContainsKey("Borrower"),
            static document => document.Properties.TryGetValue("Borrower", out var value) ? value?.ToString() : null);

    private static readonly ContributedSearchFieldDefinition Returned =
        new("plugin.returned", "Returned", static document => document.Properties.ContainsKey("ReturnedAt"));

    private static readonly CatalogSearchIndex Index = CreateIndex();

    [Fact]
    public void Presence_field_matches_present_and_missing_entries()
    {
        Assert.Equal(["returned"], Ids(Presence(Returned.Key, AdvancedCatalogOperator.IsPresent)));
        Assert.Equal(["active"], Ids(Presence(Returned.Key, AdvancedCatalogOperator.IsMissing)));
    }

    [Fact]
    public void Value_field_applies_text_operators_to_the_selected_value()
    {
        Assert.Equal(["active"], Ids(Text(Loaned.Key, AdvancedCatalogOperator.Equals, "Alice")));
        Assert.Equal(["returned"], Ids(Text(Loaned.Key, AdvancedCatalogOperator.Equals, "Bob")));
        Assert.Equal(["returned"], Ids(Text(Loaned.Key, AdvancedCatalogOperator.EndsWith, "ob")));
    }

    [Fact]
    public void Unknown_contribution_matches_only_missing()
    {
        Assert.Empty(Ids(Presence("plugin.unknown", AdvancedCatalogOperator.IsPresent)));
        Assert.Equal(["active", "returned"], Ids(Presence("plugin.unknown", AdvancedCatalogOperator.IsMissing)));
    }

    private static IEnumerable<string> Ids(IReadOnlyList<CatalogFindResult> results)
    {
        foreach (var result in results)
        {
            yield return result.Document.Id;
        }
    }

    private static IReadOnlyList<CatalogFindResult> Presence(string key, AdvancedCatalogOperator op) =>
        AdvancedCatalogSearchService.Search(
            Index,
            Query(new AdvancedCatalogPredicate { Field = AdvancedCatalogField.Contributed, Operator = op, Key = key }),
            contributedFields: [Loaned, Returned]);

    private static IReadOnlyList<CatalogFindResult> Text(string key, AdvancedCatalogOperator op, string value) =>
        AdvancedCatalogSearchService.Search(
            Index,
            Query(new AdvancedCatalogPredicate
            {
                Field = AdvancedCatalogField.Contributed,
                Operator = op,
                Key = key,
                Value = value
            }),
            contributedFields: [Loaned, Returned]);

    private static AdvancedCatalogQuery Query(AdvancedCatalogPredicate predicate) => new()
    {
        Root = new CatalogQueryGroup { Predicates = [predicate] }
    };

    private static CatalogSearchIndex CreateIndex()
    {
        var index = new CatalogSearchIndex();
        index.Rebuild(
        [
            new CatalogDocument
            {
                Id = "active",
                Name = "Active item",
                Type = CatalogDocumentType.File,
                Properties = new PropertiesCollection { ["Borrower"] = "Alice" }
            },
            new CatalogDocument
            {
                Id = "returned",
                Name = "Returned item",
                Type = CatalogDocumentType.File,
                Properties = new PropertiesCollection
                {
                    ["Borrower"] = "Bob",
                    ["ReturnedAt"] = "2024-05-01"
                }
            }
        ]);
        return index;
    }
}
