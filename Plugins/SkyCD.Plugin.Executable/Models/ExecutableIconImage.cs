using System;

namespace SkyCD.Plugin.Executable.Models;

/// <summary>
/// One decoded icon image: a straight (non-premultiplied) RGBA raster whose rows run top to bottom.
/// </summary>
/// <remarks>
/// Both storage forms of an icon — the uncompressed bitmap with an AND mask and the PNG that Windows Vista
/// introduced — end up here, so choosing the best image and scaling it do not depend on which one it was.
/// </remarks>
internal sealed record ExecutableIconImage(int Width, int Height, byte[] Rgba)
{
    /// <summary>True when the image has at least one pixel that is not fully transparent.</summary>
    public bool HasVisiblePixels
    {
        get
        {
            for (var index = 3; index < Rgba.Length; index += 4)
            {
                if (Rgba[index] != 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
