using System;

namespace SkyCD.Plugin.Metadata.OpenOffice.Models;

/// <summary>Which reader served a package.</summary>
internal enum OpenOfficePackageSource
{
    /// <summary>
    /// The OfficeIMO reader accepted the package, validated its container and served its members.
    /// </summary>
    OfficeImo,

    /// <summary>
    /// The package was read from its ZIP members because the OfficeIMO reader does not accept it: the
    /// templates, master and web documents, charts, images, databases and formulas it rejects, the legacy
    /// OpenOffice.org packages, and the packages it cannot parse.
    /// </summary>
    ZipMembers
}
