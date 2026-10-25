using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DBus.Services.Secrets;
using SkyCD.Plugin.Abstractions.OAuth;

namespace SkyCD.Core.OAuth;

/// <summary>Namespace keys in an unlocked, persistent Secret Service default collection.</summary>
internal sealed class LinuxOAuthKeyStore : IOAuthKeyStore
{
    // This client owns a D-Bus session for the host process lifetime, never a cached storage key.
    private Task<SecretService>? _service;

    public byte[]? Read(string keyId) => Run(async collection =>
    {
        var items = await collection.SearchItemsAsync(Attributes(keyId));
        if (items.Length == 0)
            return null;
        if (items.Length != 1 || await items[0].IsLockedAsync())
            throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
        return await items[0].GetSecretAsync();
    });

    public void Create(string keyId, byte[] key) => Run<object?>(async collection =>
    {
        var existing = await collection.SearchItemsAsync(Attributes(keyId));
        if (existing.Length != 0)
            throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
        if (await collection.CreateItemAsync("SkyCD OAuth encryption key", Attributes(keyId), key,
                "application/octet-stream", false) is null)
            throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
        return null;
    });

    internal void Delete(string keyId) => Run<object?>(async collection =>
    {
        foreach (var item in await collection.SearchItemsAsync(Attributes(keyId)))
            await item.DeleteAsync();
        return null;
    });

    private T Run<T>(Func<Collection, Task<T>> action)
    {
        if (!OperatingSystem.IsLinux())
            throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
        try
        {
            // The protector is synchronous; never capture an Avalonia synchronization context.
            return Task.Run(async () =>
            {
                _service ??= SecretService.ConnectAsync(EncryptionType.Dh);
                var service = await _service;
                var collection = await service.GetDefaultCollectionAsync();
                if (collection is null || await collection.IsLockedAsync())
                    throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
                var sessionCollection = await service.GetCollectionByAliasAsync("session");
                if (sessionCollection is not null && collection.CollectionPath == sessionCollection.CollectionPath)
                    throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
                return await action(collection);
            }).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Failed connections may be retried after the desktop/keyring becomes available.
            if (_service?.IsFaulted == true)
                _service = null;
            throw new OAuthException(OAuthFailure.SecureStorageUnavailable);
        }
    }

    private static Dictionary<string, string> Attributes(string keyId) => new()
    {
        ["application"] = "org.skycd.oauth.keys.v1",
        ["namespace"] = keyId
    };
}
