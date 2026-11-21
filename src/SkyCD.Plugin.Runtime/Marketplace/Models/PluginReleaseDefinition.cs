using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Runtime.Marketplace.Models;

/// <summary>Compatibility metadata of one manifest release, before it is bound to a source.</summary>
/// <param name="Version">Package version.</param>
/// <param name="MinHostVersion">Minimum host version.</param>
/// <param name="MaxHostVersion">Optional maximum host version.</param>
/// <param name="Platforms">Runtime identifiers; empty means any platform.</param>
public sealed record PluginReleaseDefinition(
    PluginPackageVersion Version,
    Version MinHostVersion,
    Version? MaxHostVersion,
    IReadOnlyList<string> Platforms);
