using System;
using System.Collections.Generic;
using System.Linq;

using Android.Content;
using Android.Provider;

using Saraka.AndroidSaf;



namespace Saraka.AndroidSaf
{
    /// <summary>
    /// Implements a collection of content resolver document IDs and methods to resolve them to Uris on-demand.
    /// This is much more time efficient than DocumentFile.ListFiles() - see https://stackoverflow.com/a/66296597/67316.
    /// </summary>
    internal class DocumentCollection
    {
        /// <summary>
        /// Index value for "no position"
        /// </summary>
        public const int NoPosition = Positions.None;


        /// <summary>
        /// Get the folder uri for the root of a document tree. Collections are created from folder uris, which are
        /// document uris built from a tree uri, so that they can refer to any folder in the tree.
        /// </summary>
        /// <param name="treeUri">Tree uri, as returned by the system folder picker</param>
        /// <returns>Folder uri for the root of the tree</returns>
        public static Android.Net.Uri RootFolderUri(Android.Net.Uri treeUri)
        {
            ArgumentNullException.ThrowIfNull(treeUri);

            return DocumentsContract.BuildDocumentUriUsingTree(treeUri, DocumentsContract.GetTreeDocumentId(treeUri))!;
        }


        /// <summary>
        /// Create a DocumentCollection object.
        /// </summary>
        /// <param name="context">Context used to access content resolver.</param>
        /// <param name="dirUri">Folder uri of the directory containing the image file documents (see RootFolderUri()).</param>
        /// <param name="sortField">Field to sort on.</param>
        /// <param name="sortOrder">Order to sort.</param>
        /// <param name="fileTypes">File types (e.g. "jpegg" etc). Pass null for all files.</param>
        /// <param name="includeHiddenFiles">True to include hidden image files (name starts with a period).</param>
        /// <param name="includeHiddenFolders">True to include hidden subfolders (name starts with a period).</param>
        /// <returns>DocumentCollection object.</returns>
        public static DocumentCollection Create(
            Context context,
            Android.Net.Uri dirUri,
            SortField sortField = SortField.FileName,
            SortOrder sortOrder = SortOrder.Ascending,
            IEnumerable<string>? fileTypes = null,
            bool includeHiddenFiles = false,
            bool includeHiddenFolders = false)
        {
            var ft = DocumentSelection.ExpandFileTypes(fileTypes);

            return CreateImpl(context, dirUri, sortField, sortOrder, ft, includeHiddenFiles, includeHiddenFolders);
        }


        /// <summary>
        /// Get the position of a document in this collection.
        /// Use this to find a document again after the collection has been recreated, because positions can change
        /// if files are added, removed or renamed.
        /// </summary>
        /// <param name="documentUri">Document uri, as returned by UriAt()</param>
        /// <returns>Position of the document, or NoPosition if it is not in the collection</returns>
        /// <exception cref="ArgumentNullException">documentUri argument most not be null</exception>
        public int PositionOf(
            Android.Net.Uri documentUri)
        {
            ArgumentNullException.ThrowIfNull(documentUri);

            var documentId = DocumentsContract.GetDocumentId(documentUri);
            return Array.IndexOf(this.documentFileIds, documentId);  // -1 (NoPosition) if not found
        }


        /// <summary>
        /// Get url of directory containing the image document files. This is the url passed to Create().
        /// </summary>
        public Android.Net.Uri DirectoryUri
        {
            get { return this.dirUri; }
        }


        /// <summary>
        /// Number of image document files in the collection.
        /// </summary>
        public int Length
        {
            get { return documentFileIds.Length; }
        }


        /// <summary>
        /// Document id of the directory.
        /// </summary>
        public string DirectoryDocumentId
        {
            get { return DocumentsContract.GetDocumentId(this.dirUri)!; }
        }


        /// <summary>
        /// Number of subfolders of the directory. Hidden folders are included only if the collection was created to
        /// include them.
        /// </summary>
        public int FolderCount
        {
            get { return folderIds.Length; }
        }


        /// <summary>
        /// Get the document id of the subfolder at the specified position.
        /// </summary>
        /// <param name="position">Zero-based position, in the same order as the image files</param>
        /// <returns>Document id of the subfolder</returns>
        /// <exception cref="ArgumentException">An argument value was invalid</exception>
        public string FolderDocumentIdAt(int position)
        {
            if (position < 0 || position >= folderIds.Length)
                throw new ArgumentException("Index out of range", nameof(position));

            return folderIds[position];
        }


