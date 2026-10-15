using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saraka.Abstractions;

namespace Saraka.Zip
{
    /// <summary>
    /// A file system backed by a zip file, using the zip support in System.IO.Compression.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zip entry names map directly to Saraka paths: the entry "data/file.txt" is the file
    /// "/data/file.txt". A directory exists if it has its own entry (a name ending in "/") or if
    /// any entry is inside it. Entries whose names can't be expressed as Saraka paths (for
    /// example, names starting with "/" or containing "..") are ignored.
    /// </para>
    /// <para>
    /// A writable file system holds the zip file open exclusively, and changes are written to it
    /// when the file system is disposed. Instances of this class and of the entries it returns
    /// are not thread-safe.
    /// </para>
    /// <para>
    /// .NET Standard 2.1 exposes only the synchronous zip APIs, so most operations do their work
    /// synchronously and return completed tasks. Copying data to and from streams is
    /// asynchronous, and on .NET 10 and later so is saving changes on disposal.
    /// </para>
    /// <para>
    /// Zip stores only a last-write time, in MS-DOS format: local time in two-second steps, from
    /// 1980 to 2107. Creation times read as the last-write time, and setting them does nothing.
    /// Setting a last-write time outside that range throws
    /// <see cref="ArgumentOutOfRangeException"/>. Only the MS-DOS attributes are stored, and the
    /// root directory's time and attributes can't be set.
    /// </para>
    /// <para>
    /// Names are case-sensitive. A name may not be empty, "." or "..", or contain "/", "\" or a
    /// control character.
    /// </para>
    /// </remarks>
    public sealed partial class FileSystem : IFileSystem
    {
        private const string RootPath = "/";
        private const char Separator = '/';
        private const int CopyBufferSize = 81920;

        // The low byte of a zip entry's external attributes holds its MS-DOS attributes.
        private const int DosAttributeMask = 0xFF;

        private static readonly char[] PatternSeparators = { '/', '\\' };

        private readonly ZipArchive archive;
        private readonly DateTime archiveLastWriteTimeUtc;
        private readonly DirectoryEntry root;
        private bool disposed;

        private FileSystem(ZipArchive archive, bool canWrite, DateTime archiveLastWriteTimeUtc)
        {
            this.archive = archive;
            this.archiveLastWriteTimeUtc = archiveLastWriteTimeUtc;
            CanWrite = canWrite;
            root = new DirectoryEntry(this, RootPath);
        }

        /// <summary>
        /// Opens a zip file as a file system.
        /// </summary>
        /// <param name="path">
        /// The path of the zip file. A relative path is resolved against the current directory.
        /// </param>
        /// <param name="canWrite">
        /// True to open the zip file for writing, creating it if it doesn't exist; false to open
        /// an existing zip file read-only.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The opened file system.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
        /// <exception cref="FileNotFoundException">
        /// <paramref name="canWrite"/> is false and the zip file doesn't exist, or
        /// <paramref name="path"/> is a directory.
        /// </exception>
        /// <exception cref="InvalidDataException">The file is not a valid zip file.</exception>
        public static Task<FileSystem> OpenAsync(string path, bool canWrite, CancellationToken cancellationToken = default)
        {
            return Complete(() =>
            {
                if (path == null)
                    throw new ArgumentNullException(nameof(path));
                if (path.Trim().Length == 0)
                    throw new ArgumentException("The path is empty.", nameof(path));
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                    throw new FileNotFoundException($"'{fullPath}' is a directory, not a zip file.", fullPath);
                var existed = File.Exists(fullPath);
                if (!canWrite && !existed)
                    throw new FileNotFoundException($"The zip file '{fullPath}' does not exist.", fullPath);

                var stream = canWrite
                    ? new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
                    : new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                try
                {
                    var lastWriteTimeUtc = File.GetLastWriteTimeUtc(fullPath);
                    var mode = canWrite ? ZipArchiveMode.Update : ZipArchiveMode.Read;
                    return new FileSystem(new ZipArchive(stream, mode, leaveOpen: false), canWrite, lastWriteTimeUtc);
                }
                catch
                {
                    stream.Dispose();
                    if (!existed)
                        TryDeleteFile(fullPath);
                    throw;
                }
            });
        }

        /// <inheritdoc/>
        public IDirectory Root => root;

        /// <inheritdoc/>
        /// <remarks>This is the value passed to <see cref="OpenAsync"/> and doesn't change.</remarks>
        public bool CanWrite { get; }

        /// <summary>
        /// Closes the file system, writing any changes to the zip file. Operations on the file
        /// system or its entries then throw <see cref="ObjectDisposedException"/>, and streams
        /// that are still open can no longer be used.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (disposed)
                return;
            disposed = true;

            // ZipArchive implements IAsyncDisposable from .NET 10, but .NET Standard 2.1 can't
            // see that, so check at run time.
            if (archive is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else
                archive.Dispose();
        }

