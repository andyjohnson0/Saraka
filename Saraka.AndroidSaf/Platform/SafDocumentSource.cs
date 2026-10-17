using System;
using System.Collections.Generic;
using System.IO;

using Android.Content;
using Android.Provider;


namespace Saraka.AndroidSaf
{
    /// <summary>
    /// Reads documents from a document tree through the content resolver. This is the only class
    /// that calls the Android document APIs, and it reports their exceptions as .NET exceptions.
    /// </summary>
    /// <remarks>
    /// Folders are listed with a single query of the folder's children, which is much faster than
    /// DocumentFile.ListFiles() - see https://stackoverflow.com/a/66296597/67316.
    /// </remarks>
    internal sealed class SafDocumentSource : IDocumentSource
    {
        private static readonly string[] DocumentProjection =
        {
            DocumentsContract.Document.ColumnMimeType,
            DocumentsContract.Document.ColumnLastModified,
            DocumentsContract.Document.ColumnSize,
            DocumentsContract.Document.ColumnDisplayName,
        };

        private static readonly string[] ChildrenProjection =
        {
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnMimeType,
            DocumentsContract.Document.ColumnLastModified,
            DocumentsContract.Document.ColumnDisplayName,
        };

        private readonly ContentResolver _resolver;
        private readonly Android.Net.Uri _treeUri;

        /// <summary>
        /// Creates a source for a document tree.
        /// </summary>
        /// <param name="context">A context used to reach the content resolver.</param>
        /// <param name="treeUri">The tree URI, as returned by the system folder picker.</param>
        /// <exception cref="InvalidOperationException">The context has no content resolver.</exception>
        public SafDocumentSource(Context context, Android.Net.Uri treeUri)
        {
            var appContext = context.ApplicationContext ?? context;
            _resolver = appContext.ContentResolver
                ?? throw new InvalidOperationException("The context has no content resolver.");
            _treeUri = treeUri;
            RootDocumentId = DocumentsContract.GetTreeDocumentId(treeUri)!;
        }

        /// <inheritdoc/>
        public string RootDocumentId { get; }

        /// <inheritdoc/>
        /// <remarks>
        /// A provider's query returns null for a document that doesn't exist, because
        /// DocumentsProvider.query catches the provider's FileNotFoundException.
        /// </remarks>
        public DocumentDetails? QueryDocument(string documentId)
        {
            return Translate(() =>
            {
                using var cursor = _resolver.Query(DocumentUri(documentId), DocumentProjection, null, null, null);
                if (cursor == null || !cursor.MoveToFirst())
                    return (DocumentDetails?)null;

                return new DocumentDetails(
                    IsDirectory: cursor.GetString(0) == DocumentsContract.Document.MimeTypeDir,
                    LastModified: cursor.IsNull(1) ? null : cursor.GetLong(1),
                    Size: cursor.IsNull(2) ? null : cursor.GetLong(2),
                    DisplayName: cursor.GetString(3));
            });
        }

        /// <inheritdoc/>
        public IReadOnlyList<DocumentEntry> ListChildren(string directoryDocumentId)
        {
            return Translate(() =>
            {
                // Build a uri that refers to the _contents of_ the folder. The folder can be any folder in the tree, so
                // use its own document id rather than the tree's.
                var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(_treeUri, directoryDocumentId)!;

                // Note that the file system provider that is used by our ContentResolver's Query() method seems† to
                // ignore any selection and order clauses that we pass to it, so we have to get everything and do
                // filtering/ordering ourselves.
                // † By experiment, and also see https://stackoverflow.com/a/61214849/67316
                using var cursor = _resolver.Query(childrenUri, ChildrenProjection, null, null, null)
                    ?? throw new FileNotFoundException($"The folder '{directoryDocumentId}' does not exist.");
                return ReadEntries(cursor);
            });
        }

        /// <inheritdoc/>
        public Stream OpenRead(string documentId)
        {
            return Translate(() =>
            {
                var stream = _resolver.OpenInputStream(DocumentUri(documentId))
                    ?? throw new IOException($"The provider returned no stream for '{documentId}'.");
                return (Stream)new ProviderStream(stream);
            });
        }

        /// <summary>
        /// Reports an Android or Java exception as the .NET exception that <see cref="IDocumentSource"/>
        /// specifies, keeping the original as the inner exception.
        /// </summary>
        public static Exception Translate(Java.Lang.Throwable exception)
        {
            return exception switch
            {
                Java.Lang.SecurityException => new UnauthorizedAccessException(exception.Message, exception),
                Java.IO.FileNotFoundException => new FileNotFoundException(exception.Message, exception),
                Android.OS.OperationCanceledException => new OperationCanceledException(exception.Message, exception),
                _ => new IOException(exception.Message, exception),
            };
        }

        /// <summary>
        /// Runs a call to the provider, reporting Android and Java exceptions as .NET exceptions.
        /// </summary>
        /// <typeparam name="T">The call's result type.</typeparam>
        /// <param name="operation">The call.</param>
        /// <returns>The call's result.</returns>
        private static T Translate<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (Java.Lang.Throwable ex)
            {
                throw Translate(ex);
            }
        }

        /// <summary>
        /// Builds the content URI of a document in the tree.
        /// </summary>
        /// <param name="documentId">The document ID.</param>
        /// <returns>The document's URI.</returns>
        private Android.Net.Uri DocumentUri(string documentId)
        {
            return DocumentsContract.BuildDocumentUriUsingTree(_treeUri, documentId)!;
        }

        /// <summary>
        /// Reads a folder's children from a cursor over <see cref="ChildrenProjection"/>. Documents
        /// without an ID are skipped, because they can't be opened.
        /// </summary>
        /// <param name="cursor">The cursor.</param>
        /// <returns>The documents, in the provider's order.</returns>
        private static List<DocumentEntry> ReadEntries(Android.Database.ICursor cursor)
        {
            var entries = new List<DocumentEntry>();
            while (cursor.MoveToNext())
            {
                var documentId = cursor.GetString(0);
                if (documentId == null)
                    continue;  // Can't be opened, so ignore it.

                entries.Add(new DocumentEntry(
                    DocumentId: documentId,
                    DisplayName: cursor.GetString(3) ?? string.Empty,
                    LastModified: cursor.GetLong(2),
                    IsDirectory: cursor.GetString(1) == DocumentsContract.Document.MimeTypeDir));
            }
            return entries;
        }
    }
}
