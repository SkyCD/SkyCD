using System;

namespace SkyCD.Presentation.Collections;

public sealed class SavedSearchException : InvalidOperationException
{
    public SavedSearchException()
        : base("Saved search is corrupt or unsupported. Edit or delete the saved search and create it again.")
    {
    }
}
