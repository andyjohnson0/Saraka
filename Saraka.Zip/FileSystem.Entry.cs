using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Saraka.Abstractions;

namespace Saraka.Zip
{
    public sealed partial class FileSystem
    {
        /// <summary>
        /// The behaviour shared by files and directories. An entry holds only its Saraka path, so
        /// it reflects whatever is in the archive at that path when each operation runs.
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

            /// <summary>The zip entry name for this entry's path, without any trailing "/".</summary>
            protected string EntryName => ToEntryName(_path);

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
                return Complete(() =>
                {
                    PrepareRead(cancellationToken);
                    return GetAttributesCore();
                });
            }

            /// <inheritdoc/>
            /// <remarks>Zip doesn't record creation times, so the last-write time stands in for it.</remarks>
            public Task<DateTime> GetCreationTimeUtcAsync(CancellationToken cancellationToken = default)
            {
                return GetLastWriteTimeUtcAsync(cancellationToken);
            }

            /// <inheritdoc/>
            public Task<DateTime> GetLastWriteTimeUtcAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareRead(cancellationToken);
                    return GetLastWriteTimeCore();
                });
            }

            /// <inheritdoc/>
            public Task SetAttributesAsync(FileAttributes attributes, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareWrite(cancellationToken);
                    SetAttributesCore(attributes);
                });
            }

            /// <inheritdoc/>
            /// <remarks>Zip can't record a creation time, so after the usual checks this does nothing.</remarks>
            public Task SetCreationTimeUtcAsync(DateTime creationTimeUtc, CancellationToken cancellationToken = default)
            {
                return Complete(() => PrepareWrite(cancellationToken));
            }

            /// <inheritdoc/>
            public Task SetLastWriteTimeUtcAsync(DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareWrite(cancellationToken);
                    SetLastWriteTimeCore(ToZipTime(lastWriteTimeUtc));
                });
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
                    if (Owner.EntryExists(ToEntryName(newPath)))
                        throw NameTaken(newName);

                    Owner.MoveEntries(EntryName, ToEntryName(newPath), this is IDirectory);
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
                    if (target.Path == GetParentPath(_path))
                        return;

                    var newPath = CombinePath(target.Path, Name);
                    if (Owner.EntryExists(ToEntryName(newPath)))
                        throw NameTaken(Name);

                    Owner.MoveEntries(EntryName, ToEntryName(newPath), this is IDirectory);
                    _path = newPath;
                });
            }

            /// <summary>
            /// Returns the entry's path.
            /// </summary>
            /// <returns>The entry's Saraka path.</returns>
            public override string ToString() => _path;

            /// <summary>
            /// Determines whether this entry exists in the archive.
            /// </summary>
            /// <returns>True if the entry exists.</returns>
            protected abstract bool Exists();

            /// <summary>
            /// Creates the exception thrown when this entry doesn't exist in the archive.
            /// </summary>
            /// <returns>A <see cref="FileNotFoundException"/> or <see cref="DirectoryNotFoundException"/>.</returns>
            protected abstract IOException NotFound();

            /// <summary>
            /// Reads this entry's attributes, after the checks have been made.
            /// </summary>
            /// <returns>The attributes.</returns>
            protected abstract FileAttributes GetAttributesCore();

            /// <summary>
            /// Stores this entry's attributes, after the checks have been made.
            /// </summary>
            /// <param name="attributes">The attributes to store.</param>
            protected abstract void SetAttributesCore(FileAttributes attributes);

            /// <summary>
            /// Reads this entry's last-write time, after the checks have been made.
            /// </summary>
            /// <returns>The last-write time, in UTC.</returns>
            protected abstract DateTime GetLastWriteTimeCore();

            /// <summary>
            /// Stores this entry's last-write time, after the checks have been made.
            /// </summary>
            /// <param name="lastWriteTime">The time, as a local time for the archive.</param>
            protected abstract void SetLastWriteTimeCore(DateTimeOffset lastWriteTime);

            /// <summary>
            /// Throws if this entry doesn't exist in the archive.
            /// </summary>
            /// <exception cref="IOException">The entry doesn't exist; see <see cref="NotFound"/>.</exception>
            protected void EnsureExists()
            {
                if (!Exists())
                    throw NotFound();
            }

            /// <summary>
            /// Makes the checks for reading this entry.
            /// </summary>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            protected void PrepareRead(CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExists();
            }

            /// <summary>
            /// Makes the checks for changing this entry.
            /// </summary>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            protected void PrepareWrite(CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                Owner.EnsureWritable();
                EnsureExists();
            }
        }
    }
}
