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
            public Task<IEnumerable<IFileSystemEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    EnsureExists();
                    return (IEnumerable<IFileSystemEntry>)ListChildren(
                        files: true, directories: true, _ => true, cancellationToken);
                }, cancellationToken);
            }

            /// <inheritdoc/>
            public Task<IDirectory> GetDirectoryAsync(string name, CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    ValidateName(name, nameof(name));
                    EnsureExists();
                    var match = ListChildren(files: false, directories: true, NameIs(name), cancellationToken)
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
                    var match = ListChildren(files: true, directories: false, NameIs(name), cancellationToken)
                        .FirstOrDefault();
                    return (IFile)(match ?? throw new FileNotFoundException($"'{Path}' has no file named '{name}'."));
                }, cancellationToken);
            }

            /// <inheritdoc/>
            public Task<IEnumerable<IFile>> EnumerateFilesAsync(string searchPattern = "*", CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    ValidatePattern(searchPattern, nameof(searchPattern));
                    EnsureExists();
                    return (IEnumerable<IFile>)ListChildren(
                            files: true, directories: false, Matches(searchPattern), cancellationToken)
                        .Cast<IFile>()
                        .ToList();
                }, cancellationToken);
            }

            /// <inheritdoc/>
            public Task<IEnumerable<IDirectory>> EnumerateDirectoriesAsync(string searchPattern = "*", CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    ValidatePattern(searchPattern, nameof(searchPattern));
                    EnsureExists();
                    return (IEnumerable<IDirectory>)ListChildren(
                            files: false, directories: true, Matches(searchPattern), cancellationToken)
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

            /// <inheritdoc/>
            protected override IOException NotFound(Exception? innerException) =>
                new DirectoryNotFoundException($"The directory '{Path}' does not exist.", innerException);

            /// <summary>
            /// Lists the direct children that the options allow, in the order the options give. When
            /// two children of the same kind have names that differ only in case, the first is kept.
            /// That happens before matching, so lookups and listings agree on which one is used.
            /// </summary>
            /// <param name="files">True to include files.</param>
            /// <param name="directories">True to include directories.</param>
            /// <param name="match">Selects children by name.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The children, directories first.</returns>
            private List<Entry> ListChildren(
                bool files, bool directories, Func<string, bool> match, CancellationToken cancellationToken)
            {
                var listing = FromSource(() => Owner._source.ListChildren(DocumentId));
                var children = new List<Entry>();

                if (directories)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var folder in DocumentSelection.SelectFolders(
                        listing, Owner._sortField, Owner._sortOrder, Owner._includeHiddenFolders))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (IsValidName(folder.DisplayName) && seen.Add(folder.DisplayName) && match(folder.DisplayName))
                            children.Add(new DirectoryEntry(Owner, this, folder.DisplayName, folder.DocumentId));
                    }
                }

                if (files)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var file in DocumentSelection.Select(
                        listing, Owner._fileTypes, Owner._sortField, Owner._sortOrder, Owner._includeHiddenFiles))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (IsValidName(file.DisplayName) && seen.Add(file.DisplayName) && match(file.DisplayName))
                            children.Add(new FileEntry(Owner, this, file.DisplayName, file.DocumentId));
                    }
                }

                return children;
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
