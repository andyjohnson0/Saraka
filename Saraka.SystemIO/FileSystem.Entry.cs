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
            private string path;

            protected Entry(FileSystem owner, string path)
            {
                Owner = owner;
                this.path = path;
            }

            protected FileSystem Owner { get; }

            protected bool IsRoot => path == RootPath;

            protected string FullPath => Owner.ToFullPath(path);

            public string Name => IsRoot ? string.Empty : path.Substring(path.LastIndexOf(Separator) + 1);

            public string Path => path;

            // A new instance on each call, built from this entry's current path.
            public IDirectory Parent => IsRoot ? null : new DirectoryEntry(Owner, GetParentPath(path));

            public Task<FileAttributes> GetAttributesAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() => GetExistingInfo(cancellationToken).Attributes);
            }

            public Task<DateTime> GetCreationTimeUtcAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() => GetExistingInfo(cancellationToken).CreationTimeUtc);
            }

            public Task<DateTime> GetLastWriteTimeUtcAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() => GetExistingInfo(cancellationToken).LastWriteTimeUtc);
            }

            public Task SetAttributesAsync(FileAttributes attributes, CancellationToken cancellationToken = default)
            {
                return Complete(() => GetWritableInfo(cancellationToken).Attributes = attributes);
            }

            public Task SetCreationTimeUtcAsync(DateTime creationTimeUtc, CancellationToken cancellationToken = default)
            {
                return Complete(() => GetWritableInfo(cancellationToken).CreationTimeUtc = ToUtc(creationTimeUtc));
            }

            public Task SetLastWriteTimeUtcAsync(DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default)
            {
                return Complete(() => GetWritableInfo(cancellationToken).LastWriteTimeUtc = ToUtc(lastWriteTimeUtc));
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
                    var newFullPath = Owner.ToFullPath(newPath);
                    // A name that differs only in case may be this entry on a case-insensitive file
                    // system, so let the move itself decide.
                    if (!string.Equals(newName, Name, StringComparison.OrdinalIgnoreCase) && EntryExists(newFullPath))
                        throw NameTaken(newName);

                    MoveOnDisk(FullPath, newFullPath);
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
                    if (string.Equals(target.Path, GetParentPath(path), PathComparison))
                        return;

                    var newPath = CombinePath(target.Path, Name);
                    var newFullPath = Owner.ToFullPath(newPath);
                    if (EntryExists(newFullPath))
                        throw NameTaken(Name);

                    MoveOnDisk(FullPath, newFullPath);
                    path = newPath;
                });
            }

            public override string ToString() => path;

            /// <summary>
            /// Creates a System.IO object for this entry's current location.
            /// </summary>
            protected abstract FileSystemInfo CreateInfo();

            /// <summary>
            /// Creates the exception thrown when this entry doesn't exist on disk.
            /// </summary>
            protected abstract IOException NotFound();

            protected abstract void MoveOnDisk(string sourceFullPath, string destinationFullPath);

            protected void EnsureExists()
            {
                GetInfoIfExists();
            }

            private FileSystemInfo GetInfoIfExists()
            {
                var info = CreateInfo();
                if (!info.Exists)
                    throw NotFound();
                return info;
            }

            private FileSystemInfo GetExistingInfo(CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                return GetInfoIfExists();
            }

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
