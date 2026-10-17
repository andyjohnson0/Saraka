using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Android.Content;
using Android.Provider;


namespace Saraka.AndroidSaf
{
    public sealed partial class FileSystem
    {
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

            return await OpenCoreAsync(new SafDocumentSource(context, treeUri), options, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
