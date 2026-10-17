namespace Saraka.Abstractions
{
    /// <summary>
    /// What <see cref="EntryOptions"/> sorts entries by.
    /// </summary>
    public enum EntrySortField
    {
        /// <summary>
        /// Don't sort. Entries are returned in no particular order.
        /// </summary>
        None,

        /// <summary>
        /// Sort by name, ignoring case (<see cref="System.StringComparer.OrdinalIgnoreCase"/>).
        /// Names that are equal when case is ignored are ordered by
        /// <see cref="System.StringComparer.Ordinal"/>.
        /// </summary>
        Name,

        /// <summary>
        /// Sort by last-write time. Entries with the same time are ordered by name, as for
        /// <see cref="Name"/>.
        /// </summary>
        LastWriteTime,
    }
}
