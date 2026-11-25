using System;

namespace SkyCD.Presentation.ViewModels;

public sealed class MotionCaptureCatalogUnavailableException : InvalidOperationException
{
    public MotionCaptureCatalogUnavailableException() : base("The capture catalog is unavailable or has changed. Open a writable catalog and retry.") { }
}
