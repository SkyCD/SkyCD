using System.Collections.ObjectModel;

namespace SkyCD.Models.VirtualFileSystem
{
    // Generic collection constrained to IMediaChild so only allowed types can be added at compile time.
    // It also keeps the child's Parent property in sync with the collection owner.
    public class MediaChildrenCollection<T> : ObservableCollection<T>
        where T : IMediaChild
    {
        private readonly IParentItem _owner;

        public MediaChildrenCollection(IParentItem owner)
        {
            _owner = owner;
        }

        protected override void InsertItem(int index, T item)
        {
            if (item is not null)
                item.Parent = _owner;
            base.InsertItem(index, item);
        }

        protected override void SetItem(int index, T item)
        {
            var old = this[index];
            if (old is not null)
                old.Parent = null;

            if (item is not null)
                item.Parent = _owner;

            base.SetItem(index, item);
        }

        protected override void RemoveItem(int index)
        {
            var old = this[index];
            if (old is not null)
                old.Parent = null;

            base.RemoveItem(index);
        }

        protected override void ClearItems()
        {
            foreach (var item in this)
            {
                if (item is not null)
                    item.Parent = null;
            }

            base.ClearItems();
        }
    }
}
