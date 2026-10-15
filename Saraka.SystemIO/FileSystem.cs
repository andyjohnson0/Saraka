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

        // Used only to detect moving a directory into itself or one of its descendants.
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

        private readonly string rootFullPath;
        private readonly DirectoryEntry root;
        private bool disposed;

        private FileSystem(string rootFullPath, bool canWrite)
        {
            this.rootFullPath = rootFullPath;
            CanWrite = canWrite;
            root = new DirectoryEntry(this, RootPath);
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
        public IDirectory Root => root;

        /// <inheritdoc/>
        /// <remarks>This is the value passed to <see cref="OpenAsync"/> and doesn't change.</remarks>
        public bool CanWrite { get; }

        /// <summary>
        /// Closes the file system. Operations on it or its entries then throw
        /// <see cref="ObjectDisposedException"/>. Streams that are already open are not closed.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            disposed = true;
            return default;
        }

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

        private string ToFullPath(string path)
        {
            if (path == RootPath)
                return rootFullPath;
            var segments = path.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries);
            return Path.Combine(new[] { rootFullPath }.Concat(segments).ToArray());
        }

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
            return string.Equals(path, ancestorPath, PathComparison)
                || path.StartsWith(ancestorPath + Separator, PathComparison);
        }

        private static bool EntryExists(string fullPath)
        {
            return File.Exists(fullPath) || Directory.Exists(fullPath);
        }

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

        private static void ValidatePattern(string searchPattern, string paramName)
        {
            if (searchPattern == null)
                throw new ArgumentNullException(paramName);
            if (searchPattern.IndexOfAny(PatternSeparators) >= 0)
                throw new ArgumentException("The search pattern contains a path separator.", paramName);
        }

        // Unspecified is treated as UTC and Local is converted, as the contract requires.
        private static DateTime ToUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
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
