using System.Linq;
using SkyCD.Documents.Collections;
using Xunit;

namespace SkyCD.App.Tests;

public class PropertiesCollectionTests
{
    [Fact]
    public void Enumeration_IsSortedByKey_CaseInsensitive()
    {
        var properties = new PropertiesCollection
        {
            ["zeta"] = 1,
            ["Alpha"] = 2,
            ["beta"] = 3
        };

        var keys = properties.Keys.ToArray();

        Assert.Equal(["Alpha", "beta", "zeta"], keys);
    }
}