namespace Saraka.AndroidSaf
{
    // Completes the linked FileSystem class in this test assembly only, standing in for the
    // Android OpenAsync in Platform/FileSystem.Android.cs.
    public sealed partial class FileSystem
    {
        /// <summary>
        /// Opens a file system over a document source, as the Android OpenAsync does over the
        /// content resolver.
        /// </summary>
        internal static Task<FileSystem> OpenAsync(
            IDocumentSource source,
            FileSystemOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return OpenCoreAsync(source, options, cancellationToken);
        }
    }
}
