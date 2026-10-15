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
            private string path;

            protected Entry(FileSystem owner, string path)
            {
                Owner = owner;
                this.path = path;
            }

            protected FileSystem Owner { get; }

            protected bool IsRoot => path == RootPath;

            /// <summary>The zip entry name for this entry's path, without any trailing "/".</summary>
            protected string EntryName => ToEntryName(path);

            public string Name => IsRoot ? string.Empty : path.Substring(path.LastIndexOf(Separator) + 1);

            public string Path => path;

            // A new instance on each call, built from this entry's current path.
            public IDirectory Parent => IsRoot ? null : new DirectoryEntry(Owner, GetParentPath(path));

            public Task<FileAttributes> GetAttributesAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareRead(cancellationToken);
                    return GetAttributesCore();
                });
            }

            // Zip doesn't record creation times, so the last-write time stands in for it.
            public Task<DateTime> GetCreationTimeUtcAsync(CancellationToken cancellationToken = default)
            {
                return GetLastWriteTimeUtcAsync(cancellationToken);
            }

            public Task<DateTime> GetLastWriteTimeUtcAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareRead(cancellationToken);
                    return GetLastWriteTimeCore();
                });
            }

            public Task SetAttributesAsync(FileAttributes attributes, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareWrite(cancellationToken);
                    SetAttributesCore(attributes);
                });
            }

            // Zip can't record a creation time, so after the usual checks this does nothing.
            public Task SetCreationTimeUtcAsync(DateTime creationTimeUtc, CancellationToken cancellationToken = default)
            {
                return Complete(() => PrepareWrite(cancellationToken));
            }

            public Task SetLastWriteTimeUtcAsync(DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareWrite(cancellationToken);
                    SetLastWriteTimeCore(ToZipTime(lastWriteTimeUtc));
                });
            }

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

                    var newPath = CombinePath(GetParentPath(path), newName);
                    if (Owner.EntryExists(ToEntryName(newPath)))
                        throw NameTaken(newName);

                    Owner.MoveEntries(EntryName, ToEntryName(newPath), this is IDirectory);
                    path = newPath;
                });
            }

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
                    if (this is IDirectory && IsSameOrDescendant(target.Path, path))
                        throw new InvalidOperationException("A directory can't be moved into itself or one of its descendants.");
                    EnsureExists();
                    target.EnsureExists();
                    if (target.Path == GetParentPath(path))
                        return;

                    var newPath = CombinePath(target.Path, Name);
                    if (Owner.EntryExists(ToEntryName(newPath)))
                        throw NameTaken(Name);

                    Owner.MoveEntries(EntryName, ToEntryName(newPath), this is IDirectory);
                    path = newPath;
                });
            }

            public override string ToString() => path;

            protected abstract bool Exists();

            /// <summary>
            /// Creates the exception thrown when this entry doesn't exist in the archive.
            /// </summary>
            protected abstract IOException NotFound();

            protected abstract FileAttributes GetAttributesCore();

            protected abstract void SetAttributesCore(FileAttributes attributes);

            protected abstract DateTime GetLastWriteTimeCore();

            protected abstract void SetLastWriteTimeCore(DateTimeOffset lastWriteTime);

            protected void EnsureExists()
            {
                if (!Exists())
                    throw NotFound();
            }

            protected void PrepareRead(CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExists();
            }

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
