using System;

namespace SkyCD.Plugin.FileFormat.Mdf.Parsing;

/// <summary>
/// The parts of an MDS descriptor this plugin validates. The descriptor names the image, but the data
/// layout is probed from the MDF file itself because Alcohol images store it in variant encodings.
/// </summary>
internal sealed record MdsDescriptor(string Version, ushort MediumType, ushort SessionCount);
