using System;
using System.Collections.Generic;
using System.Text;

namespace Saraka.AndroidSaf
{
    /// <summary>
    /// Filters and sorts the documents in a directory.
    /// </summary>
    internal static class DocumentSelection
    {
        public const string JpegExtension = "jpeg";  // canonical extension
        public const string JpgExtension = "jpg";    // Synonym only


        /// <summary>
        /// Expand a set of file types to include synonyms (e.g. "jpg" for "jpeg").
        /// </summary>
        /// <param name="fileTypes">File types, without periods. Can be null.</param>
        /// <returns>Expanded list of file types. Null if fileTypes is null, meaning all files.</returns>
        public static List<string>? ExpandFileTypes(IEnumerable<string>? fileTypes)
        {
            if (fileTypes == null)
                return null;  // An empty list would select no files at all.

            var ft = new List<string>(fileTypes);
            if (ft.Contains(JpegExtension, StringComparer.OrdinalIgnoreCase))
                ft.Add(JpgExtension);  // synonym
            return ft;
        }


        /// <summary>
        /// Select the file documents from a directory listing and sort them.
        /// Directories are excluded, and so are hidden files (display name starts with a period) unless includeHidden is
        /// true.
        /// </summary>
        /// <param name="entries">Directory listing, in content resolver order</param>
        /// <param name="fileTypes">File types to include (e.g. "jpeg"), without periods. Pass null for all files.</param>
        /// <param name="sortField">Field to sort on.</param>
        /// <param name="sortOrder">Order to sort.</param>
        /// <param name="includeHidden">True to include hidden files.</param>
        /// <returns>Selected documents, in sorted order.</returns>
        public static List<DocumentEntry> Select(
            IEnumerable<DocumentEntry> entries,
            IEnumerable<string>? fileTypes,
            SortField sortField,
            SortOrder sortOrder,
            bool includeHidden = false)
        {
            ArgumentNullException.ThrowIfNull(entries);

            var documents = new List<DocumentEntry>();
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                    continue;  // Ignore directories.
                if (!includeHidden && IsHidden(entry))
                    continue;  // Ignore hidden files.
                if (fileTypes != null && !fileTypes.Any(s => entry.DisplayName.EndsWith('.' + s, StringComparison.CurrentCultureIgnoreCase)))
                    continue;  // File types is specified and display name doesn't end with any of them.

                documents.Add(entry);
            }

            return Sort(documents, sortField, sortOrder);
        }


        /// <summary>
        /// Select the folders from a directory listing and sort them.
        /// Hidden folders (display name starts with a period) are excluded unless includeHidden is true.
        /// The SAF has no creation date, so sorting by date uses the last modified date, as for files.
        /// </summary>
        /// <param name="entries">Directory listing, in content resolver order</param>
        /// <param name="sortField">Field to sort on.</param>
        /// <param name="sortOrder">Order to sort.</param>
        /// <param name="includeHidden">True to include hidden folders.</param>
        /// <returns>Selected folders, in sorted order.</returns>
        public static List<DocumentEntry> SelectFolders(
            IEnumerable<DocumentEntry> entries,
            SortField sortField,
            SortOrder sortOrder,
            bool includeHidden = false)
        {
            ArgumentNullException.ThrowIfNull(entries);

            var folders = entries.Where(e => e.IsDirectory && (includeHidden || !IsHidden(e))).ToList();
            return Sort(folders, sortField, sortOrder);
        }


        /// <summary>
        /// Determines whether a document is hidden.
        /// </summary>
        /// <param name="entry">The document.</param>
        /// <returns>True if its display name starts with a period.</returns>
        private static bool IsHidden(DocumentEntry entry)
        {
            return entry.DisplayName.StartsWith('.');
        }


        /// <summary>
        /// Sorts documents. Sorting by name ignores case, so that the order is what people expect.
        /// Documents that compare equal keep their order.
        /// </summary>
        /// <param name="documents">The documents.</param>
        /// <param name="sortField">Field to sort on.</param>
        /// <param name="sortOrder">Order to sort.</param>
        /// <returns>The sorted documents.</returns>
        private static List<DocumentEntry> Sort(
            List<DocumentEntry> documents,
            SortField sortField,
            SortOrder sortOrder)
        {
            if (sortField == SortField.FileName)
            {
                if (sortOrder == SortOrder.Ascending)
                {
                    documents = documents.OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
                }
                else if (sortOrder == SortOrder.Descending)
                {
                    documents = documents.OrderByDescending(d => d.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
                }
            }
            else if (sortField == SortField.FileDate)
            {
                if (sortOrder == SortOrder.Ascending)
                {
                    documents = documents.OrderBy(d => d.LastModified).ToList();
                }
                else if (sortOrder == SortOrder.Descending)
                {
                    documents = documents.OrderByDescending(d => d.LastModified).ToList();
                }
            }

            return documents;
        }
    }
}
