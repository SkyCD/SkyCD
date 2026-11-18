using System;

namespace SkyCD.Plugin.DiskExplorer.Ded.Exceptions;

/// <summary>Raised when a selected-volume import or export cannot be performed.</summary>
public sealed class DedTransferException(string detail)
    : InvalidOperationException($"Disk Explorer volume transfer failed: {detail}");
