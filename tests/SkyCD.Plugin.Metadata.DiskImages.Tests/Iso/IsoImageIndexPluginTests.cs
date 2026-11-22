using System;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.DiskImages;
using Xunit;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

public sealed class IsoImageIndexPluginTests
{
    internal static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Iso", fileName);
}
