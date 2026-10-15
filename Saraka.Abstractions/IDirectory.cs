using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Saraka.Abstractions
{
    /// <summary>
    /// A directory within a file system.
    /// </summary>
    public interface IDirectory : IFileSystemEntry
    {
        /// <summary>
        /// Lists the files and subdirectories directly contained in this directory.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        Task<IEnumerable<IFileSystemEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the subdirectory with the given name.
        /// </summary>
        /// <param name="name">The name of the subdirectory, without any path.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> is empty or contains a path separator.
        /// </exception>
        /// <exception cref="DirectoryNotFoundException">
        /// This directory has no subdirectory named <paramref name="name"/>.
        /// </exception>
        Task<IDirectory> GetDirectoryAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the file with the given name.
        /// </summary>
        /// <param name="name">The name of the file, without any path.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> is empty or contains a path separator.
        /// </exception>
        /// <exception cref="FileNotFoundException">
        /// This directory has no file named <paramref name="name"/>.
        /// </exception>
        Task<IFile> GetFileAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the files directly contained in this directory whose names match a search pattern.
        /// </summary>
        /// <param name="searchPattern">The search pattern (e.g. "*.txt"). "*" matches every file.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="searchPattern"/> is null.</exception>
        Task<IEnumerable<IFile>> EnumerateFilesAsync(string searchPattern = "*", CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the subdirectories directly contained in this directory whose names match a search
        /// pattern.
        /// </summary>
        /// <param name="searchPattern">
        /// The search pattern (e.g. "data*"). "*" matches every subdirectory.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="searchPattern"/> is null.</exception>
        Task<IEnumerable<IDirectory>> EnumerateDirectoriesAsync(string searchPattern = "*", CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an empty subdirectory.
        /// </summary>
        /// <param name="name">The name of the new subdirectory, without any path.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The new subdirectory.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> is empty or contains a path separator.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The file system is not writable, or an entry named <paramref name="name"/> already
        /// exists in this directory.
        /// </exception>
        Task<IDirectory> CreateDirectoryAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an empty file.
        /// </summary>
        /// <param name="name">The name of the new file, without any path.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The new file.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> is empty or contains a path separator.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The file system is not writable, or an entry named <paramref name="name"/> already
        /// exists in this directory.
        /// </exception>
        Task<IFile> CreateFileAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a file containing the data read from <paramref name="content"/>, from its
        /// current position to its end.
        /// </summary>
        /// <param name="name">The name of the new file, without any path.</param>
        /// <param name="content">The stream to read the file's content from.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The new file.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="name"/> or <paramref name="content"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> is empty or contains a path separator.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The file system is not writable, or an entry named <paramref name="name"/> already
        /// exists in this directory.
        /// </exception>
        Task<IFile> CreateFileAsync(string name, Stream content, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes this directory.
        /// </summary>
        /// <param name="recursive">
        /// True to delete this directory and everything in it; false to delete it only if it is
        /// empty.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="InvalidOperationException">
        /// The file system is not writable, this directory is the root directory, or
        /// <paramref name="recursive"/> is false and this directory is not empty.
        /// </exception>
        Task DeleteAsync(bool recursive = false, CancellationToken cancellationToken = default);
    }
}
