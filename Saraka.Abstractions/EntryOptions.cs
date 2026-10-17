using System.Collections.Generic;
using System.IO;

namespace Saraka.Abstractions
{
    /// <summary>
    /// Options that filter and sort the entries returned by
    /// <see cref="IDirectory.GetEntriesAsync"/>, <see cref="IDirectory.EnumerateFilesAsync"/> and
    /// <see cref="IDirectory.EnumerateDirectoriesAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The defaults include every entry, unsorted, which is the same as passing no options. The
    /// options affect only the call they're passed to. Lookups by name, such as
    /// <see cref="IDirectory.GetFileAsync"/>, ignore them.
    /// </para>
    /// <para>
    /// An entry is hidden if its attributes include <see cref="FileAttributes.Hidden"/>. Each
    /// implementation documents when that's the case: for example, a name starting with "." on
    /// Android, or the Hidden attribute on Windows.
    /// </para>
    /// </remarks>
    public sealed class EntryOptions
    {
        /// <summary>
        /// The file types to include, as file name extensions without the leading period (for
        /// example "jpeg"). A file is included if its name ends with "." and one of the types,
        /// ignoring case. Include any synonyms you want, such as both "jpeg" and "jpg". Null, the
        /// default, includes every file, and an empty collection includes none. Directories are
        /// not affected.
        /// </summary>
        public IEnumerable<string>? FileTypes { get; set; }

        /// <summary>
        /// True, the default, to include hidden files.
        /// </summary>
        public bool IncludeHiddenFiles { get; set; } = true;

        /// <summary>
        /// True, the default, to include hidden directories.
        /// </summary>
        public bool IncludeHiddenDirectories { get; set; } = true;

        /// <summary>
        /// What to sort by. The default, <see cref="EntrySortField.None"/>, returns entries in no
        /// particular order. Otherwise directories come before files, and each is sorted.
        /// </summary>
        public EntrySortField SortField { get; set; } = EntrySortField.None;

        /// <summary>
        /// The direction to sort in. The default is <see cref="EntrySortDirection.Ascending"/>.
        /// Directories still come before files when sorting in either direction.
        /// </summary>
        public EntrySortDirection SortDirection { get; set; } = EntrySortDirection.Ascending;
    }
}
