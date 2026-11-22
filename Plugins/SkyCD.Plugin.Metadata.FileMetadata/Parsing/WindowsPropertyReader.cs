using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SkyCD.Plugin.MediaMetadata;
using SkyCD.Plugin.MediaMetadata.Parsing;

namespace SkyCD.Plugin.Metadata.FileMetadata.Parsing;

/// <summary>
/// Reads the values a user assigns in the Explorer Details tab through the Windows Property System. They
/// live in shell property handlers and NTFS alternate data streams rather than in the file bytes. Reads use
/// <c>GPS_BESTEFFORT</c>, so a file whose handler is missing simply yields no values; the store is opened
/// read-only and never written back.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsPropertyReader
{
    private const uint GpsDefault = 0x00000000;
    private const uint GpsBestEffort = 0x00000040;

    private const ushort VtI2 = 2;
    private const ushort VtI4 = 3;
    private const ushort VtBstr = 8;
    private const ushort VtUi4 = 19;
    private const ushort VtLpwstr = 31;
    private const ushort VtVector = 0x1000;

    private static readonly Guid PropertyStoreIid = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Guid SummaryInformation = new("F29F85E0-4FF9-1068-AB91-08002B27B3D9");
    private static readonly Guid ShellRating = new("64440492-4C8B-11D1-8B70-080036B11A03");

    private static readonly PropertyKey KeywordsKey = new(SummaryInformation, 5);
    private static readonly PropertyKey CommentKey = new(SummaryInformation, 6);
    private static readonly PropertyKey RatingKey = new(ShellRating, 9);

    public static void Read(string path, MetadataScan scan)
    {
        IPropertyStore? store = null;
        try
        {
            var iid = PropertyStoreIid;
            if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, GpsDefault | GpsBestEffort, ref iid,
                    out store) < 0 || store is null)
            {
                return;
            }

            if (TryGetValue(store, KeywordsKey, out var keywords))
            {
                foreach (var keyword in ReadStrings(ref keywords))
                {
                    scan.AddKeywords(keyword);
                }
            }

            if (TryGetValue(store, CommentKey, out var comment))
            {
                scan.SetComment(ReadString(ref comment));
            }

            if (TryGetValue(store, RatingKey, out var rating))
            {
                scan.SetRating(ReadInteger(ref rating));
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or NotSupportedException)
        {
            // A missing or failing property handler leaves the file without shell metadata.
        }
        finally
        {
            if (store is not null)
            {
                Marshal.ReleaseComObject(store);
            }
        }
    }

    private static bool TryGetValue(IPropertyStore store, PropertyKey key, out PropVariant value)
    {
        value = default;
        return store.GetValue(in key, out value) >= 0;
    }

    internal static IReadOnlyList<string> ReadStrings(ref PropVariant value)
    {
        try
        {
            if (value.VariantType == (VtVector | VtLpwstr))
            {
                var array = value.StringArray;
                var values = new List<string>((int)Math.Min(array.Count, MetadataScan.MaxTagCount));
                for (var index = 0; index < array.Count && values.Count < MetadataScan.MaxTagCount; index++)
                {
                    var pointer = Marshal.ReadIntPtr(array.Values, index * IntPtr.Size);
                    var text = Marshal.PtrToStringUni(pointer);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        values.Add(text);
                    }
                }

                return values;
            }

            var single = ReadScalarString(value);
            return single.Length > 0 ? [single] : [];
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    internal static string ReadString(ref PropVariant value)
    {
        try
        {
            return ReadScalarString(value);
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    internal static int? ReadInteger(ref PropVariant value)
    {
        try
        {
            return value.VariantType switch
            {
                VtUi4 => Clamp(value.UInt32),
                VtI4 => Clamp(value.Int32),
                VtI2 => Clamp(value.Short),
                _ => null
            };
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static int? Clamp(long value) => value is >= 0 and <= int.MaxValue ? (int)value : null;

    private static string ReadScalarString(in PropVariant value) => value.VariantType switch
    {
        VtLpwstr => Marshal.PtrToStringUni(value.Pointer) ?? string.Empty,
        VtBstr => Marshal.PtrToStringBSTR(value.Pointer) ?? string.Empty,
        _ => string.Empty
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetPropertyStoreFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        uint flags,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);

        [PreserveSig] int GetAt(uint index, out PropertyKey key);

        [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);

        [PreserveSig] int SetValue(in PropertyKey key, in PropVariant value);

        [PreserveSig] int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }

        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CapStringArray
    {
        public uint Count;
        public IntPtr Values;
    }

    /// <summary>Layout of PROPVARIANT: the type at offset 0 and the union at offset 8 on both bitnesses.</summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public IntPtr Pointer;
        [FieldOffset(8)] public CapStringArray StringArray;
        [FieldOffset(8)] public int Int32;
        [FieldOffset(8)] public uint UInt32;
        [FieldOffset(8)] public short Short;
    }
}
