namespace SkyCD.Plugin.Feature.MotionDetector.Models;

/// <summary>Localization keys owned by the motion detector plugin, namespaced to its plugin id.</summary>
public static class MotionDetectorKeys
{
    private const string Prefix = "plugins.SkyCD.Plugin.MotionDetector.ui.";

    public const string ProviderName = Prefix + "ProviderName";
    public const string SourceKindFile = Prefix + "SourceKind.File";
    public const string SourceKindDevice = Prefix + "SourceKind.Device";
    public const string SourceKindJpegUrl = Prefix + "SourceKind.JpegUrl";
    public const string SourceKindMjpegUrl = Prefix + "SourceKind.MjpegUrl";
    public const string SourceKindMmsStream = Prefix + "SourceKind.MmsStream";
    public const string LocationFile = Prefix + "Location.File";
    public const string LocationDevice = Prefix + "Location.Device";
    public const string LocationUrl = Prefix + "Location.Url";
    public const string DeviceName = Prefix + "DeviceName";
    public const string OptionsPage = Prefix + "OptionsPage";
    public const string OptionsHint = Prefix + "OptionsHint";
    public const string DetectorLabel = Prefix + "DetectorLabel";
    public const string DetectorNone = Prefix + "DetectorNone";
    public const string ParametersLabel = Prefix + "ParametersLabel";
    public const string Save = Prefix + "Save";
    public const string Saved = Prefix + "Saved";
    public const string RestartHint = Prefix + "RestartHint";
    public const string CatalogUnavailable = Prefix + "CatalogUnavailable";
    public const string NativeUnavailable = Prefix + "NativeUnavailable";
    public const string SourceDirectoryMissing = Prefix + "SourceDirectoryMissing";
    public const string SourceFileMissing = Prefix + "SourceFileMissing";
    public const string SourceUrlInvalid = Prefix + "SourceUrlInvalid";
    public const string SourceOpenFailed = Prefix + "SourceOpenFailed";
    public const string SourceStreamEnded = Prefix + "SourceStreamEnded";
    public const string PreviewFailed = Prefix + "PreviewFailed";
    public const string DestinationRequired = Prefix + "DestinationRequired";
    public const string DestinationMissing = Prefix + "DestinationMissing";
    public const string CatalogFolderRequired = Prefix + "CatalogFolderRequired";
    public const string LabelRequired = Prefix + "LabelRequired";
    public const string SourceAdded = Prefix + "SourceAdded";
    public const string SourceRemoved = Prefix + "SourceRemoved";
    public const string FrameWritten = Prefix + "FrameWritten";
    public const string ImportFailed = Prefix + "ImportFailed";
    public const string CaptureSuspended = Prefix + "CaptureSuspended";

    /// <summary>Localization key for a detector name, for example <c>Detector.mog2.Name</c>.</summary>
    public static string DetectorName(string detectorId) => Prefix + "Detector." + detectorId + ".Name";

    /// <summary>Localization key for a detector description.</summary>
    public static string DetectorDescription(string detectorId) =>
        Prefix + "Detector." + detectorId + ".Description";

    /// <summary>Localization key for a detector parameter label.</summary>
    public static string ParameterName(string parameterKey) => Prefix + "Parameter." + parameterKey;
}
