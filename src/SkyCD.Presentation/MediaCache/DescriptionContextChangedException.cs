using System;

namespace SkyCD.Presentation.MediaCache;

public sealed class DescriptionContextChangedException() : InvalidOperationException(
    "The selected file or its cached context changed during generation. Generate the description again.");
