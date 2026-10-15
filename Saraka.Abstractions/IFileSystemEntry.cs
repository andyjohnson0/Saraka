using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Saraka.Abstractions
{
    /// <summary>
    /// A named entry within a file system: either an <see cref="IDirectory"/> or an
    /// <see cref="IFile"/>.
    /// </summary>
    public interface IFileSystemEntry
    {
        /// <summary>
        /// The entry's name, without any path.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The entry's full path within the file system, rooted at "\" (e.g. "\data\file.txt").
        /// The root directory's path is "\".
        /// </summary>
        string Path { get; }

        /// <summary>
        /// The directory containing this entry, or null if this entry is the root directory.
        /// </summary>
        IDirectory Parent { get; }

        /// <summary>
        /// Gets the entry's attributes.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        Task<FileAttributes> GetAttributesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the time the entry was created, in UTC.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        Task<DateTime> GetCreationTimeUtcAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the time the entry was last written to, in UTC.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        Task<DateTime> GetLastWriteTimeUtcAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Renames this entry within its parent directory. On success, this instance's
        /// <see cref="Name"/> and <see cref="Path"/> reflect the new name; other instances
        /// referring to the original path are not updated.
        /// </summary>
        /// <param name="newName">The new name, without any path.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="newName"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="newName"/> is empty or contains a path separator.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The file system is not writable, this entry is the root directory, or an entry named
        /// <paramref name="newName"/> already exists in the parent directory.
        /// </exception>
        Task RenameAsync(string newName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves this entry into another directory, keeping its name. On success, this instance's
        /// <see cref="Path"/> and <see cref="Parent"/> reflect the new location; other instances
        /// referring to the original path are not updated.
        /// </summary>
        /// <param name="destination">The directory to move this entry into.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// The file system is not writable, this entry is the root directory, an entry with the same
        /// name already exists in <paramref name="destination"/>, this entry is a directory and
        /// <paramref name="destination"/> is that directory or one of its descendants, or
        /// <paramref name="destination"/> belongs to a different file system.
        /// </exception>
        Task MoveToAsync(IDirectory destination, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the entry's attributes.
        /// </summary>
        /// <param name="attributes">The attributes to set.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="InvalidOperationException">The file system is not writable.</exception>
        Task SetAttributesAsync(FileAttributes attributes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the time the entry was created, in UTC.
        /// </summary>
        /// <param name="creationTimeUtc">The creation time to set.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="InvalidOperationException">The file system is not writable.</exception>
        Task SetCreationTimeUtcAsync(DateTime creationTimeUtc, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the time the entry was last written to, in UTC. Creating or writing to a file
        /// updates this automatically; use this method to set a specific value.
        /// </summary>
        /// <param name="lastWriteTimeUtc">The last-write time to set.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="InvalidOperationException">The file system is not writable.</exception>
        Task SetLastWriteTimeUtcAsync(DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default);
    }
}
