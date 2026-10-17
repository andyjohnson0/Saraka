using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saraka.Abstractions;

namespace Saraka.SystemIO
{
    public sealed partial class FileSystem
    {
        /// <summary>
        /// A directory in the file system.
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
                return Complete(() =>
                {
                    Owner.ThrowIfDisposed();
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureExists();
                    var infos = new DirectoryInfo(FullPath).EnumerateFileSystemInfos("*", ListingOptions);
                    return (IEnumerable<IFileSystemEntry>)Select(infos, options, cancellationToken)
                        .Select(info => info is DirectoryInfo
                            ? (IFileSystemEntry)new DirectoryEntry(Owner, CombinePath(Path, info.Name))
                            : new FileEntry(Owner, CombinePath(Path, info.Name)))
                        .ToList();
                });
            }

            /// <inheritdoc/>
            public Task<IDirectory> GetDirectoryAsync(string name, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var fullPath = PrepareLookup(name, cancellationToken);
                    if (!Directory.Exists(fullPath))
                        throw new DirectoryNotFoundException($"'{Path}' has no subdirectory named '{name}'.");
                    return (IDirectory)new DirectoryEntry(Owner, CombinePath(Path, name));
                });
            }

            /// <inheritdoc/>
            public Task<IFile> GetFileAsync(string name, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var fullPath = PrepareLookup(name, cancellationToken);
                    if (!File.Exists(fullPath))
                        throw new FileNotFoundException($"'{Path}' has no file named '{name}'.");
                    return (IFile)new FileEntry(Owner, CombinePath(Path, name));
                });
            }

            /// <inheritdoc/>
            public Task<IEnumerable<IFile>> EnumerateFilesAsync(string searchPattern = "*", EntryOptions? options = null, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var info = PrepareEnumeration(searchPattern, cancellationToken);
                    return (IEnumerable<IFile>)Select(info.EnumerateFiles(searchPattern, ListingOptions), options, cancellationToken)
                        .Select(file => (IFile)new FileEntry(Owner, CombinePath(Path, file.Name)))
                        .ToList();
                });
            }

            /// <inheritdoc/>
            public Task<IEnumerable<IDirectory>> EnumerateDirectoriesAsync(string searchPattern = "*", EntryOptions? options = null, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var info = PrepareEnumeration(searchPattern, cancellationToken);
                    return (IEnumerable<IDirectory>)Select(info.EnumerateDirectories(searchPattern, ListingOptions), options, cancellationToken)
                        .Select(directory => (IDirectory)new DirectoryEntry(Owner, CombinePath(Path, directory.Name)))
                        .ToList();
                });
            }

            /// <inheritdoc/>
            public Task<IDirectory> CreateDirectoryAsync(string name, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var fullPath = PrepareCreate(name, cancellationToken);
                    Directory.CreateDirectory(fullPath);
                    return (IDirectory)new DirectoryEntry(Owner, CombinePath(Path, name));
                });
            }

            /// <inheritdoc/>
            public Task<IFile> CreateFileAsync(string name, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var fullPath = PrepareCreate(name, cancellationToken);
                    OpenNewFile(fullPath, name).Dispose();
                    return (IFile)new FileEntry(Owner, CombinePath(Path, name));
                });
            }

            /// <inheritdoc/>
            public async Task<IFile> CreateFileAsync(string name, Stream content, CancellationToken cancellationToken = default)
            {
                Owner.ThrowIfDisposed();
                ValidateName(name, nameof(name));
                if (content == null)
                    throw new ArgumentNullException(nameof(content));
                if (!content.CanRead)
                    throw new ArgumentException("The content stream can't be read.", nameof(content));
                var fullPath = PrepareCreate(name, cancellationToken);

                var stream = OpenNewFile(fullPath, name);
                try
                {
                    using (stream)
                    {
                        await content.CopyToAsync(stream, CopyBufferSize, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Don't leave a partly written file behind.
                    TryDeleteFile(fullPath);
                    throw;
                }
                return new FileEntry(Owner, CombinePath(Path, name));
            }

            /// <inheritdoc/>
            public Task DeleteAsync(bool recursive = false, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    Owner.ThrowIfDisposed();
                    cancellationToken.ThrowIfCancellationRequested();
                    Owner.EnsureWritable();
                    if (IsRoot)
                        throw new InvalidOperationException("The root directory can't be deleted.");
                    EnsureExists();
                    if (!recursive && Directory.EnumerateFileSystemEntries(FullPath).Any())
                        throw new InvalidOperationException($"The directory '{Path}' is not empty.");
                    Directory.Delete(FullPath, recursive);
                });
            }

            /// <inheritdoc/>
            protected override FileSystemInfo CreateInfo() => new DirectoryInfo(FullPath);

            /// <inheritdoc/>
            protected override IOException NotFound() =>
                new DirectoryNotFoundException($"The directory '{Path}' does not exist.");

            /// <inheritdoc/>
            protected override void MoveOnDisk(string sourceFullPath, string destinationFullPath) =>
                Directory.Move(sourceFullPath, destinationFullPath);

            /// <summary>
            /// Leaves out names that can't be presented, then applies the options. An entry is
            /// hidden if the file system gives it the Hidden attribute, which .NET also reports for
            /// names starting with "." on Linux and macOS.
            /// </summary>
            /// <typeparam name="T">The kind of System.IO object.</typeparam>
            /// <param name="infos">The directory's contents, from System.IO.</param>
            /// <param name="options">The options, or null.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The selected contents.</returns>
            private static List<T> Select<T>(IEnumerable<T> infos, EntryOptions? options, CancellationToken cancellationToken)
                where T : FileSystemInfo
            {
                var presentable = infos.Where(info =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return IsPresentableName(info.Name);
                });
                return EntrySelection.Apply(
                    presentable,
                    options,
                    info => info.Name,
                    info => info is DirectoryInfo,
                    info => (info.Attributes & FileAttributes.Hidden) != 0,
                    info => info.LastWriteTimeUtc);
            }

            /// <summary>
            /// Makes the checks for looking up a child by name.
            /// </summary>
            /// <param name="name">The child's name.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The child's host path.</returns>
            private string PrepareLookup(string name, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                ValidateName(name, nameof(name));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExists();
                return Owner.ToFullPath(CombinePath(Path, name));
            }

            /// <summary>
            /// Makes the checks for enumerating children that match a pattern.
            /// </summary>
            /// <param name="searchPattern">The search pattern.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The System.IO object for this directory.</returns>
            private DirectoryInfo PrepareEnumeration(string searchPattern, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                ValidatePattern(searchPattern, nameof(searchPattern));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExists();
                return new DirectoryInfo(FullPath);
            }

            /// <summary>
            /// Makes the checks for creating a child, including that the name isn't taken.
            /// </summary>
            /// <param name="name">The new child's name.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The new child's host path.</returns>
            private string PrepareCreate(string name, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                ValidateName(name, nameof(name));
                cancellationToken.ThrowIfCancellationRequested();
                Owner.EnsureWritable();
                EnsureExists();
                var fullPath = Owner.ToFullPath(CombinePath(Path, name));
                if (EntryExists(fullPath))
                    throw NameTaken(name);
                return fullPath;
            }

            /// <summary>
            /// Creates a new, empty file and opens it for writing. Unlike creating with truncation,
            /// this fails if the file appeared since the name was checked.
            /// </summary>
            /// <param name="fullPath">The new file's host path.</param>
            /// <param name="name">The new file's name, for the error message.</param>
            /// <returns>A writable stream over the new file.</returns>
            /// <exception cref="InvalidOperationException">An entry with that name now exists.</exception>
            private static FileStream OpenNewFile(string fullPath, string name)
            {
                try
                {
                    return new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        StreamBufferSize, FileOptions.Asynchronous);
                }
                catch (IOException) when (EntryExists(fullPath))
                {
                    throw NameTaken(name);
                }
            }
        }
    }
}
