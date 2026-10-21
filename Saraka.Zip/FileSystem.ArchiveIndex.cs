using System;
using System.Collections.Generic;
using System.IO.Compression;

namespace Saraka.Zip
{
    public sealed partial class FileSystem
    {
        /// <summary>
        /// An index of a read-only archive's entries, built once when the archive is opened, so
        /// that lookups and a directory's entries don't search every entry in the archive. A
        /// read-only archive never changes, so the index never goes out of date. It answers the
        /// same questions as the searches it replaces, with the same results.
        /// </summary>
        private sealed class ArchiveIndex
        {
            private readonly Dictionary<string, ZipArchiveEntry> _files;
            private readonly Dictionary<string, ZipArchiveEntry> _directoryEntries;
            private readonly Dictionary<string, DirectoryContents> _directories;

            /// <summary>
            /// Indexes an archive's entries.
            /// </summary>
            /// <param name="entries">The archive's entries, in the archive's order.</param>
            public ArchiveIndex(IReadOnlyCollection<ZipArchiveEntry> entries)
            {
                _files = new Dictionary<string, ZipArchiveEntry>(entries.Count, StringComparer.Ordinal);
                _directoryEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                _directories = new Dictionary<string, DirectoryContents>(StringComparer.Ordinal);
                var root = new DirectoryContents();
                _directories.Add(string.Empty, root);

                foreach (var entry in entries)
                {
                    var fullName = entry.FullName;
                    var time = FromZipTime(entry.LastWriteTime);

                    // The first entry with a name wins, as it does for a search in archive order.
                    if (fullName.Length > 0 && fullName[fullName.Length - 1] == Separator)
                    {
                        var directoryName = fullName.Substring(0, fullName.Length - 1);
                        if (!_directoryEntries.ContainsKey(directoryName))
                            _directoryEntries.Add(directoryName, entry);
                    }
                    else if (!_files.ContainsKey(fullName))
                    {
                        _files.Add(fullName, entry);
                    }

                    // Walk down the name: every name up to a "/" is a directory that contains this
                    // entry, and the next part of the name is one of that directory's children.
                    // Stop at a part that isn't a valid name: no valid path reaches anything below
                    // it. (A name starting with "/" would otherwise make "" look like the root.)
                    var parent = root;
                    var start = 0;
                    while (true)
                    {
                        parent.Include(time);
                        var separatorIndex = fullName.IndexOf(Separator, start);
                        if (separatorIndex < 0)
                        {
                            if (start < fullName.Length)
                                parent.AddChild(fullName.Substring(start), isDirectory: false);
                            break;
                        }

                        var childName = fullName.Substring(start, separatorIndex - start);
                        if (!IsValidName(childName))
                            break;
                        parent.AddChild(childName, isDirectory: true);
                        var directoryName = fullName.Substring(0, separatorIndex);
                        if (!_directories.TryGetValue(directoryName, out var directory))
                        {
                            directory = new DirectoryContents();
                            _directories.Add(directoryName, directory);
                        }
                        parent = directory;
                        start = separatorIndex + 1;
                    }
                }

                foreach (var directory in _directories.Values)
                    directory.FinishBuilding();
            }

            /// <summary>
            /// Finds the zip entry for a file.
            /// </summary>
            /// <param name="name">The file's zip entry name.</param>
            /// <returns>The first entry with that exact name, or null if there is none.</returns>
            public ZipArchiveEntry? FindFile(string name)
            {
                return _files.TryGetValue(name, out var entry) ? entry : null;
            }

            /// <summary>
            /// Finds a directory's own zip entry (its name followed by "/").
            /// </summary>
            /// <param name="name">The directory's zip entry name, without a trailing "/".</param>
            /// <returns>The directory's own entry, or null if it has none.</returns>
            public ZipArchiveEntry? FindDirectoryEntry(string name)
            {
                return _directoryEntries.TryGetValue(name, out var entry) ? entry : null;
            }

            /// <summary>
            /// Determines whether any entry is inside a directory.
            /// </summary>
            /// <param name="name">The directory's zip entry name, without a trailing "/", or "" for the root.</param>
            /// <returns>True if an entry's name starts with the name followed by "/", or if it's the root.</returns>
            public bool DirectoryExists(string name)
            {
                return _directories.ContainsKey(name);
            }

            /// <summary>
            /// Gets the latest last-write time of the entries inside a directory.
            /// </summary>
            /// <param name="name">The directory's zip entry name, without a trailing "/", or "" for the root.</param>
            /// <returns>The latest time, in UTC, or null if no entry is inside the directory.</returns>
            public DateTime? LatestTimeInside(string name)
            {
                return _directories.TryGetValue(name, out var directory) ? directory.LatestTimeUtc : null;
            }

            /// <summary>
            /// Gets a directory's direct children, in the order that they first appear in the
            /// archive. Children whose names aren't valid are left out, and a name appears at most
            /// once as a file and once as a directory.
            /// </summary>
            /// <param name="name">The directory's zip entry name, without a trailing "/", or "" for the root.</param>
            /// <returns>The children, or an empty list if no entry is inside the directory.</returns>
            public IReadOnlyList<Child> GetChildren(string name)
            {
                return _directories.TryGetValue(name, out var directory) ? directory.Children : Array.Empty<Child>();
            }

            /// <summary>
            /// A direct child of a directory.
            /// </summary>
            public readonly struct Child
            {
                /// <summary>
                /// Creates a child.
                /// </summary>
                /// <param name="name">The child's name.</param>
                /// <param name="isDirectory">True if the child is a directory.</param>
                public Child(string name, bool isDirectory)
                {
                    Name = name;
                    IsDirectory = isDirectory;
                }

                /// <summary>The child's name.</summary>
                public string Name { get; }

                /// <summary>True if the child is a directory, false if it's a file.</summary>
                public bool IsDirectory { get; }
            }

            /// <summary>
            /// What the index knows about one directory that has entries inside it.
            /// </summary>
            private sealed class DirectoryContents
            {
                private readonly List<Child> _children = new List<Child>();
                private HashSet<string>? _fileNames = new HashSet<string>(StringComparer.Ordinal);
                private HashSet<string>? _directoryNames = new HashSet<string>(StringComparer.Ordinal);

                /// <summary>The directory's direct children, in archive order.</summary>
                public IReadOnlyList<Child> Children => _children;

                /// <summary>The latest last-write time of the entries inside the directory, in UTC.</summary>
                public DateTime? LatestTimeUtc { get; private set; }

                /// <summary>
                /// Records that an entry is inside the directory.
                /// </summary>
                /// <param name="timeUtc">The entry's last-write time, in UTC.</param>
                public void Include(DateTime timeUtc)
                {
                    if (LatestTimeUtc == null || timeUtc > LatestTimeUtc.Value)
                        LatestTimeUtc = timeUtc;
                }

                /// <summary>
                /// Adds a direct child, unless its name isn't valid or a child of the same kind
                /// already has the name.
                /// </summary>
                /// <param name="name">The child's name.</param>
                /// <param name="isDirectory">True if the child is a directory.</param>
                public void AddChild(string name, bool isDirectory)
                {
                    if (!IsValidName(name))
                        return;
                    if ((isDirectory ? _directoryNames : _fileNames)!.Add(name))
                        _children.Add(new Child(name, isDirectory));
                }

                /// <summary>
                /// Releases the memory used only while the index is being built.
                /// </summary>
                public void FinishBuilding()
                {
                    _fileNames = null;
                    _directoryNames = null;
                    _children.TrimExcess();
                }
            }
        }
    }
}
