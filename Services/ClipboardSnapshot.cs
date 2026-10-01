using System.Collections.Generic;

namespace MovaCore.Services
{
    /// <summary>
    /// A copy of the restorable part of the clipboard: raw data of a few well-known formats (text, HTML, RTF, bitmap,
    /// file list). Opaque to callers; produced and consumed by <see cref="IClipboardService"/>.
    /// </summary>
    public sealed class ClipboardSnapshot
    {
        public ClipboardSnapshot(IReadOnlyList<Item> items)
        {
            Items = items;
        }

        public IReadOnlyList<Item> Items { get; }

        public readonly record struct Item(uint Format, byte[] Data);
    }
}
