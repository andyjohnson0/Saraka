using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saraka.Abstractions;

namespace Saraka.Zip
{
    public sealed partial class FileSystem
    {
        /// <summary>
        /// A directory in the archive. It exists if it has its own entry, or if any entry is
        /// inside it.
        /// </summary>
        private sealed class DirectoryEntry : Entry, IDirectory
        {
            /// <summary>
            /// Creates an entry for the directory at a Saraka path.
            /// </summary>
            /// <param name="owner">The file system the directory belongs to.</param>
            /// <param name="path">The directory's Saraka path.</param>
            public DirectoryEntry(FileSystem owner, string path)
                : base(owner, path)
            {
            }

            /// <inheritdoc/>
            public Task<IEnumerable<IFileSystemEntry>> GetEntriesAsync(EntryOptions? options = null, CancellationToken cancellationToken = default)
            {
                return Owner.Locked(() =>
                {
                    PrepareRead(cancellationToken);
                    return (IEnumerable<IFileSystemEntry>)Select(
                        ListChildren(files: true, directories: true, _ => true, cancellationToken), options);
                });
            }

            /// <inheritdoc/>
            public Task<IDirectory> GetDirectoryAsync(string name, CancellationToken cancellationToken = default)
            {
                return Owner.Locked(() =>
                {
                    var childName = PrepareLookup(name, cancellationToken);
                    if (!Owner.DirectoryExists(childName))
                        throw new DirectoryNotFoundException($"'{Path}' has no subdirectory named '{name}'.");
                    return (IDirectory)new DirectoryEntry(Owner, CombinePath(Path, name));
                });
            }

            /// <inheritdoc/>
            public Task<IFile> GetFileAsync(string name, CancellationToken cancellationToken = default)
            {
                return Owner.Locked(() =>
                {
                    var childName = PrepareLookup(name, cancellationToken);
                    if (!Owner.FileExists(childName))
                        throw new FileNotFoundException($"'{Path}' has no file named '{name}'.");
                    return (IFile)new FileEntry(Owner, CombinePath(Path, name));
                });
            }

            /// <inheritdoc/>
            public Task<IEnumerable<IFile>> EnumerateFilesAsync(string searchPattern = "*", EntryOptions? options = null, CancellationToken cancellationToken = default)
            {
                return Owner.Locked(() =>
                {
                    PrepareEnumeration(searchPattern, cancellationToken);
                    return (IEnumerable<IFile>)Select(
                            ListChildren(files: true, directories: false, Matcher(searchPattern), cancellationToken), options)
                        .Cast<IFile>()
                        .ToList();
                });
            }

            /// <inheritdoc/>
            public Task<IEnumerable<IDirectory>> EnumerateDirectoriesAsync(string searchPattern = "*", EntryOptions? options = null, CancellationToken cancellationToken = default)
            {
                return Owner.Locked(() =>
                {
                    PrepareEnumeration(searchPattern, cancellationToken);
                    return (IEnumerable<IDirectory>)Select(
                            ListChildren(files: false, directories: true, Matcher(searchPattern), cancellationToken), options)
                        .Cast<IDirectory>()
                        .ToList();
                });
            }

            /// <summary>
            /// Applies the options to this directory's children. An entry is hidden if its MS-DOS
            /// Hidden bit is set. A directory without its own entry is never hidden, and its time is
            /// the latest time of anything inside it.
            /// </summary>
            /// <param name="children">The children, in the archive's order.</param>
            /// <param name="options">The options, or null.</param>
            /// <returns>The selected children.</returns>
            private static List<IFileSystemEntry> Select(List<IFileSystemEntry> children, EntryOptions? options)
            {
                return EntrySelection.Apply(
                    children,
                    options,
                    child => child.Name,
                    child => child is IDirectory,
                    child => (((Entry)child).ReadAttributes() & FileAttributes.Hidden) != 0,
                    child => ((Entry)child).ReadLastWriteTimeUtc());
            }

