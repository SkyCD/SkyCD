using System;
using System.Collections.Generic;

namespace SkyCD.SDK.Capabilities.Capture;

/// <summary>One source kind a capture capability can open, such as a file, a local device or a URL.</summary>
/// <param name="Id">Stable identifier the host persists with a configured source.</param>
/// <param name="DisplayName">Localized label shown in the source picker.</param>
/// <param name="LocationLabel">Localized label for the location field the kind requires.</param>
/// <param name="UseDevicePicker">True when the location is chosen from <see cref="ICaptureSourcePluginCapability.GetDevices"/>.</param>
/// <param name="UseFilePicker">True when the location is a local file path.</param>
public sealed record CaptureSourceKind(
    string Id,
    string DisplayName,
    string LocationLabel,
    bool UseDevicePicker = false,
    bool UseFilePicker = false);

/// <summary>One capture device the plugin can enumerate, such as a webcam.</summary>
/// <param name="Id">Stable identifier the host persists with a configured source.</param>
/// <param name="DisplayName">Localized label shown in the device picker.</param>
public sealed record CaptureDevice(string Id, string DisplayName);

/// <summary>
/// A saved capture source owned by the plugin. The destination is always a physical folder, and the
/// catalog folder names the virtual collection the captured files are imported into.
/// </summary>
/// <param name="SourceId">Stable identifier assigned by the plugin.</param>
/// <param name="KindId">The <see cref="CaptureSourceKind.Id"/> the source opens.</param>
/// <param name="Location">File path, device identifier or URL selected for the kind.</param>
/// <param name="DestinationFolder">Physical folder where captured files are written.</param>
/// <param name="CatalogFolder">Virtual catalog folder that receives the captured entries.</param>
/// <param name="Label">User-facing source name.</param>
public sealed record CaptureSourceConfiguration(
    string SourceId,
    string KindId,
    string Location,
    string DestinationFolder,
    string CatalogFolder,
    string Label);

/// <summary>A source the user is configuring before the plugin assigns an identifier.</summary>
public sealed record CaptureSourceDraft(
    string KindId,
    string Location,
    string DestinationFolder,
    string CatalogFolder,
    string Label);

/// <summary>Result of validating or probing one source configuration.</summary>
/// <param name="IsAvailable">True when the source can be opened.</param>
/// <param name="Message">Localized explanation when the source is unavailable, otherwise null.</param>
public sealed record CaptureSourceProbeResult(bool IsAvailable, string? Message)
{
    /// <summary>Creates a successful probe result.</summary>
    public static CaptureSourceProbeResult Available() => new(true, null);

    /// <summary>Creates a failed probe result with a localized explanation.</summary>
    public static CaptureSourceProbeResult Unavailable(string message) => new(false, message);
}

/// <summary>
/// One preview frame with the detector overlay applied. The image is a PNG so the host can decode it
/// without referencing the plugin's imaging stack.
/// </summary>
/// <param name="Succeeded">True when a frame was captured.</param>
/// <param name="Image">PNG bytes for the annotated frame, or null when capture failed.</param>
/// <param name="Message">Localized explanation when capture failed, otherwise null.</param>
public sealed record CapturePreviewResult(bool Succeeded, byte[]? Image, string? Message)
{
    /// <summary>Creates a preview result describing why no frame could be captured.</summary>
    public static CapturePreviewResult Failed(string message) => new(false, null, message);

    /// <summary>Creates a preview result carrying the annotated PNG frame.</summary>
    public static CapturePreviewResult Captured(byte[] image) => new(true, image, null);
}

/// <summary>Mutable detector settings the plugin persists and applies to every source without an override.</summary>
/// <param name="DetectorId">Selected detector identifier, or null to leave detection off.</param>
/// <param name="Parameters">Detector parameters keyed by the names the detector reports.</param>
public sealed record CaptureDetectorSettings(string? DetectorId, IReadOnlyDictionary<string, double> Parameters);

/// <summary>One selectable detector with its scientific name and tunable parameters.</summary>
public sealed record CaptureDetectorDescriptor(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<CaptureDetectorParameter> Parameters);

/// <summary>One numeric detector parameter with its default, minimum and maximum.</summary>
public sealed record CaptureDetectorParameter(string Key, string DisplayName, double DefaultValue, double Minimum,
    double Maximum);
