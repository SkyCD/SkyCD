namespace SkyCD.Plugin.Metadata.Audio.AudioCD.Sources;

/// <summary>An online metadata source the plugin can query, in the order the user configured.</summary>
public enum AudioCdSource
{
    /// <summary>MusicBrainz WS/2 disc lookups, keyed by the MusicBrainz disc id.</summary>
    MusicBrainz,

    /// <summary>GnuDb freedb lookups, keyed by the CDDB disc id.</summary>
    GnuDb
}
