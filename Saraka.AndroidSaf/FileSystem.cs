using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Saraka.Abstractions;


namespace Saraka.AndroidSaf
{
    /// <summary>
    /// A read-only file system backed by a document tree from the Android Storage Access Framework
    /// (SAF), such as a folder the user chose with the system folder picker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tree's top-level folder becomes the root directory, "/". Paths are built from the
    /// documents' display names, so the document "DCIM/Camera/img1.jpg" has the path
    /// "/DCIM/Camera/img1.jpg". The file system is read-only: <see cref="CanWrite"/> is always
    /// false, and write operations throw <see cref="InvalidOperationException"/>.
    /// </para>
    /// <para>
    /// Names are matched case-insensitively, in lookups and search patterns. If a directory holds
    /// two entries of the same kind whose names differ only in case, only the first one in the
    /// provider's order is used. Entries whose display names can't be Saraka names, such as names
    /// containing "/", are ignored. A document is hidden if its name starts with a period.
    /// </para>
    /// <para>
    /// Each entry refers to its document by the provider's document ID. For the device's own
    /// storage the ID is derived from the document's location, so an entry behaves as a location.
    /// Other providers may use IDs that stay the same when a document is renamed or moved, and an
    /// entry from such a provider then goes on referring to the document at its new location.
    /// </para>
    /// <para>
    /// The SAF records only a last-modified time. Creation times read as the last-modified time,
    /// and a document whose provider doesn't record a time reports the Unix epoch. Attributes are
    /// <see cref="FileAttributes.Directory"/> or <see cref="FileAttributes.Normal"/>, plus
    /// <see cref="FileAttributes.Hidden"/> for names starting with a period.
    /// </para>
    /// <para>
    /// Calls to the document provider block, so every operation runs on the thread pool. Android
    /// and Java exceptions are never passed on as they are: they are wrapped in .NET exceptions,
    /// with the original as the inner exception, both from operations and from reading streams
    /// returned by <see cref="IFile.OpenReadAsync"/>. A denied or revoked permission gives
    /// <see cref="UnauthorizedAccessException"/>, a missing document gives
    /// <see cref="FileNotFoundException"/> or <see cref="DirectoryNotFoundException"/>, a
    /// cancellation by the provider gives <see cref="OperationCanceledException"/>, and any
    /// other provider failure gives <see cref="IOException"/>. Instances of this class and of the
    /// entries it returns are not thread-safe.
    /// </para>
    /// </remarks>
    public sealed partial class FileSystem : IFileSystem
    {
        private const string RootPath = "/";
        private const char Separator = '/';
        private const int CopyBufferSize = 81920;

        private static readonly char[] PatternSeparators = { '/', '\\' };

        private readonly IDocumentSource _source;
        private readonly DirectoryEntry _root;
        private bool _disposed;

        /// <summary>
        /// Creates a file system over a document source.
        /// </summary>
        /// <param name="source">The document source.</param>
        private FileSystem(IDocumentSource source)
        {
            _source = source;
            _root = new DirectoryEntry(this, null, string.Empty, source.RootDocumentId);
        }

        /// <inheritdoc/>
        public IDirectory Root => _root;

        /// <inheritdoc/>
        /// <remarks>This file system is read-only, so this is always false.</remarks>
        public bool CanWrite => false;

        /// <inheritdoc/>
        /// <remarks>
        /// Names are compared ignoring case (<see cref="StringComparer.OrdinalIgnoreCase"/>), as the
        /// device's own storage does.
        /// </remarks>
        public StringComparer NameComparer => StringComparer.OrdinalIgnoreCase;

        /// <inheritdoc/>
        /// <remarks>
        /// The display name of the tree's top-level folder, such as "Pictures", as the provider
        /// reported it when the file system was opened.
        /// </remarks>
        public string DisplayName { get; private set; } = string.Empty;

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
        /// Opens a file system over a document source, checking that the root exists, is a folder
        /// and can be read. Used by the Android <c>OpenAsync</c>, and by tests.
        /// </summary>
        /// <param name="source">The document source.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The opened file system.</returns>
        /// <exception cref="DirectoryNotFoundException">The root folder doesn't exist.</exception>
        private static async Task<FileSystem> OpenCoreAsync(IDocumentSource source, CancellationToken cancellationToken)
        {
            var fileSystem = new FileSystem(source);
            // Reading the root's details checks that it exists, is a folder and can be read.
            var details = await fileSystem._root.GetDetailsAsync(cancellationToken).ConfigureAwait(false);
            fileSystem.DisplayName = details.DisplayName ?? string.Empty;
            return fileSystem;
        }

        /// <summary>
        /// Throws if the file system has been disposed.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The file system has been disposed.</exception>
        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        /// <summary>
        /// Determines whether a name is a valid Saraka name.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>False if the name is empty, "." or "..", or contains "/".</returns>
        private static bool IsValidName(string name)
        {
            return name.Length > 0 && name != "." && name != ".." && name.IndexOf(Separator) < 0;
        }

        /// <summary>
        /// Checks that a name is a valid bare name.
        /// </summary>
        /// <param name="name">The name to check.</param>
        /// <param name="paramName">The name of the parameter it was passed as.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> isn't valid; see <see cref="IsValidName"/>.</exception>
        private static void ValidateName(string? name, string paramName)
        {
            ArgumentNullException.ThrowIfNull(name, paramName);
            if (!IsValidName(name))
                throw new ArgumentException(
                    $"'{name}' is not a valid name. Names may not be empty, \".\" or \"..\", or contain \"/\".",
                    paramName);
        }

        /// <summary>
        /// Checks that a search pattern is valid. "\" is rejected as well as "/", because it is an
        /// escape character in the pattern matcher.
        /// </summary>
        /// <param name="searchPattern">The pattern to check.</param>
        /// <param name="paramName">The name of the parameter it was passed as.</param>
        /// <exception cref="ArgumentNullException"><paramref name="searchPattern"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="searchPattern"/> contains "/" or "\".</exception>
        private static void ValidatePattern(string? searchPattern, string paramName)
        {
            ArgumentNullException.ThrowIfNull(searchPattern, paramName);
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
        /// Converts a provider's last-modified time to UTC.
        /// </summary>
        /// <param name="milliseconds">Milliseconds since the Unix epoch, or null if unknown.</param>
        /// <returns>The time in UTC, or the Unix epoch if it's unknown.</returns>
        private static DateTime FromUnixTime(long? milliseconds)
        {
            return milliseconds.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds.Value).UtcDateTime
                : DateTime.UnixEpoch;
        }
    }
}
