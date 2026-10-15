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
                    PrepareRead(cancellationToken);
                    return (IEnumerable<IFileSystemEntry>)ListChildren(files: true, directories: true, _ => true, cancellationToken);
                });
            }

            public Task<IDirectory> GetDirectoryAsync(string name, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var childName = PrepareLookup(name, cancellationToken);
                    if (!Owner.DirectoryExists(childName))
                        throw new DirectoryNotFoundException($"'{Path}' has no subdirectory named '{name}'.");
                    return (IDirectory)new DirectoryEntry(Owner, CombinePath(Path, name));
                });
            }

            public Task<IFile> GetFileAsync(string name, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var childName = PrepareLookup(name, cancellationToken);
                    if (!Owner.FileExists(childName))
                        throw new FileNotFoundException($"'{Path}' has no file named '{name}'.");
                    return (IFile)new FileEntry(Owner, CombinePath(Path, name));
                });
            }

            public Task<IEnumerable<IFile>> EnumerateFilesAsync(string searchPattern = "*", CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareEnumeration(searchPattern, cancellationToken);
                    return (IEnumerable<IFile>)ListChildren(files: true, directories: false, Matcher(searchPattern), cancellationToken)
                        .Cast<IFile>()
                        .ToList();
                });
            }

            public Task<IEnumerable<IDirectory>> EnumerateDirectoriesAsync(string searchPattern = "*", CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareEnumeration(searchPattern, cancellationToken);
                    return (IEnumerable<IDirectory>)ListChildren(files: false, directories: true, Matcher(searchPattern), cancellationToken)
                        .Cast<IDirectory>()
                        .ToList();
                });
            }

            public Task<IDirectory> CreateDirectoryAsync(string name, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var childName = PrepareCreate(name, cancellationToken);
                    Owner.archive.CreateEntry(childName + Separator);
                    return (IDirectory)new DirectoryEntry(Owner, CombinePath(Path, name));
                });
            }

            public Task<IFile> CreateFileAsync(string name, CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    var childName = PrepareCreate(name, cancellationToken);
                    Owner.archive.CreateEntry(childName);
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
                var childName = PrepareCreate(name, cancellationToken);

                var entry = Owner.archive.CreateEntry(childName);
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

                    var prefix = DirectoryPrefix(EntryName);
                    var entries = Owner.FindEntriesWithPrefix(prefix);
                    if (!recursive && entries.Any(e => e.FullName.Length > prefix.Length))
                        throw new InvalidOperationException($"The directory '{Path}' is not empty.");
                    foreach (var entry in entries)
                        entry.Delete();
                    Owner.KeepDirectory(GetParentName(EntryName));
                });
            }

            protected override bool Exists() => Owner.DirectoryExists(EntryName);

            protected override IOException NotFound() =>
                new DirectoryNotFoundException($"The directory '{Path}' does not exist.");

            protected override FileAttributes GetAttributesCore()
            {
                var entry = Owner.FindDirectoryEntry(EntryName);
                return entry == null
                    ? FileAttributes.Directory
                    : FromExternalAttributes(entry.ExternalAttributes, isDirectory: true);
            }

            // The root has no entry of its own, so its attributes can't be changed.
            protected override void SetAttributesCore(FileAttributes attributes)
            {
                if (IsRoot)
                    return;
                var entry = GetOrCreateOwnEntry();
                entry.ExternalAttributes = ToExternalAttributes(entry.ExternalAttributes, attributes, isDirectory: true);
            }

            // A directory without its own entry reports the latest time of anything inside it.
            // An empty root reports the zip file's last-write time.
            protected override DateTime GetLastWriteTimeCore()
            {
                var entry = Owner.FindDirectoryEntry(EntryName);
                if (entry != null)
                    return FromZipTime(entry.LastWriteTime);

                var contents = Owner.FindEntriesWithPrefix(DirectoryPrefix(EntryName));
                return contents.Count == 0
                    ? Owner.archiveLastWriteTimeUtc
                    : contents.Max(e => FromZipTime(e.LastWriteTime));
            }

            // The root has no entry of its own, so its time can't be changed.
            protected override void SetLastWriteTimeCore(DateTimeOffset lastWriteTime)
            {
                if (IsRoot)
                    return;
                GetOrCreateOwnEntry().LastWriteTime = lastWriteTime;
            }

            private ZipArchiveEntry GetOrCreateOwnEntry()
            {
                return Owner.FindDirectoryEntry(EntryName) ?? Owner.archive.CreateEntry(EntryName + Separator);
            }

            // Lists the direct children of this directory. A child directory may have its own
            // entry ("a/") or exist only because entries are inside it ("a/b.txt").
            private List<IFileSystemEntry> ListChildren(
                bool files, bool directories, Func<string, bool> match, CancellationToken cancellationToken)
            {
                var prefix = DirectoryPrefix(EntryName);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var children = new List<IFileSystemEntry>();
                foreach (var entry in Owner.archive.Entries)
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

            private static Func<string, bool> Matcher(string searchPattern)
            {
                return name => FileSystemName.MatchesSimpleExpression(searchPattern, name, ignoreCase: false);
            }

            private string PrepareLookup(string name, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                ValidateName(name, nameof(name));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExists();
                return ToEntryName(CombinePath(Path, name));
            }

            private void PrepareEnumeration(string searchPattern, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                ValidatePattern(searchPattern, nameof(searchPattern));
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExists();
            }

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
