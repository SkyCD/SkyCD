using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Accounts;
using SkyCD.SDK.Capabilities.Accounts;
using Xunit;

namespace SkyCD.App.Tests;

public class AccountCatalogBuilderTests
{
    private static AccountSessionStatus CreateStatus(params AccountOwnedProduct[] products) => new()
    {
        ProviderId = "jetbrains",
        DisplayName = "JetBrains",
        AccountId = "default",
        AccountEmail = "dev@example.com",
        IsConnected = true,
        LastSyncedUtc = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero),
        Products = products
    };

    private static AccountOwnedProduct CreateProduct(
        string id,
        string product,
        string? assignee = null,
        DateTimeOffset? expires = null,
        int seats = 1,
        string status = "active",
        string? executable = null) => new()
        {
            Id = id,
            Product = product,
            ProductCode = "II",
            LicenseType = "subscription",
            Seats = seats,
            ExpiresUtc = expires,
            AssigneeEmail = assignee,
            Status = status,
            ExecutablePath = executable
        };

    [Fact]
    public void BuildPlan_UsesStableFolderAndEntryIds()
    {
        var status = CreateStatus(
            CreateProduct("L1", "IntelliJ IDEA Ultimate"),
            CreateProduct("L2", "PhpStorm"));

        var first = AccountCatalogBuilder.BuildPlan(status);
        var second = AccountCatalogBuilder.BuildPlan(status);

        Assert.Equal("account-folder-jetbrains-default", first.FolderId);
        Assert.Equal(first.FolderId, second.FolderId);
        Assert.Contains("JetBrains", first.FolderName, StringComparison.Ordinal);
        Assert.Contains("dev@example.com", first.FolderName, StringComparison.Ordinal);
        Assert.Equal(first.Entries.Select(entry => entry.Id), second.Entries.Select(entry => entry.Id));
        Assert.Equal(["account-jetbrains-L1", "account-jetbrains-L2"], first.Entries.Select(entry => entry.Id));
    }

    [Fact]
    public void BuildPlan_OrdersEntriesByProductName()
    {
        var plan = AccountCatalogBuilder.BuildPlan(CreateStatus(
            CreateProduct("L1", "WebStorm"),
            CreateProduct("L2", "Aqua"),
            CreateProduct("L3", "PhpStorm")));

        Assert.Equal(["Aqua", "PhpStorm", "WebStorm"], plan.Entries.Select(entry => entry.Product));
    }

    [Fact]
    public void BuildPlan_QualifiesCollidingProductNames()
    {
        var plan = AccountCatalogBuilder.BuildPlan(CreateStatus(
            CreateProduct("L1", "IntelliJ IDEA Ultimate", assignee: "one@example.com"),
            CreateProduct("L2", "IntelliJ IDEA Ultimate", assignee: "two@example.com"),
            CreateProduct("L3", "IntelliJ IDEA Ultimate", assignee: "two@example.com",
                expires: new DateTimeOffset(2027, 4, 12, 0, 0, 0, TimeSpan.Zero))));

        Assert.Equal(3, plan.Entries.Select(entry => entry.Name).Distinct().Count());
        Assert.Contains(plan.Entries, entry =>
            entry.Name.Contains("one@example.com", StringComparison.Ordinal));
        Assert.Contains(plan.Entries, entry =>
            entry.Name.Contains("2027-04-12", StringComparison.Ordinal));
        Assert.Contains(plan.Entries, entry => entry.Name.Contains('(') && entry.Name.Contains(')'));
    }

    [Fact]
    public void BuildDocuments_EmitsAccountFolderWithFileEntries()
    {
        var plan = AccountCatalogBuilder.BuildPlan(CreateStatus(
            CreateProduct("L1", "IntelliJ IDEA Ultimate", seats: 3, assignee: "dev@example.com",
                executable: @"C:\IDEA\bin\idea64.exe"),
            CreateProduct("L2", "PhpStorm")));

        var documents = AccountCatalogBuilder.BuildDocuments(plan, "parent-1");

        var folder = Assert.Single(documents, document => document.Type == CatalogDocumentType.Folder);
        Assert.Equal("account-folder-jetbrains-default", folder.Id);
        Assert.Equal("parent-1", folder.ParentId);
        Assert.Equal("jetbrains", folder.Properties["AccountProvider"]);
        Assert.False((bool)folder.Properties[CatalogDocument.SizeKnownPropertyKey]!);

        var files = documents.Where(document => document.Type == CatalogDocumentType.File).ToList();
        Assert.Equal(2, files.Count);
        Assert.All(files, file =>
        {
            Assert.Equal(folder.Id, file.ParentId);
            Assert.Equal("jetbrains", file.Properties["AccountProvider"]);
            Assert.False(file.Properties.ContainsKey("LocationId"));
        });

        var idea = files.Single(file => file.Properties["AccountRecordId"] as string == "L1");
        Assert.Equal("IntelliJ IDEA Ultimate", idea.Properties["Product"]);
        Assert.Equal(3, idea.Properties["Seats"]);
        Assert.Equal("active", idea.Properties["LicenseStatus"]);
        Assert.Equal(@"C:\IDEA\bin\idea64.exe", idea.Properties["FullName"]);
        Assert.Equal("dev@example.com", idea.Properties["AssignedTo"]);

        var phpStorm = files.Single(file => file.Properties["AccountRecordId"] as string == "L2");
        Assert.False(phpStorm.Properties.ContainsKey("FullName"));
    }

    [Fact]
    public void Rebuilding_KeepsTheSameDocumentIds()
    {
        var status = CreateStatus(
            CreateProduct("L1", "IntelliJ IDEA Ultimate"),
            CreateProduct("L2", "PhpStorm"));

        var first = AccountCatalogBuilder.BuildDocuments(AccountCatalogBuilder.BuildPlan(status), "parent-1");
        var second = AccountCatalogBuilder.BuildDocuments(AccountCatalogBuilder.BuildPlan(status), "parent-1");

        Assert.Equal(
            first.Select(document => (document.Id, document.Name)),
            second.Select(document => (document.Id, document.Name)));
    }

    [Fact]
    public void BuildPlan_BuildsRecordIdForProductsWithoutALicenseId()
    {
        var plan = AccountCatalogBuilder.BuildPlan(CreateStatus(new AccountOwnedProduct
        {
            Id = string.Empty,
            Product = "ReSharper",
            Status = "active"
        }));

        var entry = Assert.Single(plan.Entries);
        Assert.False(string.IsNullOrWhiteSpace(entry.RecordId));
        Assert.StartsWith("account-jetbrains-", entry.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPlan_HandlesDisconnectedAccount()
    {
        var status = CreateStatus() with { IsConnected = false };

        var plan = AccountCatalogBuilder.BuildPlan(status);

        Assert.Empty(plan.Entries);
        Assert.Contains("dev@example.com", plan.FolderName, StringComparison.Ordinal);
    }
}
