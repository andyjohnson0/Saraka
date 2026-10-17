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

        /// <summary>
        /// The low byte of a zip entry's external attributes, which holds its MS-DOS attributes.
        /// </summary>
        private const int DosAttributeMask = 0xFF;

        private static readonly char[] PatternSeparators = { '/', '\\' };

        private readonly ZipArchive _archive;
        private readonly DateTime _archiveLastWriteTimeUtc;
        private readonly DirectoryEntry _root;
        private bool _disposed;

        /// <summary>
        /// Creates a file system over an open archive.
        /// </summary>
        /// <param name="archive">The archive. The file system disposes it.</param>
        /// <param name="canWrite">True if the archive is open for updating.</param>
        /// <param name="archiveLastWriteTimeUtc">
        /// The zip file's last-write time, reported by an empty root directory.
        /// </param>
        /// <param name="zipPath">The zip file's full path.</param>
        private FileSystem(ZipArchive archive, bool canWrite, DateTime archiveLastWriteTimeUtc, string zipPath)
        {
            _archive = archive;
            DisplayName = Path.GetFileName(zipPath);
            _archiveLastWriteTimeUtc = archiveLastWriteTimeUtc;
            CanWrite = canWrite;
            _root = new DirectoryEntry(this, RootPath);
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
                    return new FileSystem(new ZipArchive(stream, mode, leaveOpen: false), canWrite, lastWriteTimeUtc, fullPath);
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
        public IDirectory Root => _root;

        /// <inheritdoc/>
        /// <remarks>This is the value passed to <see cref="OpenAsync"/> and doesn't change.</remarks>
        public bool CanWrite { get; }

        /// <inheritdoc/>
        /// <remarks>Zip entry names are case-sensitive, so this is <see cref="StringComparer.Ordinal"/>.</remarks>
        public StringComparer NameComparer => StringComparer.Ordinal;

        /// <inheritdoc/>
        /// <remarks>The zip file's name, such as "photos.zip".</remarks>
        public string DisplayName { get; }

        /// <summary>
        /// Closes the file system, writing any changes to the zip file. Operations on the file
        /// system or its entries then throw <see cref="ObjectDisposedException"/>, and streams
        /// that are still open can no longer be used.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;

            // ZipArchive implements IAsyncDisposable from .NET 10, but .NET Standard 2.1 can't
            // see that, so check at run time.
            if (_archive is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else
                _archive.Dispose();
        }

        /// <summary>
        /// True if the archive was opened read-only.
        /// </summary>
        private bool IsReadMode => _archive.Mode == ZipArchiveMode.Read;

        /// <summary>
        /// Throws if the file system has been disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The file system has been disposed.</exception>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FileSystem));
        }

        /// <summary>
        /// Throws if the file system is read-only.
        /// </summary>
        /// <exception cref="InvalidOperationException">The file system is not writable.</exception>
        private void EnsureWritable()
        {
            if (!CanWrite)
                throw new InvalidOperationException("The file system is not writable.");
        }

        /// <summary>
        /// Converts a Saraka path to a zip entry name: no leading "/", and "" for the root.
        /// </summary>
        /// <param name="path">The Saraka path.</param>
        /// <returns>The zip entry name.</returns>
        private static string ToEntryName(string path)
        {
            return path == RootPath ? string.Empty : path.Substring(1);
        }

        /// <summary>
        /// Gets the prefix that the names of a directory's contents start with.
        /// </summary>
        /// <param name="name">The directory's zip entry name, without a trailing "/".</param>
        /// <returns>The name followed by "/", or "" for the root.</returns>
        private static string DirectoryPrefix(string name)
        {
            return name.Length == 0 ? string.Empty : name + Separator;
        }

        /// <summary>
        /// Gets the zip entry name of an entry's parent directory.
        /// </summary>
        /// <param name="name">The entry's zip entry name.</param>
        /// <returns>The parent's zip entry name, or "" for the root.</returns>
        private static string GetParentName(string name)
        {
            var index = name.LastIndexOf(Separator);
            return index < 0 ? string.Empty : name.Substring(0, index);
        }

        /// <summary>
        /// Finds the zip entry for a file.
        /// </summary>
        /// <param name="name">The file's zip entry name.</param>
        /// <returns>The first entry with that exact name, or null if there is none.</returns>
        private ZipArchiveEntry? FindFileEntry(string name)
        {
            return _archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, name, StringComparison.Ordinal));
        }

        /// <summary>
        /// Finds a directory's own zip entry (its name followed by "/").
        /// </summary>
        /// <param name="name">The directory's zip entry name, without a trailing "/".</param>
        /// <returns>The directory's own entry, or null if it has none or is the root.</returns>
        private ZipArchiveEntry? FindDirectoryEntry(string name)
        {
            if (name.Length == 0)
                return null;
            var entryName = name + Separator;
            return _archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, entryName, StringComparison.Ordinal));
        }

        /// <summary>
        /// Finds every zip entry whose name starts with a prefix.
        /// </summary>
        /// <param name="prefix">The prefix.</param>
        /// <returns>The matching entries.</returns>
        private List<ZipArchiveEntry> FindEntriesWithPrefix(string prefix)
        {
            return _archive.Entries.Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }

        /// <summary>
        /// Determines whether a file exists.
        /// </summary>
        /// <param name="name">The file's zip entry name.</param>
        /// <returns>True if the archive has an entry with that exact name.</returns>
        private bool FileExists(string name)
        {
            return FindFileEntry(name) != null;
        }

        /// <summary>
        /// Determines whether a directory exists: it has its own entry, or any entry is inside it.
        /// </summary>
        /// <param name="name">The directory's zip entry name, without a trailing "/".</param>
        /// <returns>True if the directory exists. The root always exists.</returns>
        private bool DirectoryExists(string name)
        {
            if (name.Length == 0)
                return true;
            var prefix = name + Separator;
            return _archive.Entries.Any(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal));
        }

        /// <summary>
        /// Determines whether a file or directory has a name.
        /// </summary>
        /// <param name="name">The zip entry name.</param>
        /// <returns>True if a file or directory has that name.</returns>
        private bool EntryExists(string name)
        {
            return FileExists(name) || DirectoryExists(name);
        }

        /// <summary>
        /// Gives a directory its own entry if nothing else keeps it in existence, so that removing
        /// the last thing in a directory doesn't remove the directory too.
        /// </summary>
        /// <param name="name">The directory's zip entry name, without a trailing "/".</param>
        private void KeepDirectory(string name)
        {
            if (!DirectoryExists(name))
                _archive.CreateEntry(name + Separator);
        }

        /// <summary>
        /// Moves a file or directory to a new name. Zip has no rename, so this copies each entry to
        /// its new name and deletes the original.
        /// </summary>
        /// <param name="oldName">The current zip entry name, without a trailing "/".</param>
        /// <param name="newName">The new zip entry name, without a trailing "/".</param>
        /// <param name="isDirectory">True to move a directory and everything in it.</param>
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
                var entry = FindFileEntry(oldName)!;
                CopyEntry(entry, newName);
                entry.Delete();
            }
            KeepDirectory(GetParentName(oldName));
        }

        /// <summary>
        /// Copies a zip entry, with its data, last-write time and attributes, to a new name.
        /// </summary>
        /// <param name="source">The entry to copy.</param>
        /// <param name="newName">The new entry's name. A name ending in "/" copies no data.</param>
        private void CopyEntry(ZipArchiveEntry source, string newName)
        {
            var target = _archive.CreateEntry(newName);
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

        /// <summary>
        /// Determines whether a name is a valid bare name for this file system.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>
        /// False if the name is empty, "." or "..", or contains "/", "\" or a control character.
        /// </returns>
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

        /// <summary>
        /// Checks that a name is a valid bare name for this file system.
        /// </summary>
        /// <param name="name">The name to check.</param>
        /// <param name="paramName">The name of the parameter it was passed as.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> isn't valid; see <see cref="IsValidName"/>.</exception>
        private static void ValidateName(string? name, string paramName)
        {
            if (name == null)
                throw new ArgumentNullException(paramName);
            if (!IsValidName(name))
                throw new ArgumentException(
                    $"'{name}' is not a valid name. Names may not be empty, \".\" or \"..\", or contain \"/\", \"\\\" or control characters.",
                    paramName);
        }

        /// <summary>
        /// Checks that a search pattern is valid.
        /// </summary>
        /// <param name="searchPattern">The pattern to check.</param>
        /// <param name="paramName">The name of the parameter it was passed as.</param>
        /// <exception cref="ArgumentNullException"><paramref name="searchPattern"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="searchPattern"/> contains a path separator.</exception>
        private static void ValidatePattern(string? searchPattern, string paramName)
        {
            if (searchPattern == null)
                throw new ArgumentNullException(paramName);
            if (searchPattern.IndexOfAny(PatternSeparators) >= 0)
                throw new ArgumentException("The search pattern contains a path separator.", paramName);
        }

        /// <summary>
        /// Builds the Saraka path of a child entry.
        /// </summary>
        /// <param name="parentPath">The parent directory's path.</param>
        /// <param name="name">The child's name.</param>
        /// <returns>The child's path.</returns>
        private static string CombinePath(string parentPath, string name)
        {
            return parentPath == RootPath ? RootPath + name : parentPath + Separator + name;
        }

        /// <summary>
        /// Gets the Saraka path of an entry's parent directory.
        /// </summary>
        /// <param name="path">The path of an entry other than the root.</param>
        /// <returns>The parent directory's path.</returns>
        private static string GetParentPath(string path)
        {
            var index = path.LastIndexOf(Separator);
            return index == 0 ? RootPath : path.Substring(0, index);
        }

        /// <summary>
        /// Determines whether a path is the same as, or inside, another path.
        /// </summary>
        /// <param name="path">The path to test.</param>
        /// <param name="ancestorPath">The possible ancestor.</param>
        /// <returns>True if <paramref name="path"/> is <paramref name="ancestorPath"/> or one of its descendants.</returns>
        private static bool IsSameOrDescendant(string path, string ancestorPath)
        {
            return string.Equals(path, ancestorPath, StringComparison.Ordinal)
                || path.StartsWith(ancestorPath + Separator, StringComparison.Ordinal);
        }

        /// <summary>
        /// Reads the attributes stored in a zip entry's external attributes.
        /// </summary>
        /// <param name="externalAttributes">The entry's external attributes.</param>
        /// <param name="isDirectory">True if the entry is a directory.</param>
        /// <returns>The MS-DOS attributes, with <see cref="FileAttributes.Directory"/> set for a directory.</returns>
        private static FileAttributes FromExternalAttributes(int externalAttributes, bool isDirectory)
        {
            var attributes = (FileAttributes)(externalAttributes & DosAttributeMask);
            attributes &= ~(FileAttributes.Directory | FileAttributes.Normal);
            if (isDirectory)
                attributes |= FileAttributes.Directory;
            return attributes == 0 ? FileAttributes.Normal : attributes;
        }

        /// <summary>
        /// Stores attributes in a zip entry's external attributes, keeping the Unix bits.
        /// </summary>
        /// <param name="externalAttributes">The entry's current external attributes.</param>
        /// <param name="attributes">The attributes to store. Only the MS-DOS attributes are kept.</param>
        /// <param name="isDirectory">True if the entry is a directory.</param>
        /// <returns>The new external attributes.</returns>
        private static int ToExternalAttributes(int externalAttributes, FileAttributes attributes, bool isDirectory)
        {
            var dosAttributes = (int)attributes & DosAttributeMask & ~(int)(FileAttributes.Directory | FileAttributes.Normal);
            if (isDirectory)
                dosAttributes |= (int)FileAttributes.Directory;
            return (externalAttributes & ~DosAttributeMask) | dosAttributes;
        }

        /// <summary>
        /// Converts a time to the form a zip entry stores. An Unspecified time is treated as UTC
        /// and a Local time is converted, as the contract requires. Zip stores local times, so the
        /// result is in local time.
        /// </summary>
        /// <param name="value">The time to convert.</param>
        /// <returns>The time as a local <see cref="DateTimeOffset"/>.</returns>
        private static DateTimeOffset ToZipTime(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
            return new DateTimeOffset(utc).ToLocalTime();
        }

        /// <summary>
        /// Converts a zip entry's time to UTC.
        /// </summary>
        /// <param name="value">The entry's time.</param>
        /// <returns>The time in UTC.</returns>
        private static DateTime FromZipTime(DateTimeOffset value)
        {
            return value.UtcDateTime;
        }

        /// <summary>
        /// Creates the exception thrown when a name is already taken.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The exception.</returns>
        private static InvalidOperationException NameTaken(string name)
        {
            return new InvalidOperationException($"An entry named '{name}' already exists.");
        }

        /// <summary>
        /// Deletes a file, ignoring any failure. Used to clean up after a failed open.
        /// </summary>
        /// <param name="fullPath">The host path of the file.</param>
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

        /// <summary>
        /// Runs a synchronous operation and reports its outcome through a task, so that every
        /// exception, including argument exceptions, is observed when the task is awaited.
        /// </summary>
        /// <typeparam name="T">The operation's result type.</typeparam>
        /// <param name="operation">The operation.</param>
        /// <returns>A completed, cancelled or faulted task.</returns>
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

        /// <summary>
        /// Runs a synchronous operation that has no result and reports its outcome through a task.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <returns>A completed, cancelled or faulted task.</returns>
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
