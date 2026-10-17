using System;

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
    }
}
