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
    /// <remarks>
    /// <para>
    /// Methods that take a name expect a bare name, without any path. They throw
    /// <see cref="ArgumentException"/> if the name is empty, contains "/", is "." or "..", or is
    /// otherwise not a valid name in this file system (for example, it contains a character the
    /// underlying storage doesn't allow).
    /// </para>
    /// <para>
    /// <see cref="GetEntriesAsync"/>, <see cref="EnumerateFilesAsync"/> and
    /// <see cref="EnumerateDirectoriesAsync"/> return entries in no particular order and include
    /// every entry, whatever its attributes, so hidden and system entries are included. In a
    /// search pattern, "*" matches zero or more characters and "?" matches exactly one. Whether
    /// matching is case-sensitive follows <see cref="IFileSystem.NameComparer"/>.
    /// </para>
    /// <para>
    /// The only entries left out are ones the file system can't present: an entry whose stored
    /// name isn't a valid name (for example, it contains "/"), and an entry whose name is the
    /// same as an earlier entry of the same kind, as <see cref="IFileSystem.NameComparer"/>
    /// compares names. A consumer can't find out about these through Saraka. They can't be found by name either, so the
    /// methods that return entries and the lookups by name always agree. Each implementation
    /// documents which stored names it can't present.
    /// </para>
    /// </remarks>
    public interface IDirectory : IFileSystemEntry
    {
        /// <summary>
        /// Lists the files and subdirectories directly contained in this directory.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="DirectoryNotFoundException">This directory no longer exists.</exception>
        Task<IEnumerable<IFileSystemEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the subdirectory with the given name.
        /// </summary>
        /// <param name="name">The name of the subdirectory, without any path.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid name.</exception>
        /// <exception cref="DirectoryNotFoundException">
        /// This directory has no subdirectory named <paramref name="name"/>, or this directory no
        /// longer exists.
        /// </exception>
        Task<IDirectory> GetDirectoryAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the file with the given name.
        /// </summary>
        /// <param name="name">The name of the file, without any path.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid name.</exception>
        /// <exception cref="FileNotFoundException">
        /// This directory has no file named <paramref name="name"/>.
        /// </exception>
        /// <exception cref="DirectoryNotFoundException">This directory no longer exists.</exception>
        Task<IFile> GetFileAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the files directly contained in this directory whose names match a search pattern.
        /// </summary>
        /// <param name="searchPattern">
        /// The search pattern (e.g. "*.txt"). "*" matches every file.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="searchPattern"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="searchPattern"/> contains "/" or is otherwise not valid in this file
        /// system.
        /// </exception>
        /// <exception cref="DirectoryNotFoundException">This directory no longer exists.</exception>
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
        /// <exception cref="ArgumentException">
        /// <paramref name="searchPattern"/> contains "/" or is otherwise not valid in this file
        /// system.
        /// </exception>
        /// <exception cref="DirectoryNotFoundException">This directory no longer exists.</exception>
        Task<IEnumerable<IDirectory>> EnumerateDirectoriesAsync(string searchPattern = "*", CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an empty subdirectory.
        /// </summary>
        /// <param name="name">The name of the new subdirectory, without any path.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The new subdirectory.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid name.</exception>
        /// <exception cref="InvalidOperationException">
        /// The file system is not writable, or an entry named <paramref name="name"/> already
        /// exists in this directory.
        /// </exception>
        /// <exception cref="DirectoryNotFoundException">This directory no longer exists.</exception>
        Task<IDirectory> CreateDirectoryAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an empty file.
        /// </summary>
        /// <param name="name">The name of the new file, without any path.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The new file.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid name.</exception>
        /// <exception cref="InvalidOperationException">
        /// The file system is not writable, or an entry named <paramref name="name"/> already
        /// exists in this directory.
        /// </exception>
        /// <exception cref="DirectoryNotFoundException">This directory no longer exists.</exception>
        Task<IFile> CreateFileAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a file containing the data read from <paramref name="content"/>, from its
        /// current position to its end. If the operation fails or is cancelled after the file has
        /// been created, implementations should delete the partly written file.
        /// </summary>
        /// <param name="name">The name of the new file, without any path.</param>
        /// <param name="content">The stream to read the file's content from.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The new file.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="name"/> or <paramref name="content"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="name"/> is not a valid name, or <paramref name="content"/> can't be read.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The file system is not writable, or an entry named <paramref name="name"/> already
        /// exists in this directory.
        /// </exception>
        /// <exception cref="DirectoryNotFoundException">This directory no longer exists.</exception>
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
        /// <exception cref="DirectoryNotFoundException">This directory no longer exists.</exception>
        Task DeleteAsync(bool recursive = false, CancellationToken cancellationToken = default);
    }
}
