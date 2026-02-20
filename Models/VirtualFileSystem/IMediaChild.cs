namespace SkyCD.Models.VirtualFileSystem
{
    // Interface used to indicate types that are valid children of a MediaItem.
    // Also exposes a Parent property so collections can update parent references
    // automatically when children are added/removed.
    public interface IMediaChild
    {
        IParentItem? Parent { get; set; }
    }
}
