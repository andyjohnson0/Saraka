using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Saraka.Abstractions;


namespace Saraka.AndroidSaf
{
    public sealed partial class FileSystem
    {
        /// <summary>
        /// A folder in the document tree.
        /// </summary>
        private sealed class DirectoryEntry : Entry, IDirectory
        {
            /// <summary>
            /// Creates an entry for a folder.
            /// </summary>
            /// <param name="owner">The file system the folder belongs to.</param>
            /// <param name="parent">The folder it was found in, or null for the root.</param>
            /// <param name="name">The folder's display name, or "" for the root.</param>
            /// <param name="documentId">The provider's document ID.</param>
            public DirectoryEntry(FileSystem owner, DirectoryEntry? parent, string name, string documentId)
                : base(owner, parent, name, documentId)
            {
            }

            /// <inheritdoc/>
            protected override bool IsDirectory => true;

            /// <inheritdoc/>
            public Task<IEnumerable<IFileSystemEntry>> GetEntriesAsync(EntryOptions? options = null, CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    EnsureExists();
                    return (IEnumerable<IFileSystemEntry>)ListChildren(
                        files: true, directories: true, _ => true, options, cancellationToken);
                }, cancellationToken);
            }

            /// <inheritdoc/>
            public Task<IDirectory> GetDirectoryAsync(string name, CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    ValidateName(name, nameof(name));
                    EnsureExists();
                    var match = ListChildren(files: false, directories: true, NameIs(name), null, cancellationToken)
                        .FirstOrDefault();
                    return (IDirectory)(match ?? throw new DirectoryNotFoundException($"'{Path}' has no subdirectory named '{name}'."));
                }, cancellationToken);
            }

            /// <inheritdoc/>
            public Task<IFile> GetFileAsync(string name, CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    ValidateName(name, nameof(name));
                    EnsureExists();
                    var match = ListChildren(files: true, directories: false, NameIs(name), null, cancellationToken)
                        .FirstOrDefault();
                    return (IFile)(match ?? throw new FileNotFoundException($"'{Path}' has no file named '{name}'."));
                }, cancellationToken);
            }

            /// <inheritdoc/>
            public Task<IEnumerable<IFile>> EnumerateFilesAsync(string searchPattern = "*", EntryOptions? options = null, CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    ValidatePattern(searchPattern, nameof(searchPattern));
                    EnsureExists();
                    return (IEnumerable<IFile>)ListChildren(
                            files: true, directories: false, Matches(searchPattern), options, cancellationToken)
                        .Cast<IFile>()
                        .ToList();
                }, cancellationToken);
            }

            /// <inheritdoc/>
            public Task<IEnumerable<IDirectory>> EnumerateDirectoriesAsync(string searchPattern = "*", EntryOptions? options = null, CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    ValidatePattern(searchPattern, nameof(searchPattern));
                    EnsureExists();
                    return (IEnumerable<IDirectory>)ListChildren(
                            files: false, directories: true, Matches(searchPattern), options, cancellationToken)
                        .Cast<IDirectory>()
                        .ToList();
                }, cancellationToken);
            }

            /// <inheritdoc/>
            public Task<IDirectory> CreateDirectoryAsync(string name, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync<IDirectory>(cancellationToken, () => ValidateName(name, nameof(name)));
            }

            /// <inheritdoc/>
            public Task<IFile> CreateFileAsync(string name, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync<IFile>(cancellationToken, () => ValidateName(name, nameof(name)));
            }

            /// <inheritdoc/>
            public Task<IFile> CreateFileAsync(string name, Stream content, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync<IFile>(cancellationToken, () =>
                {
                    ValidateName(name, nameof(name));
                    ArgumentNullException.ThrowIfNull(content);
                    if (!content.CanRead)
                        throw new ArgumentException("The content stream can't be read.", nameof(content));
                });
            }

            /// <inheritdoc/>
            public Task DeleteAsync(bool recursive = false, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken);
            }

            /// <summary>
            /// Finds the directory at a path below this directory, which must be the root.
            /// </summary>
            /// <param name="path">An absolute path.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The directory.</returns>
            public Task<IDirectory> FindDirectoryAsync(string path, CancellationToken cancellationToken)
            {
                return RunAsync(() =>
                {
                    var names = SplitPath(path, nameof(path));
                    if (names.Length == 0)
                    {
                        EnsureExists();
                        return (IDirectory)this;
                    }
                    return (IDirectory)WalkTo(names, names.Length, path, cancellationToken);
                }, cancellationToken);
            }

            /// <summary>
            /// Finds the file at a path below this directory, which must be the root.
            /// </summary>
            /// <param name="path">An absolute path.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The file.</returns>
            public Task<IFile> FindFileAsync(string path, CancellationToken cancellationToken)
            {
                return RunAsync(() =>
                {
                    var names = SplitPath(path, nameof(path));
                    if (names.Length == 0)
                        throw new ArgumentException("\"/\" is the root directory, not a file.", nameof(path));
                    var parent = WalkTo(names, names.Length - 1, path, cancellationToken);
                    var file = parent.ListChildren(files: true, directories: false, NameIs(names[^1]), null, cancellationToken)
                        .FirstOrDefault();
                    return (IFile)(file ?? throw new FileNotFoundException($"There's no file at '{path}'."));
                }, cancellationToken);
            }

            /// <summary>
            /// Walks down from this directory through the named subdirectories, reading each one's
            /// contents once.
            /// </summary>
            /// <param name="names">The names along the path.</param>
            /// <param name="count">How many of the names to follow.</param>
            /// <param name="path">The whole path, for error messages.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The directory reached.</returns>
            /// <exception cref="DirectoryNotFoundException">A directory along the path doesn't exist.</exception>
            private DirectoryEntry WalkTo(string[] names, int count, string path, CancellationToken cancellationToken)
            {
                var current = this;
                for (var i = 0; i < count; i++)
                {
                    var next = current.ListChildren(files: false, directories: true, NameIs(names[i]), null, cancellationToken)
                        .FirstOrDefault();
                    current = (DirectoryEntry?)next
                        ?? throw new DirectoryNotFoundException($"There's no directory at '/{string.Join("/", names, 0, i + 1)}' (looking for '{path}').");
                }
                return current;
            }

            /// <inheritdoc/>
            protected override IOException NotFound(Exception? innerException) =>
                new DirectoryNotFoundException($"The directory '{Path}' does not exist.", innerException);

            /// <summary>
            /// Lists the direct children. Display names that aren't valid names are skipped, and when
            /// two children of the same kind have names that differ only in case, the first in the
            /// provider's order is kept. That happens before matching and before the options are
            /// applied, so lookups and every enumeration agree on which one is used. A child is hidden if
            /// its name starts with a period.
            /// </summary>
            /// <param name="files">True to include files.</param>
            /// <param name="directories">True to include directories.</param>
            /// <param name="match">Selects children by name.</param>
            /// <param name="options">Options that filter and sort the children, or null.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The children.</returns>
            private List<Entry> ListChildren(
                bool files, bool directories, Func<string, bool> match, EntryOptions? options, CancellationToken cancellationToken)
            {
                var documents = FromSource(() => Owner._source.ListChildren(DocumentId));
                var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var candidates = new List<DocumentEntry>();
                foreach (var document in documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = document.DisplayName;
                    if (!IsValidName(name) || !(document.IsDirectory ? seenFolders : seenFiles).Add(name))
                        continue;
                    if ((document.IsDirectory ? directories : files) && match(name))
                        candidates.Add(document);
                }

                return EntrySelection.Apply(
                        candidates,
                        options,
                        document => document.DisplayName,
                        document => document.IsDirectory,
                        document => document.DisplayName.StartsWith('.'),
                        document => FromUnixTime(document.LastModified))
                    .Select(document => document.IsDirectory
                        ? (Entry)new DirectoryEntry(Owner, this, document.DisplayName, document.DocumentId)
                        : new FileEntry(Owner, this, document.DisplayName, document.DocumentId))
                    .ToList();
            }

            /// <summary>
            /// Creates a matcher for a name, ignoring case.
            /// </summary>
            /// <param name="name">The name to match.</param>
            /// <returns>A function that tests a name.</returns>
            private static Func<string, bool> NameIs(string name)
            {
                return candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase);
            }

            /// <summary>
            /// Creates a matcher for a search pattern, ignoring case.
            /// </summary>
            /// <param name="searchPattern">The search pattern, using "*" and "?".</param>
            /// <returns>A function that tests a name against the pattern.</returns>
            private static Func<string, bool> Matches(string searchPattern)
            {
                return name => FileSystemName.MatchesSimpleExpression(searchPattern, name, ignoreCase: true);
            }
        }
    }
}
