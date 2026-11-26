using System;

namespace SkyCD.Plugin.Metadata.VideoProjects.Models;

/// <summary>Stable identifiers published in the <c>Kind</c>, <c>Format</c> and <c>Application</c> fields.</summary>
internal static class VideoProjectIdentity
{
    public const string KindPremiere = "Premiere";
    public const string KindAfterEffects = "AfterEffects";
    public const string KindResolve = "Resolve";
    public const string KindVegas = "Vegas";

    public const string FormatPrproj = "prproj";
    public const string FormatAep = "aep";
    public const string FormatAepx = "aepx";
    public const string FormatDrp = "drp";
    public const string FormatDrt = "drt";
    public const string FormatVeg = "veg";

    public const string ApplicationPremiere = "Adobe Premiere Pro";
    public const string ApplicationAfterEffects = "Adobe After Effects";
    public const string ApplicationResolve = "DaVinci Resolve";
    public const string ApplicationVegas = "VEGAS Pro";
}
