using System;
using System.Collections.Generic;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteCatalogProviderRegistry
{
    private readonly Dictionary<string, IRemoteCatalogConnector> providers = new(StringComparer.OrdinalIgnoreCase);

    public static RemoteCatalogProviderRegistry Default { get; } = new([new FtpCatalogConnector(), new SftpCatalogConnector()]);

    public RemoteCatalogProviderRegistry(IEnumerable<IRemoteCatalogConnector> connectors)
    {
        foreach (var connector in connectors)
        {
            providers.Add(connector.Scheme, connector);
        }
    }

    public IRemoteCatalogConnector Get(string scheme) =>
        providers.TryGetValue(scheme, out var provider)
            ? provider
            : throw new RemoteSchemeNotSupportedException(scheme);
}