        /// <summary>
        /// Get the display name of the subfolder at the specified position, as it was when the collection was created.
        /// </summary>
        /// <param name="position">Zero-based position, in the same order as the image files</param>
        /// <returns>Display name of the subfolder</returns>
        /// <exception cref="ArgumentException">An argument value was invalid</exception>
        public string FolderDisplayNameAt(int position)
        {
            if (position < 0 || position >= folderDisplayNames.Length)
                throw new ArgumentException("Index out of range", nameof(position));

            return folderDisplayNames[position];
        }


        /// <summary>
        /// Get the folder uri of the subfolder at the specified position, for creating a collection for it.
        /// </summary>
        /// <param name="position">Zero-based position, in the same order as the image files</param>
        /// <returns>Folder uri of the subfolder</returns>
        /// <exception cref="ArgumentException">An argument value was invalid</exception>
        public Android.Net.Uri FolderUriAt(int position)
        {
            return DocumentsContract.BuildDocumentUriUsingTree(dirUri, FolderDocumentIdAt(position))!;
        }


        /// <summary>
        /// Get distinct file extensions (excluding leading period)
        /// </summary>
        public string[] DocumentDisplayNameExtensions
        {
            get { return documentDisplayNameExtensionCounts.Keys.ToArray(); }
        }


        /// <summary>
        /// get the number of documents with a given display name extension
        /// </summary>
        /// <param name="extension">Extension (excluding leading period)</param>
        /// <returns>
        /// Document count. Zero if no matches.
        /// </returns>
        public int GetDocumentDisplayNameExtensionCount(string extension)
        {
            if (documentDisplayNameExtensionCounts.ContainsKey(extension))
            {
                return documentDisplayNameExtensionCounts[extension];
            }
            else
            {
                return 0;
            }
        }


        /// <summary>
        /// Get the content url of the image file document at the specified position
        /// </summary>
        /// <param name="position">Zero-based position</param>
        /// <returns>Url of the image file document</returns>
        /// <exception cref="ArgumentNullException">A required argument was null</exception>
        /// <exception cref="ArgumentException">An argument value was invalid</exception>
        public Android.Net.Uri UriAt(
            int position)
        {
            if (position < 0 || position >= documentFileIds.Length)
                throw new ArgumentException("Index out of range", nameof(position));

            var fileUri = DocumentsContract.BuildDocumentUriUsingTree(dirUri, documentFileIds[position])!;
            return fileUri;
        }


        /// <summary>
        /// Get the display name (e.g. "image123.png") of the image file document at the specified position, as it was
        /// when the collection was created.
        /// </summary>
        /// <param name="position">Zero-based position</param>
        /// <returns>Display name of the image file document</returns>
        /// <exception cref="ArgumentException">An argument value was invalid</exception>
        public string DisplayNameAt(
            int position)
        {
            if (position < 0 || position >= documentDisplayNames.Length)
                throw new ArgumentException("Index out of range", nameof(position));

            return documentDisplayNames[position];
        }


        /// <summary>
        /// Get the position of the next image file document after the supplied position.
        /// </summary>
        /// <param name="position">Position</param>
        /// <returns>Position of the next image file document, or -1 if there is no next image file document</returns>
        public int NextPosition(int position)
        {
            return Positions.Next(position, this.Length);
        }


        /// <summary>
        /// Get the position of the previous image file document before the supplied position.
        /// </summary>
        /// <param name="position">Position</param>
        /// <returns>Position of the previous image file document, or -1 if there is no previous image file document</returns>
        public int PreviousPosition(int position)
        {
            return Positions.Previous(position, this.Length);
        }


        /// <summary>
        /// Get the position of the first image file document.
        /// </summary>
        /// <returns>Position of the first image file document, or -1 if there is no first image file document</returns>
        public int FirstPosition()
        {
            return Positions.First(this.Length);
        }


        /// <summary>
        /// Get the position of the last image file document.
        /// </summary>
        /// <returns>Position of the last image file document, or -1 if there is no last image file document</returns>
        public int LastPosition()
        {
            return Positions.Last(this.Length);
        }


        /// <summary>
        /// Get the position of a randomly-selected image file document.
        /// </summary>
        /// <param name="excludePosition">
        /// If specified, does not return this position.
        /// Use to prevent a current position from being returned.
        /// </param>
        /// <returns>Position of a randomly-selected image file document, or DocumentCollection.NoPosition if one could not be generated.</returns>
        /// <exception cref="InvalidOperationException"></exception>
        public int RandomPosition(
            int? excludePosition = null)
        {
            return Positions.Random(this.Length, excludePosition, System.Random.Shared);
        }


        #region Implementaton

