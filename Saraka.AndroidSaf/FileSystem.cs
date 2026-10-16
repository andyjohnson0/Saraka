using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Android.Content;
using Android.Provider;

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
    /// The <see cref="FileSystemOptions"/> passed to <see cref="OpenAsync"/> filter the entries
    /// the file system presents. Unlike the general contract, which says listings include every
    /// entry, entries the options exclude don't appear in listings and can't be found by name.
    /// By default, hidden files and directories (names starting with a period) are excluded.
    /// </para>
    /// <para>
    /// Names are matched case-insensitively, in lookups and search patterns. If a directory holds
    /// two entries of the same kind whose names differ only in case, only the first one listed is
    /// used. Entries whose display names can't be Saraka names, such as names containing "/", are
    /// ignored.
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

        private static readonly string[] DocumentProjection =
        {
            DocumentsContract.Document.ColumnMimeType,
            DocumentsContract.Document.ColumnLastModified,
            DocumentsContract.Document.ColumnSize,
        };

        private readonly Context _context;
        private readonly ContentResolver _resolver;
        private readonly Android.Net.Uri _treeUri;
        private readonly SortField _sortField;
        private readonly SortOrder _sortOrder;
        private readonly string[]? _fileTypes;
        private readonly bool _includeHiddenFiles;
        private readonly bool _includeHiddenFolders;
        private readonly DirectoryEntry _root;
        private bool _disposed;

        private FileSystem(Context context, Android.Net.Uri treeUri, FileSystemOptions options)
        {
            _context = context.ApplicationContext ?? context;
            _resolver = _context.ContentResolver
                ?? throw new InvalidOperationException("The context has no content resolver.");
            _treeUri = treeUri;
            _sortField = options.SortField;
            _sortOrder = options.SortOrder;
            _fileTypes = options.FileTypes?.ToArray();
            _includeHiddenFiles = options.IncludeHiddenFiles;
            _includeHiddenFolders = options.IncludeHiddenFolders;
            _root = new DirectoryEntry(this, null, string.Empty, DocumentsContract.GetTreeDocumentId(treeUri)!);
        }

        /// <summary>
        /// Opens a document tree as a read-only file system.
        /// </summary>
        /// <param name="context">
        /// A context used to reach the content resolver. Its application context is kept, not the
        /// context itself.
        /// </param>
        /// <param name="treeUri">
        /// The tree URI, as returned by the system folder picker. The app must hold permission to
        /// read it.
        /// </param>
        /// <param name="options">
        /// Options that filter and order the entries, or null to use the defaults.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The opened file system.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> or <paramref name="treeUri"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="treeUri"/> is not a document tree URI, or <paramref name="options"/>
        /// holds an invalid value.
        /// </exception>
        /// <exception cref="DirectoryNotFoundException">The tree's top-level folder doesn't exist.</exception>
        /// <exception cref="UnauthorizedAccessException">The app doesn't have permission to read the tree.</exception>
        public static async Task<FileSystem> OpenAsync(
            Context context,
            Android.Net.Uri treeUri,
            FileSystemOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(treeUri);
            if (!DocumentsContract.IsTreeUri(treeUri))
                throw new ArgumentException("The URI is not a document tree URI.", nameof(treeUri));
            options ??= new FileSystemOptions();
            ValidateOptions(options, nameof(options));

            var fileSystem = new FileSystem(context, treeUri, options);
            // Reading the root's attributes checks that it exists, is a folder and can be read.
            await fileSystem._root.GetAttributesAsync(cancellationToken).ConfigureAwait(false);
            return fileSystem;
        }

        /// <inheritdoc/>
        public IDirectory Root => _root;

        /// <inheritdoc/>
        /// <remarks>This file system is read-only, so this is always false.</remarks>
        public bool CanWrite => false;

        /// <summary>
        /// Closes the file system. Operations on it or its entries then throw
        /// <see cref="ObjectDisposedException"/>. Streams that are already open are not closed.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return default;
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private Android.Net.Uri DocumentUri(string documentId)
        {
            return DocumentsContract.BuildDocumentUriUsingTree(_treeUri, documentId)!;
        }

        // Returns null if the provider has no such document.
        private DocumentDetails? QueryDocument(string documentId)
        {
            using var cursor = _resolver.Query(DocumentUri(documentId), DocumentProjection, null, null, null);
            if (cursor == null || !cursor.MoveToFirst())
                return null;

            return new DocumentDetails(
                IsDirectory: cursor.GetString(0) == DocumentsContract.Document.MimeTypeDir,
                LastModified: cursor.IsNull(1) ? null : cursor.GetLong(1),
                Size: cursor.IsNull(2) ? null : cursor.GetLong(2));
        }

        private DocumentCollection ListDocuments(string directoryDocumentId)
        {
            return DocumentCollection.Create(
                _context,
                DocumentUri(directoryDocumentId),
                _sortField,
                _sortOrder,
                _fileTypes,
                _includeHiddenFiles,
                _includeHiddenFolders);
        }

        private static void ValidateOptions(FileSystemOptions options, string paramName)
        {
            if (!Enum.IsDefined(options.SortField))
                throw new ArgumentException("The sort field is not valid.", paramName);
            if (!Enum.IsDefined(options.SortOrder))
                throw new ArgumentException("The sort order is not valid.", paramName);
            if (options.FileTypes != null && options.FileTypes.Any(string.IsNullOrEmpty))
                throw new ArgumentException("The file types contain a null or empty value.", paramName);
        }

        private static bool IsValidName(string name)
        {
            return name.Length > 0 && name != "." && name != ".." && name.IndexOf(Separator) < 0;
        }

        private static void ValidateName(string? name, string paramName)
        {
            ArgumentNullException.ThrowIfNull(name, paramName);
            if (!IsValidName(name))
                throw new ArgumentException(
                    $"'{name}' is not a valid name. Names may not be empty, \".\" or \"..\", or contain \"/\".",
                    paramName);
        }

        private static void ValidatePattern(string? searchPattern, string paramName)
        {
            ArgumentNullException.ThrowIfNull(searchPattern, paramName);
            if (searchPattern.IndexOfAny(PatternSeparators) >= 0)
                throw new ArgumentException("The search pattern contains a path separator.", paramName);
        }

        private static string CombinePath(string parentPath, string name)
        {
            return parentPath == RootPath ? RootPath + name : parentPath + Separator + name;
        }

        private static DateTime FromUnixTime(long? milliseconds)
        {
            return milliseconds.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds.Value).UtcDateTime
                : DateTime.UnixEpoch;
        }

        // Runs an operation on the thread pool, because provider calls block, and reports the
        // provider's Java exceptions as the exceptions the contract specifies.
        private static async Task<T> RunTranslatedAsync<T>(
            Func<Task<T>> operation,
            Func<Exception, IOException> notFound,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await Task.Run(operation, cancellationToken).ConfigureAwait(false);
            }
            catch (Java.Lang.Throwable ex)
            {
                throw TranslateProviderException(ex, notFound);
            }
        }

        // The original exception is kept as the inner exception. Anything not recognised is a
        // failure of the provider, and so of the underlying storage.
        private static Exception TranslateProviderException(Java.Lang.Throwable exception, Func<Exception, IOException> notFound)
        {
            return exception switch
            {
                Java.Lang.SecurityException => new UnauthorizedAccessException(exception.Message, exception),
                Java.IO.FileNotFoundException => notFound(exception),
                Android.OS.OperationCanceledException => new OperationCanceledException(exception.Message, exception),
                _ => new IOException(exception.Message, exception),
            };
        }

        private readonly record struct DocumentDetails(bool IsDirectory, long? LastModified, long? Size);
    }
}
