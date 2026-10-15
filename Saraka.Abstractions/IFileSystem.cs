using System;

namespace Saraka.Abstractions
{
    /// <summary>
    /// An open file system. Dispose it when it is no longer needed.
    /// </summary>
    public interface IFileSystem : IAsyncDisposable
    {
        /// <summary>
        /// The file system's root directory. Its path is "\" and its parent is null.
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