            /// <inheritdoc/>
            public Task<IDirectory> CreateDirectoryAsync(string name, CancellationToken cancellationToken = default)
            {
                return Owner.Locked(() =>
                {
                    var childName = PrepareCreate(name, cancellationToken);
                    Owner._archive.CreateEntry(childName + Separator);
                    return (IDirectory)new DirectoryEntry(Owner, CombinePath(Path, name));
                });
            }

            /// <inheritdoc/>
            public Task<IFile> CreateFileAsync(string name, CancellationToken cancellationToken = default)
            {
                return Owner.Locked(() =>
                {
                    var childName = PrepareCreate(name, cancellationToken);
                    Owner._archive.CreateEntry(childName);
                    return (IFile)new FileEntry(Owner, CombinePath(Path, name));
                });
            }

            /// <inheritdoc/>
            /// <remarks>The archive stays locked while the content is copied in.</remarks>
            public Task<IFile> CreateFileAsync(string name, Stream content, CancellationToken cancellationToken = default)
            {
                return Owner.LockedAsync<IFile>(async () =>
                {
                    Owner.ThrowIfDisposed();
                    ValidateName(name, nameof(name));
                    if (content == null)
                        throw new ArgumentNullException(nameof(content));
                    if (!content.CanRead)
                        throw new ArgumentException("The content stream can't be read.", nameof(content));
                    var childName = PrepareCreate(name, cancellationToken);

                    var entry = Owner._archive.CreateEntry(childName);
                    try
                    {
                        using (var stream = entry.Open())
                        {
                            await content.CopyToAsync(stream, CopyBufferSize, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        // Don't leave a partly written entry behind.
                        entry.Delete();
                        throw;
                    }
                    return new FileEntry(Owner, CombinePath(Path, name));
                });
            }

            /// <inheritdoc/>
            public Task DeleteAsync(bool recursive = false, CancellationToken cancellationToken = default)
            {
                return Owner.Locked(() =>
                {
                    Owner.ThrowIfDisposed();
                    cancellationToken.ThrowIfCancellationRequested();
                    Owner.EnsureWritable();
                    if (IsRoot)
                        throw new InvalidOperationException("The root directory can't be deleted.");
                    EnsureExists();

                    var prefix = DirectoryPrefix(EntryName);
                    var entries = Owner.FindEntriesWithPrefix(prefix);
                    if (!recursive && entries.Any(e => e.FullName.Length > prefix.Length))
                        throw new InvalidOperationException($"The directory '{Path}' is not empty.");
                    foreach (var entry in entries)
                        entry.Delete();
                    Owner.KeepDirectory(GetParentName(EntryName));
                });
            }

            /// <inheritdoc/>
            protected override bool Exists() => Owner.DirectoryExists(EntryName);

            /// <inheritdoc/>
            protected override IOException NotFound() =>
                new DirectoryNotFoundException($"The directory '{Path}' does not exist.");

            /// <inheritdoc/>
            protected override FileAttributes GetAttributesCore()
            {
                var entry = Owner.FindDirectoryEntry(EntryName);
                return entry == null
                    ? FileAttributes.Directory
                    : FromExternalAttributes(entry.ExternalAttributes, isDirectory: true);
            }

            /// <inheritdoc/>
            /// <remarks>The root has no entry of its own, so its attributes can't be changed.</remarks>
            protected override void SetAttributesCore(FileAttributes attributes)
            {
                if (IsRoot)
                    return;
                var entry = GetOrCreateOwnEntry();
                entry.ExternalAttributes = ToExternalAttributes(entry.ExternalAttributes, attributes, isDirectory: true);
            }

            /// <inheritdoc/>
            /// <remarks>
            /// A directory without its own entry reports the latest time of anything inside it. An
            /// empty root reports the zip file's last-write time.
            /// </remarks>
            protected override DateTime GetLastWriteTimeCore()
            {
                var entry = Owner.FindDirectoryEntry(EntryName);
                if (entry != null)
                    return FromZipTime(entry.LastWriteTime);

                var contents = Owner.FindEntriesWithPrefix(DirectoryPrefix(EntryName));
                return contents.Count == 0
                    ? Owner._archiveLastWriteTimeUtc
                    : contents.Max(e => FromZipTime(e.LastWriteTime));
            }

            /// <inheritdoc/>
            /// <remarks>The root has no entry of its own, so its time can't be changed.</remarks>
            protected override void SetLastWriteTimeCore(DateTimeOffset lastWriteTime)
            {
                if (IsRoot)
                    return;
                GetOrCreateOwnEntry().LastWriteTime = lastWriteTime;
            }

            /// <summary>
            /// Gets this directory's own entry, creating it if the directory exists only because of its
            /// contents.
            /// </summary>
            /// <returns>The directory's own entry.</returns>
            private ZipArchiveEntry GetOrCreateOwnEntry()
            {
                return Owner.FindDirectoryEntry(EntryName) ?? Owner._archive.CreateEntry(EntryName + Separator);
            }

            /// <summary>
            /// Lists the direct children of this directory. A child directory may have its own entry
            /// ("a/") or exist only because entries are inside it ("a/b.txt"). Names that aren't valid
            /// are skipped, and for duplicates the first entry wins.
            /// </summary>
            /// <param name="files">True to include files.</param>
            /// <param name="directories">True to include directories.</param>
            /// <param name="match">Selects children by name.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The children.</returns>
            private List<IFileSystemEntry> ListChildren(
                bool files, bool directories, Func<string, bool> match, CancellationToken cancellationToken)
            {
                var prefix = DirectoryPrefix(EntryName);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var children = new List<IFileSystemEntry>();
                foreach (var entry in Owner._archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fullName = entry.FullName;
                    if (fullName.Length <= prefix.Length || !fullName.StartsWith(prefix, StringComparison.Ordinal))
                        continue;

                    var rest = fullName.Substring(prefix.Length);
                    var separatorIndex = rest.IndexOf(Separator);
                    var isDirectory = separatorIndex >= 0;
                    var childName = isDirectory ? rest.Substring(0, separatorIndex) : rest;
                    if (!IsValidName(childName) || (isDirectory ? !directories : !files) || !match(childName))
                        continue;
                    if (!seen.Add((isDirectory ? "d:" : "f:") + childName))
                        continue;

                    var childPath = CombinePath(Path, childName);
                    children.Add(isDirectory
                        ? (IFileSystemEntry)new DirectoryEntry(Owner, childPath)
                        : new FileEntry(Owner, childPath));
                }
                return children;
            }

            /// <summary>
            /// Creates a case-sensitive matcher for a search pattern.
            /// </summary>
            /// <param name="searchPattern">The search pattern, using "*" and "?".</param>
            /// <returns>A function that tests a name against the pattern.</returns>
            private static Func<string, bool> Matcher(string searchPattern)
            {
                return name => FileSystemName.MatchesSimpleExpression(searchPattern, name, ignoreCase: false);
            }

            /// <summary>
            /// Makes the checks for looking up a child by name.
            /// </summary>
            /// <param name="name">The child's name.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The child's zip entry name.</returns>
            private string PrepareLookup(string name, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                ValidateName(name, nameof(name));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExists();
                return ToEntryName(CombinePath(Path, name));
            }

            /// <summary>
            /// Makes the checks for enumerating children that match a pattern.
            /// </summary>
            /// <param name="searchPattern">The search pattern.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            private void PrepareEnumeration(string searchPattern, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                ValidatePattern(searchPattern, nameof(searchPattern));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExists();
            }

            /// <summary>
            /// Makes the checks for creating a child, including that the name isn't taken.
            /// </summary>
            /// <param name="name">The new child's name.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The new child's zip entry name.</returns>
            private string PrepareCreate(string name, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                ValidateName(name, nameof(name));
                cancellationToken.ThrowIfCancellationRequested();
                Owner.EnsureWritable();
                EnsureExists();
                var childName = ToEntryName(CombinePath(Path, name));
                if (Owner.EntryExists(childName))
                    throw NameTaken(name);
                return childName;
            }
        }
    }
}
