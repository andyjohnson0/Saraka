using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Saraka.Abstractions;

namespace Saraka.SystemIO
{
    /// <summary>
    /// A file system backed by a directory on the local machine, accessed through System.IO.
    /// </summary>
    /// <remarks>
    /// The directory passed to <see cref="OpenAsync"/> becomes the root directory, "/". Entries
    /// outside it can't be reached through Saraka paths. Saraka paths always use "/"; they are
    /// mapped to the host's own separator when System.IO is called. Names may not contain "\"
    /// on any platform, since it is a separator on Windows. Instances of this class and of the entries
    /// it returns are not thread-safe.
    /// </remarks>
    public sealed partial class FileSystem : IFileSystem
    {
        private const string RootPath = "/";
        private const char Separator = '/';
        private const int StreamBufferSize = 4096;
        private const int CopyBufferSize = 81920;

        private static readonly char[] InvalidNameChars =
            Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/' }).Distinct().ToArray();

        private static readonly char[] PatternSeparators = { '\\', '/' };

        /// <summary>
        /// How host paths are compared. Used only to detect moving a directory into itself or one
        /// of its descendants.
        /// </summary>
        private static readonly StringComparison PathComparison =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static readonly EnumerationOptions ListingOptions = new EnumerationOptions
        {
            AttributesToSkip = 0,
            IgnoreInaccessible = false,
            MatchCasing = MatchCasing.PlatformDefault,
            MatchType = MatchType.Simple,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
        };

        private readonly string _rootFullPath;
        private readonly DirectoryEntry _root;
        private bool _disposed;

        /// <summary>
        /// Creates a file system rooted at a directory that is known to exist.
        /// </summary>
        /// <param name="rootFullPath">The full host path of the root directory.</param>
        /// <param name="canWrite">True to allow write operations.</param>
        private FileSystem(string rootFullPath, bool canWrite)
        {
            _rootFullPath = rootFullPath;
            CanWrite = canWrite;
            _root = new DirectoryEntry(this, RootPath);
        }

        /// <summary>
        /// Opens a file system rooted at an existing directory.
        /// </summary>
        /// <param name="rootPath">
        /// The local directory that becomes the root directory. A relative path is resolved against
        /// the current directory.
        /// </param>
        /// <param name="canWrite">True to allow write operations; false to make the file system read-only.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The opened file system.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rootPath"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="rootPath"/> is empty or whitespace.</exception>
        /// <exception cref="DirectoryNotFoundException"><paramref name="rootPath"/> is not an existing directory.</exception>
        public static Task<FileSystem> OpenAsync(string rootPath, bool canWrite, CancellationToken cancellationToken = default)
        {
            return Complete(() =>
            {
                if (rootPath == null)
                    throw new ArgumentNullException(nameof(rootPath));
                if (rootPath.Trim().Length == 0)
                    throw new ArgumentException("The root path is empty.", nameof(rootPath));
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.GetFullPath(rootPath);
                if (!Directory.Exists(fullPath))
                    throw new DirectoryNotFoundException($"The directory '{fullPath}' does not exist.");
                return new FileSystem(fullPath, canWrite);
            });
        }

        /// <inheritdoc/>
        public IDirectory Root => _root;

        /// <inheritdoc/>
        /// <remarks>This is the value passed to <see cref="OpenAsync"/> and doesn't change.</remarks>
        public bool CanWrite { get; }

        /// <summary>
        /// Closes the file system. Operations on it or its entries then throw
        /// <see cref="ObjectDisposedException"/>. Streams that are already open are not closed.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return default;
        }

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
        /// Maps a Saraka path to a host path, one segment at a time.
        /// </summary>
        /// <param name="path">The Saraka path, rooted at "/".</param>
        /// <returns>The full host path.</returns>
        private string ToFullPath(string path)
        {
            if (path == RootPath)
                return _rootFullPath;
            var segments = path.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries);
            return Path.Combine(new[] { _rootFullPath }.Concat(segments).ToArray());
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
            return string.Equals(path, ancestorPath, PathComparison)
                || path.StartsWith(ancestorPath + Separator, PathComparison);
        }

        /// <summary>
        /// Determines whether a file or directory exists at a host path.
        /// </summary>
        /// <param name="fullPath">The host path.</param>
        /// <returns>True if a file or directory exists there.</returns>
        private static bool EntryExists(string fullPath)
        {
            return File.Exists(fullPath) || Directory.Exists(fullPath);
        }

        /// <summary>
        /// Checks that a name is a valid bare name for this file system.
        /// </summary>
        /// <param name="name">The name to check.</param>
        /// <param name="paramName">The name of the parameter it was passed as.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> is empty, "." or "..", or contains a separator or a character the host doesn't allow.
        /// </exception>
        private static void ValidateName(string name, string paramName)
        {
            if (name == null)
                throw new ArgumentNullException(paramName);
            if (name.Length == 0)
                throw new ArgumentException("The name is empty.", paramName);
            if (name == "." || name == "..")
                throw new ArgumentException($"'{name}' is not a valid name.", paramName);
            if (name.IndexOfAny(InvalidNameChars) >= 0)
                throw new ArgumentException(
                    "The name contains a path separator or a character that isn't valid in a file name.", paramName);
        }

        /// <summary>
        /// Checks that a search pattern is valid.
        /// </summary>
        /// <param name="searchPattern">The pattern to check.</param>
        /// <param name="paramName">The name of the parameter it was passed as.</param>
        /// <exception cref="ArgumentNullException"><paramref name="searchPattern"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="searchPattern"/> contains a path separator.</exception>
        private static void ValidatePattern(string searchPattern, string paramName)
        {
            if (searchPattern == null)
                throw new ArgumentNullException(paramName);
            if (searchPattern.IndexOfAny(PatternSeparators) >= 0)
                throw new ArgumentException("The search pattern contains a path separator.", paramName);
        }

        /// <summary>
        /// Converts a time to UTC as the contract requires: an Unspecified time is treated as UTC,
        /// and a Local time is converted.
        /// </summary>
        /// <param name="value">The time to convert.</param>
        /// <returns>The time in UTC.</returns>
        private static DateTime ToUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
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
        /// Deletes a file, ignoring any failure. Used to clean up after a failed write.
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
