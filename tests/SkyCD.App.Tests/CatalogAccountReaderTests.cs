using System;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Accounts;
using SkyCD.SDK.Capabilities.Accounts;
using Xunit;

namespace SkyCD.App.Tests;

public class CatalogAccountReaderTests
{
    private static AccountSessionStatus ConnectedStatus() => new()
    {
        ProviderId = "jetbrains",
        DisplayName = "JetBrains",
        AccountId = "default",
        AccountEmail = "dev@example.com",
        IsConnected = true,
        LastSyncedUtc = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero),
        Products =
        [
            new AccountOwnedProduct { Id = "L1", Product = "IntelliJ IDEA Ultimate", Status = "active" },
        ],
    };

    private static CatalogDocument FolderDocument(
        string id,
        string providerId,
        string accountKey,
        string? email = "dev@example.com",
        bool connected = true) => new()
        {
            Id = id,
            Name = "JetBrains — dev@example.com",
            ParentId = "parent-1",
            Type = CatalogDocumentType.Folder,
            Size = 0,
            ChildrenCount = 0,
            Properties = new PropertiesCollection
            {
                [CatalogDocument.SizeKnownPropertyKey] = false,
                [CatalogAccountReader.ProviderKey] = providerId,
                [CatalogAccountReader.KeyKey] = accountKey,
                [CatalogAccountReader.EmailKey] = email ?? string.Empty,
                [CatalogAccountReader.DisplayNameKey] = "JetBrains",
                [CatalogAccountReader.ConnectedKey] = connected,
            },
        };

    [Fact]
    public void BuildDocuments_WritesTokenFreeConnectionMetadata()
    {
        var documents = AccountCatalogBuilder.BuildDocuments(AccountCatalogBuilder.BuildPlan(ConnectedStatus()), "parent-1");

        var folder = Assert.Single(documents, document => document.Type == CatalogDocumentType.Folder);
        Assert.Equal("jetbrains", folder.Properties[CatalogAccountReader.ProviderKey]);
        Assert.Equal("default", folder.Properties[CatalogAccountReader.KeyKey]);
        Assert.Equal("dev@example.com", folder.Properties[CatalogAccountReader.EmailKey]);
        Assert.Equal("JetBrains", folder.Properties[CatalogAccountReader.DisplayNameKey]);
        Assert.Equal(true, folder.Properties[CatalogAccountReader.ConnectedKey]);
        Assert.NotNull(folder.Properties[CatalogAccountReader.LastSyncKey]);

        Assert.All(documents, document => Assert.False(
            CatalogAccountReader.ContainsCredentialMaterial(document),
            "Catalog documents must never carry token material."));
    }

    [Fact]
    public void ReadAccountFolders_RoundTripsBuilderOutput()
    {
        var documents = AccountCatalogBuilder.BuildDocuments(AccountCatalogBuilder.BuildPlan(ConnectedStatus()), "parent-1");

        var folders = CatalogAccountReader.ReadAccountFolders(documents);

        var folder = Assert.Single(folders);
        Assert.Equal("account-folder-jetbrains-default", folder.FolderId);
        Assert.Equal("jetbrains", folder.ProviderId);
        Assert.Equal("default", folder.AccountKey);
        Assert.Equal("dev@example.com", folder.AccountEmail);
        Assert.Equal("JetBrains", folder.DisplayName);
        Assert.True(folder.IsConnected);
        Assert.NotNull(folder.LastSyncedUtc);
    }

    [Fact]
    public void ReadAccountFolders_IgnoresNonAccountFolders()
    {
        var plain = new CatalogDocument
        {
            Id = "plain-1",
            Name = "Plain folder",
            Type = CatalogDocumentType.Folder,
            Size = 0,
            ChildrenCount = 0,
            Properties = new PropertiesCollection
            {
                [CatalogDocument.SizeKnownPropertyKey] = false,
            },
        };

        Assert.Empty(CatalogAccountReader.ReadAccountFolders([plain]));
        Assert.Empty(CatalogAccountReader.ReadAccountFolders(null));
    }

    [Fact]
    public void ContainsCredentialMaterial_DetectsPlantedTokenKeys()
    {
        var folder = FolderDocument("folder-1", "jetbrains", "default");
        folder.Properties["AccessToken"] = "eyJhbGciOiJSUzI1NiJ9.supersecretpayload";

        Assert.True(CatalogAccountReader.ContainsCredentialMaterial(folder));
    }

    [Fact]
    public void SecondCatalogWithoutFolders_StartsEmpty()
    {
        var first = AccountCatalogBuilder.BuildDocuments(AccountCatalogBuilder.BuildPlan(ConnectedStatus()), "parent-1");

        Assert.Single(CatalogAccountReader.ReadAccountFolders(first));
        Assert.Empty(CatalogAccountReader.ReadAccountFolders([]));
    }
}
