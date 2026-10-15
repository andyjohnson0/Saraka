using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Saraka.Abstractions
{
    /// <summary>
    /// A file within a file system.
    /// </summary>
    public interface IFile : IFileSystemEntry
    {
        /// <summary>
        /// Gets the size of the file, in bytes.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="FileNotFoundException">This file no longer exists.</exception>
        Task<long> GetLengthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the entire contents of the file.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="FileNotFoundException">This file no longer exists.</exception>
        Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the entire contents of the file as UTF-8 text. A leading UTF-8 byte order mark is
        /// skipped.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="FileNotFoundException">This file no longer exists.</exception>
        Task<string> ReadAllTextAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a read-only stream over the file's contents. The caller is responsible for
        /// disposing the stream.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="FileNotFoundException">This file no longer exists.</exception>
        Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a writable stream over the file, truncating any existing content. The caller is
        /// responsible for disposing the stream.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="InvalidOperationException">The file system is not writable.</exception>
        /// <exception cref="FileNotFoundException">This file no longer exists.</exception>
        Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes this file.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="InvalidOperationException">The file system is not writable.</exception>
        /// <exception cref="FileNotFoundException">This file no longer exists.</exception>
        Task DeleteAsync(CancellationToken cancellationToken = default);
    }
}
