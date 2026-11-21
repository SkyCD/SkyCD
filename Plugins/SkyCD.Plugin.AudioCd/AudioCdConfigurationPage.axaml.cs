using System;
using Avalonia.Controls;

namespace SkyCD.Plugin.AudioCd;

public partial class AudioCdConfigurationPage : UserControl
{
    private readonly AudioCdConfigurationEditor editor;

    public AudioCdConfigurationPage()
        : this(new AudioCdConfigurationEditor())
    {
    }

    public AudioCdConfigurationPage(AudioCdConfigurationEditor editor)
    {
        this.editor = editor;
        InitializeComponent();

        MusicBrainzCheckBox.IsChecked = editor.MusicBrainzEnabled;
        GnudbCheckBox.IsChecked = editor.GnuDbEnabled;
        OfflineCheckBox.IsChecked = editor.Offline;
        DiscArtistTagsCheckBox.IsChecked = editor.DiscArtistTags;
        DiscCoversCheckBox.IsChecked = editor.DiscCovers;
        TrackArtistTagsCheckBox.IsChecked = editor.TrackArtistTags;
        TrackCoversCheckBox.IsChecked = editor.TrackCovers;
        DiscCoverSizeInput.Value = editor.DiscCoverSize;
        TrackCoverSizeInput.Value = editor.TrackCoverSize;
        RequestTimeoutInput.Value = editor.RequestTimeoutSeconds;
    }

    public void UpdateDraft()
    {
        editor.MusicBrainzEnabled = MusicBrainzCheckBox.IsChecked == true;
        editor.GnuDbEnabled = GnudbCheckBox.IsChecked == true;
        editor.Offline = OfflineCheckBox.IsChecked == true;
        editor.DiscArtistTags = DiscArtistTagsCheckBox.IsChecked == true;
        editor.DiscCovers = DiscCoversCheckBox.IsChecked == true;
        editor.TrackArtistTags = TrackArtistTagsCheckBox.IsChecked == true;
        editor.TrackCovers = TrackCoversCheckBox.IsChecked == true;
        editor.DiscCoverSize = ToInt(DiscCoverSizeInput.Value, editor.DiscCoverSize);
        editor.TrackCoverSize = ToInt(TrackCoverSizeInput.Value, editor.TrackCoverSize);
        editor.RequestTimeoutSeconds = ToInt(RequestTimeoutInput.Value, editor.RequestTimeoutSeconds);
    }

    private static int ToInt(decimal? value, int fallback) =>
        value is null ? fallback : (int)Math.Round(value.Value);
}
