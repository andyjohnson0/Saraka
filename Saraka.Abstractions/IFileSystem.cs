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
    }
}