        private bool IsReadMode => archive.Mode == ZipArchiveMode.Read;

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(FileSystem));
        }

        private void EnsureWritable()
        {
            if (!CanWrite)
                throw new InvalidOperationException("The file system is not writable.");
        }

        // Archive queries. Entry names here are zip names: no leading "/", and "" for the root.

        private static string ToEntryName(string path)
        {
            return path == RootPath ? string.Empty : path.Substring(1);
        }

        private static string DirectoryPrefix(string name)
        {
            return name.Length == 0 ? string.Empty : name + Separator;
        }

        private static string GetParentName(string name)
        {
            var index = name.LastIndexOf(Separator);
            return index < 0 ? string.Empty : name.Substring(0, index);
        }

        private ZipArchiveEntry FindFileEntry(string name)
        {
            return archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, name, StringComparison.Ordinal));
        }

        private ZipArchiveEntry FindDirectoryEntry(string name)
        {
            if (name.Length == 0)
                return null;
            var entryName = name + Separator;
            return archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, entryName, StringComparison.Ordinal));
        }

        private List<ZipArchiveEntry> FindEntriesWithPrefix(string prefix)
        {
            return archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }

        private bool FileExists(string name)
        {
            return FindFileEntry(name) != null;
        }

        private bool DirectoryExists(string name)
        {
            if (name.Length == 0)
                return true;
            var prefix = name + Separator;
            return archive.Entries.Any(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal));
        }

        private bool EntryExists(string name)
        {
            return FileExists(name) || DirectoryExists(name);
        }

        // Gives a directory its own entry if nothing else keeps it in existence, so that removing
        // the last thing in a directory doesn't remove the directory too.
        private void KeepDirectory(string name)
        {
            if (!DirectoryExists(name))
                archive.CreateEntry(name + Separator);
        }

        // Zip has no rename, so moving copies each entry to its new name and deletes the original.
        private void MoveEntries(string oldName, string newName, bool isDirectory)
        {
            if (isDirectory)
            {
                var oldPrefix = oldName + Separator;
                foreach (var entry in FindEntriesWithPrefix(oldPrefix))
                {
                    CopyEntry(entry, newName + Separator + entry.FullName.Substring(oldPrefix.Length));
                    entry.Delete();
                }
            }
            else
            {
                var entry = FindFileEntry(oldName);
                CopyEntry(entry, newName);
                entry.Delete();
            }
            KeepDirectory(GetParentName(oldName));
        }

        private void CopyEntry(ZipArchiveEntry source, string newName)
        {
            var target = archive.CreateEntry(newName);
            target.LastWriteTime = source.LastWriteTime;
            target.ExternalAttributes = source.ExternalAttributes;
            if (newName[newName.Length - 1] == Separator)
                return;
            using (var from = source.Open())
            using (var to = target.Open())
            {
                from.CopyTo(to, CopyBufferSize);
            }
        }

        // Validation

        private static bool IsValidName(string name)
        {
            if (name.Length == 0 || name == "." || name == "..")
                return false;
            foreach (var c in name)
            {
                if (c == '/' || c == '\\' || c < ' ')
                    return false;
            }
            return true;
        }

        private static void ValidateName(string name, string paramName)
        {
            if (name == null)
                throw new ArgumentNullException(paramName);
            if (!IsValidName(name))
                throw new ArgumentException(
                    $"'{name}' is not a valid name. Names may not be empty, \".\" or \"..\", or contain \"/\", \"\\\" or control characters.",
                    paramName);
        }

        private static void ValidatePattern(string searchPattern, string paramName)
        {
            if (searchPattern == null)
                throw new ArgumentNullException(paramName);
            if (searchPattern.IndexOfAny(PatternSeparators) >= 0)
                throw new ArgumentException("The search pattern contains a path separator.", paramName);
        }

        // Paths, attributes and times

        private static string CombinePath(string parentPath, string name)
        {
            return parentPath == RootPath ? RootPath + name : parentPath + Separator + name;
        }

        private static string GetParentPath(string path)
        {
            var index = path.LastIndexOf(Separator);
            return index == 0 ? RootPath : path.Substring(0, index);
        }

        private static bool IsSameOrDescendant(string path, string ancestorPath)
        {
            return string.Equals(path, ancestorPath, StringComparison.Ordinal)
                || path.StartsWith(ancestorPath + Separator, StringComparison.Ordinal);
        }

        private static FileAttributes FromExternalAttributes(int externalAttributes, bool isDirectory)
        {
            var attributes = (FileAttributes)(externalAttributes & DosAttributeMask);
            attributes &= ~(FileAttributes.Directory | FileAttributes.Normal);
            if (isDirectory)
                attributes |= FileAttributes.Directory;
            return attributes == 0 ? FileAttributes.Normal : attributes;
        }

        private static int ToExternalAttributes(int externalAttributes, FileAttributes attributes, bool isDirectory)
        {
            var dosAttributes = (int)attributes & DosAttributeMask & ~(int)(FileAttributes.Directory | FileAttributes.Normal);
            if (isDirectory)
                dosAttributes |= (int)FileAttributes.Directory;
            return (externalAttributes & ~DosAttributeMask) | dosAttributes;
        }

        // Unspecified is treated as UTC and Local is converted, as the contract requires. Zip
        // stores local times, so the value is given to the archive in local time.
        private static DateTimeOffset ToZipTime(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
            return new DateTimeOffset(utc).ToLocalTime();
        }

        private static DateTime FromZipTime(DateTimeOffset value)
        {
            return value.UtcDateTime;
        }

        private static InvalidOperationException NameTaken(string name)
        {
            return new InvalidOperationException($"An entry named '{name}' already exists.");
        }

        private static void TryDeleteFile(string fullPath)
        {
            try
            {
                File.Delete(fullPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        // Runs a synchronous operation and reports its outcome through a task, so that every
        // exception, including argument exceptions, is observed when the task is awaited.
        private static Task<T> Complete<T>(Func<T> operation)
        {
            try
            {
                return Task.FromResult(operation());
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        private static Task Complete(Action operation)
        {
            return Complete(() =>
            {
                operation();
                return true;
            });
        }
    }
}
