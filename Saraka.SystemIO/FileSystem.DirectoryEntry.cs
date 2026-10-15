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
        private sealed class DirectoryEntry : Entry, IDirectory
        {
            public DirectoryEntry(FileSystem owner, string path)
                : base(owner, path)
            {
            }

            public Task<IEnumerable<IFileSystemEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    Owner.ThrowIfDisposed();
                    cancellationToken.ThrowIfCancellationRequested();
                    EnsureExists();
                    var entries = new List<IFileSystemEntry>();
                    foreach (var info in new DirectoryInfo(FullPath).EnumerateFileSystemInfos("*", ListingOptions))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var childPath = CombinePath(Path, info.Name);
                        entries.Add(info is DirectoryInfo
                            ? (IFileSystemEntry)new DirectoryEntry(Owner, childPath)
                            : new FileEntry(Owner, childPath));
                    }
                    return (IEnumerable<IFileSystemEntry>)entries;
                });
            }

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

            public Task<IEnumerable<IFile>> EnumerateFilesAsync(string searchPattern = "*", CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var info = PrepareEnumeration(searchPattern, cancellationToken);
                    return (IEnumerable<IFile>)info.EnumerateFiles(searchPattern, ListingOptions)
                        .Select(file => (IFile)new FileEntry(Owner, CombinePath(Path, file.Name)))
                        .ToList();
                });
            }

            public Task<IEnumerable<IDirectory>> EnumerateDirectoriesAsync(string searchPattern = "*", CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var info = PrepareEnumeration(searchPattern, cancellationToken);
                    return (IEnumerable<IDirectory>)info.EnumerateDirectories(searchPattern, ListingOptions)
                        .Select(directory => (IDirectory)new DirectoryEntry(Owner, CombinePath(Path, directory.Name)))
                        .ToList();
                });
            }

            public Task<IDirectory> CreateDirectoryAsync(string name, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var fullPath = PrepareCreate(name, cancellationToken);
                    Directory.CreateDirectory(fullPath);
                    return (IDirectory)new DirectoryEntry(Owner, CombinePath(Path, name));
                });
            }

            public Task<IFile> CreateFileAsync(string name, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var fullPath = PrepareCreate(name, cancellationToken);
                    OpenNewFile(fullPath, name).Dispose();
                    return (IFile)new FileEntry(Owner, CombinePath(Path, name));
                });
            }

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

            protected override FileSystemInfo CreateInfo() => new DirectoryInfo(FullPath);

            protected override IOException NotFound() =>
                new DirectoryNotFoundException($"The directory '{Path}' does not exist.");

            protected override void MoveOnDisk(string sourceFullPath, string destinationFullPath) =>
                Directory.Move(sourceFullPath, destinationFullPath);

            private string PrepareLookup(string name, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                ValidateName(name, nameof(name));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExists();
                return Owner.ToFullPath(CombinePath(Path, name));
            }

            private DirectoryInfo PrepareEnumeration(string searchPattern, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                ValidatePattern(searchPattern, nameof(searchPattern));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExists();
                return new DirectoryInfo(FullPath);
            }

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

            private static FileStream OpenNewFile(string fullPath, string name)
            {
                try
                {
                    // CreateNew fails rather than truncating if the file appeared since the check.
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
