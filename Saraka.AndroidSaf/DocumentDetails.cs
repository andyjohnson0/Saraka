namespace Saraka.AndroidSaf
{
    /// <summary>
    /// The details of a single document, as reported by the document provider.
    /// </summary>
    /// <param name="IsDirectory">True if the document is a folder.</param>
    /// <param name="LastModified">Last modified time, in milliseconds since the epoch, or null if the provider doesn't record one.</param>
    /// <param name="Size">Size in bytes, or null if the provider doesn't report one.</param>
    /// <param name="DisplayName">Display name, or null if the provider doesn't report one.</param>
    internal readonly record struct DocumentDetails(
        bool IsDirectory,
        long? LastModified,
        long? Size,
        string? DisplayName);
}
