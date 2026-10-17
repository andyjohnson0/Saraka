using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Saraka.Abstractions;

namespace Saraka.SystemIO
{
    public sealed partial class FileSystem
    {
        /// <summary>
        /// The behaviour shared by files and directories. An entry holds only its Saraka path, so
        /// it reflects whatever is on disk at that path when each operation runs.
        /// </summary>
        private abstract class Entry : IFileSystemEntry
        {
            private string _path;

            /// <summary>
            /// Creates an entry for a Saraka path.
            /// </summary>
            /// <param name="owner">The file system the entry belongs to.</param>
            /// <param name="path">The entry's Saraka path.</param>
            protected Entry(FileSystem owner, string path)
            {
                Owner = owner;
                _path = path;
            }

            /// <summary>
            /// The file system the entry belongs to.
            /// </summary>
            protected FileSystem Owner { get; }

            /// <summary>
            /// True if this entry is the root directory.
            /// </summary>
            protected bool IsRoot => _path == RootPath;

            /// <summary>
            /// The host path of this entry's current location.
            /// </summary>
            protected string FullPath => Owner.ToFullPath(_path);

            /// <inheritdoc/>
            public string Name => IsRoot ? string.Empty : _path.Substring(_path.LastIndexOf(Separator) + 1);

            /// <inheritdoc/>
            public string Path => _path;

            /// <inheritdoc/>
            /// <remarks>A new instance on each call, built from this entry's current path.</remarks>
            public IDirectory? Parent => IsRoot ? null : new DirectoryEntry(Owner, GetParentPath(_path));

            /// <inheritdoc/>
            public Task<FileAttributes> GetAttributesAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() => GetExistingInfo(cancellationToken).Attributes);
            }

            /// <inheritdoc/>
            public Task<DateTime> GetCreationTimeUtcAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() => GetExistingInfo(cancellationToken).CreationTimeUtc);
            }

            /// <inheritdoc/>
            public Task<DateTime> GetLastWriteTimeUtcAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() => GetExistingInfo(cancellationToken).LastWriteTimeUtc);
            }

            /// <inheritdoc/>
            public Task SetAttributesAsync(FileAttributes attributes, CancellationToken cancellationToken = default)
            {
                return Complete(() => GetWritableInfo(cancellationToken).Attributes = attributes);
            }

            /// <inheritdoc/>
            public Task SetCreationTimeUtcAsync(DateTime creationTimeUtc, CancellationToken cancellationToken = default)
            {
                return Complete(() => GetWritableInfo(cancellationToken).CreationTimeUtc = ToUtc(creationTimeUtc));
            }

            /// <inheritdoc/>
            public Task SetLastWriteTimeUtcAsync(DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default)
            {
                return Complete(() => GetWritableInfo(cancellationToken).LastWriteTimeUtc = ToUtc(lastWriteTimeUtc));
            }

            /// <inheritdoc/>
            public Task RenameAsync(string newName, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    Owner.ThrowIfDisposed();
                    ValidateName(newName, nameof(newName));
                    cancellationToken.ThrowIfCancellationRequested();
                    Owner.EnsureWritable();
                    if (IsRoot)
                        throw new InvalidOperationException("The root directory can't be renamed.");
                    EnsureExists();
                    if (newName == Name)
                        return;

                    var newPath = CombinePath(GetParentPath(_path), newName);
                    var newFullPath = Owner.ToFullPath(newPath);
                    // A name that differs only in case may be this entry on a case-insensitive file
                    // system, so let the move itself decide.
                    if (!string.Equals(newName, Name, StringComparison.OrdinalIgnoreCase) && EntryExists(newFullPath))
                        throw NameTaken(newName);

                    MoveOnDisk(FullPath, newFullPath);
                    _path = newPath;
                });
            }

            /// <inheritdoc/>
            public Task MoveToAsync(IDirectory destination, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    Owner.ThrowIfDisposed();
                    if (destination == null)
                        throw new ArgumentNullException(nameof(destination));
                    cancellationToken.ThrowIfCancellationRequested();
                    Owner.EnsureWritable();
                    if (!(destination is DirectoryEntry target) || target.Owner != Owner)
                        throw new InvalidOperationException("The destination belongs to a different file system.");
                    if (IsRoot)
                        throw new InvalidOperationException("The root directory can't be moved.");
                    if (this is IDirectory && IsSameOrDescendant(target.Path, _path))
                        throw new InvalidOperationException("A directory can't be moved into itself or one of its descendants.");
                    EnsureExists();
                    target.EnsureExists();
                    if (string.Equals(target.Path, GetParentPath(_path), PathComparison))
                        return;

                    var newPath = CombinePath(target.Path, Name);
                    var newFullPath = Owner.ToFullPath(newPath);
                    if (EntryExists(newFullPath))
                        throw NameTaken(Name);

                    MoveOnDisk(FullPath, newFullPath);
                    _path = newPath;
                });
            }

            /// <summary>
            /// Returns the entry's path.
            /// </summary>
            /// <returns>The entry's Saraka path.</returns>
            public override string ToString() => _path;

            /// <summary>
            /// Creates a System.IO object for this entry's current location.
            /// </summary>
            /// <returns>A <see cref="FileInfo"/> or <see cref="DirectoryInfo"/>.</returns>
            protected abstract FileSystemInfo CreateInfo();

            /// <summary>
            /// Creates the exception thrown when this entry doesn't exist on disk.
            /// </summary>
            /// <returns>A <see cref="FileNotFoundException"/> or <see cref="DirectoryNotFoundException"/>.</returns>
            protected abstract IOException NotFound();

            /// <summary>
            /// Moves this entry on disk.
            /// </summary>
            /// <param name="sourceFullPath">The entry's current host path.</param>
            /// <param name="destinationFullPath">The host path to move it to.</param>
            protected abstract void MoveOnDisk(string sourceFullPath, string destinationFullPath);

            /// <summary>
            /// Throws if this entry doesn't exist on disk.
            /// </summary>
            /// <exception cref="IOException">The entry doesn't exist; see <see cref="NotFound"/>.</exception>
            protected void EnsureExists()
            {
                GetInfoIfExists();
            }

            /// <summary>
            /// Gets the System.IO object for this entry, if it exists.
            /// </summary>
            /// <returns>The System.IO object.</returns>
            /// <exception cref="IOException">The entry doesn't exist; see <see cref="NotFound"/>.</exception>
            private FileSystemInfo GetInfoIfExists()
            {
                var info = CreateInfo();
                if (!info.Exists)
                    throw NotFound();
                return info;
            }

            /// <summary>
            /// Makes the checks for a read, then gets the System.IO object for this entry.
            /// </summary>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The System.IO object.</returns>
            private FileSystemInfo GetExistingInfo(CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                return GetInfoIfExists();
            }

            /// <summary>
            /// Makes the checks for a write, then gets the System.IO object for this entry.
            /// </summary>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The System.IO object.</returns>
            private FileSystemInfo GetWritableInfo(CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                Owner.EnsureWritable();
                return GetInfoIfExists();
            }
        }
    }
}
