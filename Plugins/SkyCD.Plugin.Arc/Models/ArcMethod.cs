using System;

namespace SkyCD.Plugin.Arc.Models;

/// <summary>Compression method declared by an ARC entry header.</summary>
internal enum ArcMethod
{
    /// <summary>File data is stored uncompressed and extracted verbatim.</summary>
    Stored,

    /// <summary>File data uses the ARC "packed" adaptive Huffman method (method 1), which is not decoded.</summary>
    Packed,

    /// <summary>File data uses a historical ARC compression method that is not decoded.</summary>
    Unsupported,

    /// <summary>
    /// Reserved header that stores archive settings instead of a file, such as a text-mode model ("=TX=").
    /// It carries no file data and is never listed.
    /// </summary>
    Reserved
}
