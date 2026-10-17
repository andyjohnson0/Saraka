namespace Saraka.Abstractions
{
    /// <summary>
    /// The direction <see cref="EntryOptions"/> sorts entries in.
    /// </summary>
    public enum EntrySortDirection
    {
        /// <summary>
        /// Smallest first: A to Z, or oldest first.
        /// </summary>
        Ascending,

        /// <summary>
        /// Largest first: the exact reverse of <see cref="Ascending"/>.
        /// </summary>
        Descending,
    }
}
