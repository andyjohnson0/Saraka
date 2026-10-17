using System;
using System.Collections.Generic;
using System.Linq;

namespace Saraka.Abstractions
{
    /// <summary>
    /// Applies <see cref="EntryOptions"/> to a directory's entries. Implementations use it so that
    /// every file system filters and sorts in the same way.
    /// </summary>
    public static class EntrySelection
    {
        /// <summary>
        /// Filters and sorts entries as the options say.
        /// </summary>
        /// <typeparam name="T">The type that describes an entry.</typeparam>
        /// <param name="entries">The entries, in the storage's own order.</param>
        /// <param name="options">The options, or null to return every entry in the order given.</param>
        /// <param name="getName">Gets an entry's name.</param>
        /// <param name="isDirectory">Determines whether an entry is a directory.</param>
        /// <param name="isHidden">
        /// Determines whether an entry is hidden. It's called only when hidden entries are being
        /// left out.
        /// </param>
        /// <param name="getLastWriteTimeUtc">
        /// Gets an entry's last-write time. It's called only when sorting by
        /// <see cref="EntrySortField.LastWriteTime"/>, and only for the entries that the filters
        /// keep.
        /// </param>
        /// <returns>The entries the options include, sorted as they say.</returns>
        /// <exception cref="ArgumentNullException">An argument other than <paramref name="options"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="options"/> has a sort field or direction that isn't defined, or a file
        /// type that is null or empty.
        /// </exception>
        public static List<T> Apply<T>(
            IEnumerable<T> entries,
            EntryOptions? options,
            Func<T, string> getName,
            Func<T, bool> isDirectory,
            Func<T, bool> isHidden,
            Func<T, DateTime> getLastWriteTimeUtc)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            if (getName == null)
                throw new ArgumentNullException(nameof(getName));
            if (isDirectory == null)
                throw new ArgumentNullException(nameof(isDirectory));
            if (isHidden == null)
                throw new ArgumentNullException(nameof(isHidden));
            if (getLastWriteTimeUtc == null)
                throw new ArgumentNullException(nameof(getLastWriteTimeUtc));
            if (options == null)
                return entries.ToList();
            Validate(options, nameof(options));

            var fileTypes = options.FileTypes?.ToArray();
            var selected = new List<Candidate<T>>();
            foreach (var entry in entries)
            {
                var directory = isDirectory(entry);
                var name = getName(entry);
                var includeHidden = directory ? options.IncludeHiddenDirectories : options.IncludeHiddenFiles;
                if (!includeHidden && isHidden(entry))
                    continue;
                if (!directory && fileTypes != null && !HasFileType(name, fileTypes))
                    continue;
                selected.Add(new Candidate<T>(entry, name, directory));
            }

            if (options.SortField == EntrySortField.None)
                return selected.Select(c => c.Entry).ToList();

            if (options.SortField == EntrySortField.LastWriteTime)
            {
                foreach (var candidate in selected)
                    candidate.LastWriteTimeUtc = getLastWriteTimeUtc(candidate.Entry);
            }
            var comparer = new CandidateComparer<T>(
                options.SortField == EntrySortField.LastWriteTime,
                options.SortDirection == EntrySortDirection.Descending);
            return selected.OrderBy(c => c, comparer).Select(c => c.Entry).ToList();
        }

        /// <summary>
        /// Checks that options hold valid values.
        /// </summary>
        /// <param name="options">The options.</param>
        /// <param name="paramName">The name of the parameter they were passed as.</param>
        /// <exception cref="ArgumentException">A value isn't valid.</exception>
        private static void Validate(EntryOptions options, string paramName)
        {
            if (!Enum.IsDefined(typeof(EntrySortField), options.SortField))
                throw new ArgumentException("The sort field is not valid.", paramName);
            if (!Enum.IsDefined(typeof(EntrySortDirection), options.SortDirection))
                throw new ArgumentException("The sort direction is not valid.", paramName);
            if (options.FileTypes != null && options.FileTypes.Any(string.IsNullOrEmpty))
                throw new ArgumentException("The file types contain a null or empty value.", paramName);
        }

        /// <summary>
        /// Determines whether a file name has one of the given types.
        /// </summary>
        /// <param name="name">The file name.</param>
        /// <param name="fileTypes">The types, as extensions without the leading period.</param>
        /// <returns>True if the name ends with "." and one of the types, ignoring case.</returns>
        private static bool HasFileType(string name, string[] fileTypes)
        {
            foreach (var fileType in fileTypes)
            {
                if (name.Length > fileType.Length
                    && name[name.Length - fileType.Length - 1] == '.'
                    && name.EndsWith(fileType, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// An entry that the filters kept, with what sorting needs to know about it.
        /// </summary>
        /// <typeparam name="T">The type that describes an entry.</typeparam>
        private sealed class Candidate<T>
        {
            /// <summary>
            /// Creates a candidate.
            /// </summary>
            /// <param name="entry">The entry.</param>
            /// <param name="name">The entry's name.</param>
            /// <param name="isDirectory">True if the entry is a directory.</param>
            public Candidate(T entry, string name, bool isDirectory)
            {
                Entry = entry;
                Name = name;
                IsDirectory = isDirectory;
            }

            /// <summary>
            /// The entry.
            /// </summary>
            public T Entry { get; }

            /// <summary>
            /// The entry's name.
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// True if the entry is a directory.
            /// </summary>
            public bool IsDirectory { get; }

            /// <summary>
            /// The entry's last-write time. Set only when sorting by time.
            /// </summary>
            public DateTime LastWriteTimeUtc { get; set; }
        }

        /// <summary>
        /// Orders candidates: directories first, then by time if asked, then by name ignoring case,
        /// then by name with case. Descending reverses everything but directories coming first.
        /// </summary>
        /// <typeparam name="T">The type that describes an entry.</typeparam>
        private sealed class CandidateComparer<T> : IComparer<Candidate<T>>
        {
            private readonly bool _byTime;
            private readonly bool _descending;

            /// <summary>
            /// Creates a comparer.
            /// </summary>
            /// <param name="byTime">True to compare last-write times before names.</param>
            /// <param name="descending">True to reverse the order.</param>
            public CandidateComparer(bool byTime, bool descending)
            {
                _byTime = byTime;
                _descending = descending;
            }

            /// <inheritdoc/>
            public int Compare(Candidate<T>? x, Candidate<T>? y)
            {
                if (ReferenceEquals(x, y))
                    return 0;
                if (x == null)
                    return -1;
                if (y == null)
                    return 1;
                if (x.IsDirectory != y.IsDirectory)
                    return x.IsDirectory ? -1 : 1;

                var result = _byTime ? x.LastWriteTimeUtc.CompareTo(y.LastWriteTimeUtc) : 0;
                if (result == 0)
                    result = StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name);
                if (result == 0)
                    result = StringComparer.Ordinal.Compare(x.Name, y.Name);
                return _descending ? -result : result;
            }
        }
    }
}
