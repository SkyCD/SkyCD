namespace SkyCD.Plugin.CdCatalogExpert;

/// <summary>
/// Container constants for CD Catalog Expert 9.x catalogs (<c>.zdc</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is the single documented writer profile: new files use
/// <see cref="ChunkSize"/>-byte chunks, reserve chunks <c>0..3</c>, start the
/// directory tree at chunk <see cref="FirstDirectoryChunk"/>, sort members as
/// <c>zdc_index.xml</c> then disc XML by GUID then covers by GUID, wrap
/// payloads in AACS with <see cref="AacsCompressionField"/>, and split wrapped
/// payloads into <see cref="DataAreaSize"/>-byte data areas.
/// </para>
/// <para>All integers are little-endian.</para>
/// </remarks>
public static class ZdcFormat
{
    /// <summary>
    /// ASCII magic at container offset <c>0x000</c>: <c>AAMVHFSS</c>.
    /// </summary>
    public static readonly byte[] ContainerMagic =
    [
        0x41, 0x41, 0x4D, 0x56, 0x48, 0x46, 0x53, 0x53
    ];

    /// <summary>
    /// ASCII magic at container offset <c>0x118</c>: <c>AASFSSGN</c>.
    /// </summary>
    public static readonly byte[] ProfileMagic =
    [
        0x41, 0x41, 0x53, 0x46, 0x53, 0x53, 0x47, 0x4E
    ];

    /// <summary>
    /// ASCII magic at the start of every wrapped member payload: <c>AACS</c>.
    /// </summary>
    public static readonly byte[] AacsMagic = [0x41, 0x41, 0x43, 0x53];

    public const int HeaderSize = 364;
    public const int ContainerMagicOffset = 0x000;
    public const int ProfileMagicOffset = 0x118;
    public const int CscOffset = 0x120;
    public const int OofOffset = 0x124;
    public const int ChunkSizeOffset = 0x128;
    public const int ProfileValuesOffset = 0x12C;
    public const int FirstDirectoryChunkOffset = 0x140;
    public const int EntryCountOffset = 0x144;
    public const int ChunkCountOffset = 0x148;
    public const int ContainerKeyOffset = 0x14C;
    public const int ContainerKeySize = 32;

    /// <summary>
    /// Reserved/profile data between the two magics; preserved verbatim.
    /// </summary>
    public const int ReservedProfileOffset = 0x008;
    public const int ReservedProfileSize = 272;

    /// <summary>
    /// The only supported chunk size (bytes).
    /// </summary>
    public const int ChunkSize = 4096;

    /// <summary>
    /// Offset from which chunk numbers are measured: chunk <c>n</c> starts at
    /// <c>280 + (n * chunkSize)</c>.
    /// </summary>
    public const int ChunkBaseOffset = 280;

    /// <summary>
    /// Chunk number where the writer starts the directory tree; chunks
    /// <c>0..3</c> are reserved (zero-filled past the 364-byte header).
    /// </summary>
    public const int FirstDirectoryChunk = 4;

    /// <summary>
    /// Byte size of one directory-tree / file-index / file-data chunk header.
    /// </summary>
    public const int ChunkHeaderSize = 0x20;

    /// <summary>
    /// Usable member-data bytes per file-data chunk.
    /// </summary>
    public const int DataAreaSize = 4064;

    /// <summary>
    /// Byte size of one directory-tree member record.
    /// </summary>
    public const int MemberRecordSize = 512;

    /// <summary>
    /// Member records per directory-tree chunk (7 x 512 after the 32-byte header).
    /// </summary>
    public const int MembersPerTreeChunk = 7;

    /// <summary>
    /// Chunk-number references per file-index chunk (4064 bytes of Int32).
    /// </summary>
    public const int ReferencesPerIndexChunk = 1016;

    public const int MemberNameFieldSize = 288;
    public const int MemberKeySize = 32;
    public const int MemberReservedSize = 140;

    /// <summary>
    /// Reserved bytes in a file-data chunk header (offset <c>0x0C..0x20</c>).
    /// </summary>
    public const int DataHeaderReservedSize = 20;

    /// <summary>
    /// File-data chunk flags for unencrypted ZDC chunks.
    /// </summary>
    public const uint PlainDataFlags = 0x00000006;

    /// <summary>
    /// File-data chunk flag outside the initial ZDC scope: encrypted content.
    /// </summary>
    public const uint EncryptedDataFlag = 0x00000100;

    /// <summary>
    /// AACS compression method/level field written by the ZDC profile.
    /// </summary>
    public const uint AacsCompressionField = 5;

    public const uint AacsRequiredOne = 1;
    public const uint AacsRequiredMagic2 = 0x40000000;
    public const int AacsHeaderSize = 0x90;

    /// <summary>
    /// Canonical file name of the catalog index member.
    /// </summary>
    public const string IndexMemberName = "zdc_index.xml";

    /// <summary>Maximum members accepted in a single container.</summary>
    public const int MaxMemberCount = 1_000_000;

    /// <summary>Maximum stored (wrapped) bytes accepted for one member.</summary>
    public const long MaxStoredBytes = 64L * 1024 * 1024;

    /// <summary>Maximum decompressed bytes accepted for one member.</summary>
    public const long MaxInflatedBytes = 128L * 1024 * 1024;

    /// <summary>Maximum total container bytes read from a stream.</summary>
    public const long MaxContainerBytes = 256L * 1024 * 1024;

    /// <summary>Maximum nesting depth accepted in catalog XML members.</summary>
    public const int MaxXmlDepth = 64;

    /// <summary>Maximum element count accepted in one catalog XML member.</summary>
    public const int MaxXmlElements = 2_000_000;

    /// <summary>Maximum attributes accepted on one catalog XML element.</summary>
    public const int MaxAttributesPerElement = 64;

    /// <summary>Maximum character count accepted for one attribute/text value.</summary>
    public const int MaxXmlTextLength = 4 * 1024 * 1024;

    /// <summary>Maximum characters accepted for one catalog XML document.</summary>
    public const long MaxXmlDocumentCharacters = 256L * 1024 * 1024;
}
