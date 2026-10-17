using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Saraka.Abstractions
{
    /// <summary>
    /// An open file system. Dispose it when it is no longer needed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Some implementations write changes to storage only when the file system is disposed, so
    /// always dispose it.
    /// </para>
    /// <para>
    /// After the file system has been disposed, operations on it and on its entries throw
    /// <see cref="ObjectDisposedException"/>. Whether streams that are already open remain usable
    /// depends on the implementation.
    /// </para>
    /// </remarks>
    public interface IFileSystem : IAsyncDisposable
    {
        /// <summary>
        /// The file system's root directory. Its path is "/", its name is the empty string, and its
        /// parent is null.
        /// </summary>
        IDirectory Root { get; }

        /// <summary>
        /// True if the file system can be written to; otherwise false. This can change while the
        /// file system is open. Write operations attempted while it is false throw
        /// <see cref="InvalidOperationException"/>.
        /// </summary>
        bool CanWrite { get; }

        /// <summary>
        /// How this file system compares names, and so paths: for example, whether case matters.
        /// Lookups by name and the rule for leaving out entries whose names are the same both use
        /// it, and search patterns match with the same case sensitivity. Use it to compare names
        /// and paths from this file system.
        /// </summary>
        StringComparer NameComparer { get; }

        /// <summary>
        /// A name for the file system that people can recognise, such as the name of the folder
        /// or file it's backed by. It's for display only, and isn't part of any path: the root
        /// directory's <see cref="IFileSystemEntry.Name"/> is still the empty string. It may be the
        /// empty string if the file system has no suitable name.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Gets the directory at a path. Names along the path are compared using
        /// <see cref="NameComparer"/>, as by <see cref="IDirectory.GetDirectoryAsync"/>.
        /// </summary>
        /// <param name="path">
        /// An absolute path, such as "/data/photos". "/" is the root directory.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The directory.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="path"/> isn't a valid path (see <see cref="SarakaPath.Split"/>), or a
        /// name along it isn't valid in this file system.
        /// </exception>
        /// <exception cref="DirectoryNotFoundException">There's no directory at that path.</exception>
        Task<IDirectory> GetDirectoryAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the file at a path. Names along the path are compared using
        /// <see cref="NameComparer"/>, as by <see cref="IDirectory.GetFileAsync"/>.
        /// </summary>
        /// <param name="path">An absolute path, such as "/data/photos/img1.jpg".</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The file.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="path"/> isn't a valid path (see <see cref="SarakaPath.Split"/>), is "/",
        /// or has a name that isn't valid in this file system.
        /// </exception>
        /// <exception cref="FileNotFoundException">There's no file at that path.</exception>
        /// <exception cref="DirectoryNotFoundException">A directory along the path doesn't exist.</exception>
        Task<IFile> GetFileAsync(string path, CancellationToken cancellationToken = default);
    }
}