        private DocumentCollection(
            Android.Net.Uri dirUri,
            string[] documentIds,
            string[] documentDisplayNames,
            Dictionary<string, int> documentFileExtensionCounts,
            string[] folderIds,
            string[] folderDisplayNames)
        {
            if (dirUri == null)
                throw new ArgumentNullException(nameof(dirUri));
            if (documentIds == null)
                throw new ArgumentNullException(nameof(documentIds));
            if (documentDisplayNames == null || documentDisplayNames.Length != documentIds.Length)
                throw new ArgumentException("Must have a display name for each document", nameof(documentDisplayNames));
            if (folderIds == null)
                throw new ArgumentNullException(nameof(folderIds));
            if (folderDisplayNames == null || folderDisplayNames.Length != folderIds.Length)
                throw new ArgumentException("Must have a display name for each folder", nameof(folderDisplayNames));

            this.dirUri = dirUri;
            this.documentFileIds = documentIds;
            this.documentDisplayNames = documentDisplayNames;
            this.documentDisplayNameExtensionCounts = documentFileExtensionCounts;
            this.folderIds = folderIds;
            this.folderDisplayNames = folderDisplayNames;
        }

        private readonly Android.Net.Uri dirUri;
        private readonly string[] documentFileIds;
        private readonly string[] documentDisplayNames;
        private readonly Dictionary<string, int> documentDisplayNameExtensionCounts;
        private readonly string[] folderIds;
        private readonly string[] folderDisplayNames;


        private static DocumentCollection CreateImpl(
            Context context,
            Android.Net.Uri dirUri,
            SortField sortField = SortField.FileName,
            SortOrder sortOrder = SortOrder.Ascending,
            IEnumerable<string>? fileTypes = null,
            bool includeHiddenFiles = false,
            bool includeHiddenFolders = false)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));
            ArgumentNullException.ThrowIfNull(dirUri, nameof(dirUri));

            // Build a uri that refers to the _contents of_ our target dir. The dir can be any folder in the tree, so use
            // its own document id rather than the tree's.
            var dirChildrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(dirUri, DocumentsContract.GetDocumentId(dirUri))!;

            // Query the contents of the target dir via the contents url. We want the document IDs to cache and use later.
            // Note that the file system provider that is used by our ContentResolver's Query() method seems† to ignore any selection
            // and order clauses that we pass to it, so we have to get everything and do filtering/ordering ourselves.
            // † By experiment, and also see https://stackoverflow.com/a/61214849/67316
            var projection = new string[]
            { 
                DocumentsContract.Document.ColumnDocumentId,
                DocumentsContract.Document.ColumnMimeType,
                DocumentsContract.Document.ColumnLastModified,
                DocumentsContract.Document.ColumnDisplayName,
            };
            using (var cursor = context.ContentResolver!.Query(dirChildrenUri, projection, null, null, null)
                                ?? throw new InvalidOperationException($"No cursor for the contents of {dirUri}"))
            {
                // Read the whole listing, because the subfolders are selected from it too.
                var entries = ReadEntries(cursor).ToList();
                var documents = DocumentSelection.Select(entries, fileTypes, sortField, sortOrder, includeHiddenFiles);
                var folders = DocumentSelection.SelectFolders(entries, sortField, sortOrder, includeHiddenFolders);

                // Count the file extnsions
                var fileExtensionCounts = DocumentSelection.CountExtensions(documents.Select(doc => doc.DisplayName));

                // Instantiate a DocumentFileCollection object with the document ids and display names from the entry list.
                return new DocumentCollection(dirUri,
                                              documents.Select(t => t.DocumentId).ToArray(),
                                              documents.Select(t => t.DisplayName).ToArray(),
                                              fileExtensionCounts,
                                              folders.Select(t => t.DocumentId).ToArray(),
                                              folders.Select(t => t.DisplayName).ToArray());
            }
        }


        /// <summary>
        /// Read directory entries from a cursor over the projection used in CreateImpl().
        /// Entries are read lazily, so a caller that stops early doesn't read the rest of the cursor.
        /// </summary>
        private static IEnumerable<DocumentEntry> ReadEntries(Android.Database.ICursor cursor)
        {
            while (cursor.MoveToNext())
            {
                var documentId = cursor.GetString(0);
                if (documentId == null)
                    continue;  // Can't be opened, so ignore it.

                yield return new DocumentEntry(
                    DocumentId: documentId,
                    DisplayName: cursor.GetString(3) ?? string.Empty,
                    LastModified: cursor.GetLong(2),
                    IsDirectory: cursor.GetString(1) == DocumentsContract.Document.MimeTypeDir);
            }
        }

        #endregion Implementaton
    }
}