using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using Xunit;

namespace SkyCD.Plugin.FileMetadata.Tests;

/// <summary>
/// The OS-assigned metadata reader publishes what a user typed into Explorer, Dolphin or Finder. The
/// platform tests write the same attributes with the same APIs the file managers use, so the round trip is
/// real; on other systems the generic tests still cover the contract.
/// </summary>
public sealed class OsMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsEverySourceAndSortsAfterEmbeddedProviders()
    {
        var descriptor = new OsMetadataPlugin().Descriptor;

        Assert.Equal(["*"], descriptor.Extensions);
        Assert.Equal(FileMetadataSchema.OsSchemaId, descriptor.Schema.Id);
        Assert.Equal(MediaMetadataFieldRole.Tags,
            descriptor.Schema.Fields.Single(field => field.Key == FileMetadataSchema.Tags).Role);
        Assert.True(descriptor.Priority < 10, "OS values must yield to an embedded value for the same field.");
    }

    [Fact]
    public async Task SourceWithoutReadableProperties_YieldsNoValuesInsteadOfFailing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"skycd-missing-{Guid.NewGuid():N}.bin");

        var result = await ReadAsync(missing);

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task FileWithoutAssignedProperties_YieldsNoValues()
    {
        using var file = new TemporaryFile(".txt");

        var result = await ReadAsync(file.Path);

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task Progress_ReachesCompletion()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"skycd-missing-{Guid.NewGuid():N}.bin");
        var progress = new RecordingProgress();

        await new OsMetadataPlugin().ExtractAsync(
            new LocalMediaContext(missing, ".bin", null, 0), progress, CancellationToken.None);

        Assert.Equal(new[] { 1d }, progress.Fractions);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OsMetadataPlugin().ExtractAsync(
            new LocalMediaContext(Path.GetTempPath(), ".bin", null, 0), null, cancellation.Token));
    }

    [Fact]
    public async Task NullContext_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new OsMetadataPlugin().ExtractAsync(null!, null, CancellationToken.None));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Windows_ScalarStringIsDecodedAndCleared()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var variant = new WindowsPropertyReader.PropVariant
        {
            VariantType = VtLpwstr,
            Pointer = Marshal.StringToCoTaskMemUni("Comment typed in Explorer")
        };

        Assert.Equal("Comment typed in Explorer", WindowsPropertyReader.ReadString(ref variant));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Windows_KeywordVectorIsDecoded()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pointer = Marshal.AllocCoTaskMem(IntPtr.Size * 2);
        Marshal.WriteIntPtr(pointer, 0, Marshal.StringToCoTaskMemUni("holiday"));
        Marshal.WriteIntPtr(pointer, IntPtr.Size, Marshal.StringToCoTaskMemUni("2024"));
        var variant = new WindowsPropertyReader.PropVariant
        {
            VariantType = VtVector | VtLpwstr,
            StringArray = new WindowsPropertyReader.CapStringArray { Count = 2, Values = pointer }
        };

        Assert.Equal(["holiday", "2024"], WindowsPropertyReader.ReadStrings(ref variant));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Windows_RatingVariantsAreClampedToValidStars()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var rating = new WindowsPropertyReader.PropVariant { VariantType = VtUi4, UInt32 = 75 };
        Assert.Equal(75, WindowsPropertyReader.ReadInteger(ref rating));

        var rejected = new WindowsPropertyReader.PropVariant { VariantType = VtI4, Int32 = -1 };
        Assert.Null(WindowsPropertyReader.ReadInteger(ref rejected));

        var unsupported = new WindowsPropertyReader.PropVariant { VariantType = VtLpwstr };
        Assert.Null(WindowsPropertyReader.ReadInteger(ref unsupported));
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public async Task Linux_XdgExtendedAttributesRoundTrip()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        using var file = new TemporaryFile(".jpg");
        Assert.Equal(0, LinuxXattrs.Set(file.Path, "user.xdg.tags", Encoding.UTF8.GetBytes("sunset, beach")));
        Assert.Equal(0, LinuxXattrs.Set(file.Path, "user.xdg.comment", Encoding.UTF8.GetBytes("From Dolphin")));

        var result = await ReadAsync(file.Path);

        Assert.Equal("sunset; beach", Text(result, FileMetadataSchema.Tags));
        Assert.Equal("From Dolphin", Text(result, FileMetadataSchema.Comment));
    }

    [Fact]
    [SupportedOSPlatform("macos")]
    public async Task MacOS_FinderAttributesRoundTrip()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using var file = new TemporaryFile(".jpg");
        var tags = BinaryPlistFixtures.ArrayOfStrings("Red\n6", "Work\n4");
        var comment = BinaryPlistFixtures.SingleString("Note from Finder");
        Assert.Equal(0, MacXattrs.Set(file.Path, "com.apple.metadata:_kMDItemUserTags", tags));
        Assert.Equal(0, MacXattrs.Set(file.Path, "com.apple.metadata:kMDItemFinderComment", comment));

        var result = await ReadAsync(file.Path);

        Assert.Equal("Red; Work", Text(result, FileMetadataSchema.Tags));
        Assert.Equal("Note from Finder", Text(result, FileMetadataSchema.Comment));
    }

    private const ushort VtI4 = 3;
    private const ushort VtUi4 = 19;
    private const ushort VtLpwstr = 31;
    private const ushort VtVector = 0x1000;

    private static Task<MediaExtractionResult> ReadAsync(string path) =>
        new OsMetadataPlugin().ExtractAsync(new LocalMediaContext(path, Path.GetExtension(path), null, 0), null,
            CancellationToken.None);

    private static string? Text(MediaExtractionResult result, string key) =>
        result.Values.SingleOrDefault(value => value.Key == key)?.Text;

    private sealed class RecordingProgress : IProgress<MediaProgress>
    {
        public System.Collections.Generic.List<double> Fractions { get; } = [];

        public void Report(MediaProgress value) => Fractions.Add(value.Fraction);
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string extension)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"skycd-os-metadata-{Guid.NewGuid():N}" + extension);
            File.WriteAllBytes(Path, [1, 2, 3]);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // A still-open property store may hold the file briefly; the temp directory is disposable.
            }
        }
    }

    [SupportedOSPlatform("linux")]
    private static class LinuxXattrs
    {
        public static int Set(string path, string name, byte[] value) =>
            SetXattr(path, name, value, (nuint)value.Length, 0);

        [DllImport("libc", SetLastError = true, EntryPoint = "setxattr")]
        private static extern int SetXattr([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte[] value, nuint size, int flags);
    }

    [SupportedOSPlatform("macos")]
    private static class MacXattrs
    {
        public static int Set(string path, string name, byte[] value) =>
            SetXattr(path, name, value, (nuint)value.Length, 0, 0);

        [DllImport("libc", SetLastError = true, EntryPoint = "setxattr")]
        private static extern int SetXattr([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte[] value, nuint size, uint position,
            int options);
    }
}
