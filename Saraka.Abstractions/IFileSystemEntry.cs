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
    /// <remarks>
    /// <para>
    /// An entry refers to a location in the file system. It is not a handle and doesn't lock
    /// anything, so the file or directory it refers to can be changed or removed by other code. An
    /// operation on an entry that no longer exists throws <see cref="FileNotFoundException"/> for
    /// a file or <see cref="DirectoryNotFoundException"/> for a directory.
    /// </para>
    /// <para>
    /// Paths always use "/" as the separator, whatever the host operating system.
    /// </para>
    /// <para>
    /// Exceptions are reported through the returned task. Besides the exceptions documented on
    /// each member, any operation can throw <see cref="ObjectDisposedException"/> after its
    /// <see cref="IFileSystem"/> has been disposed, <see cref="OperationCanceledException"/>
    /// when it is cancelled, and <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when the underlying storage fails or denies
    /// access.
    /// </para>
    /// <para>
    /// Some file systems can't store every value. An implementation may store times with less
    /// precision or a smaller range, store only some attributes, or ignore values it can't store
    /// at all. Each implementation documents what it does.
    /// </para>
    /// </remarks>
    public interface IFileSystemEntry
    {
        /// <summary>
        /// The entry's name, without any path. The root directory's name is the empty string.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The entry's full path within the file system, rooted at "/" (e.g. "/data/file.txt").
        /// The root directory's path is "/".
        /// </summary>
        string Path { get; }

        /// <summary>
        /// The directory containing this entry, or null if this entry is the root directory.
        /// Implementations may return a new instance on each access, so compare entries by
        /// <see cref="Path"/> rather than by reference.
        /// </summary>
        IDirectory Parent { get; }

        /// <summary>
        /// Gets the entry's attributes.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="FileNotFoundException">This entry is a file that no longer exists.</exception>
        /// <exception cref="DirectoryNotFoundException">This entry is a directory that no longer exists.</exception>
        Task<FileAttributes> GetAttributesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the time the entry was created, in UTC. The returned value's
        /// <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Utc"/>.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="FileNotFoundException">This entry is a file that no longer exists.</exception>
        /// <exception cref="DirectoryNotFoundException">This entry is a directory that no longer exists.</exception>
        Task<DateTime> GetCreationTimeUtcAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the time the entry was last written to, in UTC. The returned value's
        /// <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Utc"/>.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="FileNotFoundException">This entry is a file that no longer exists.</exception>
        /// <exception cref="DirectoryNotFoundException">This entry is a directory that no longer exists.</exception>
        Task<DateTime> GetLastWriteTimeUtcAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Renames this entry within its parent directory. On success, this instance's
        /// <see cref="Name"/> and <see cref="Path"/> reflect the new name; other instances
        /// referring to the original path are not updated. Renaming an entry to its current name
        /// succeeds and does nothing.
        /// </summary>
        /// <param name="newName">The new name, without any path.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="newName"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="newName"/> is empty, contains "/", is "." or "..", or is otherwise not a
        /// valid name in this file system.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The file system is not writable, this entry is the root directory, or another entry named
        /// <paramref name="newName"/> already exists in the parent directory.
        /// </exception>
        /// <exception cref="FileNotFoundException">This entry is a file that no longer exists.</exception>
        /// <exception cref="DirectoryNotFoundException">This entry is a directory that no longer exists.</exception>
        Task RenameAsync(string newName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves this entry into another directory, keeping its name. On success, this instance's
        /// <see cref="Path"/> and <see cref="Parent"/> reflect the new location; other instances
        /// referring to the original path are not updated. Moving an entry into the directory
        /// that already contains it succeeds and does nothing.
        /// </summary>
        /// <param name="destination">The directory to move this entry into.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// The file system is not writable, this entry is the root directory, another entry with
        /// the same name already exists in <paramref name="destination"/>, this entry is a
        /// directory and <paramref name="destination"/> is that directory or one of its
        /// descendants, or <paramref name="destination"/> belongs to a different file system.
        /// </exception>
        /// <exception cref="FileNotFoundException">This entry is a file that no longer exists.</exception>
        /// <exception cref="DirectoryNotFoundException">
        /// This entry is a directory that no longer exists, or <paramref name="destination"/> no
        /// longer exists.
        /// </exception>
        Task MoveToAsync(IDirectory destination, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the entry's attributes. Attributes the file system can't store are ignored.
        /// </summary>
        /// <param name="attributes">The attributes to set.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="InvalidOperationException">The file system is not writable.</exception>
        /// <exception cref="FileNotFoundException">This entry is a file that no longer exists.</exception>
        /// <exception cref="DirectoryNotFoundException">This entry is a directory that no longer exists.</exception>
        Task SetAttributesAsync(FileAttributes attributes, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the time the entry was created, in UTC. A value whose <see cref="DateTime.Kind"/>
        /// is <see cref="DateTimeKind.Unspecified"/> is treated as UTC; a
        /// <see cref="DateTimeKind.Local"/> value is converted to UTC. File systems that don't
        /// record creation times may ignore this; <see cref="GetCreationTimeUtcAsync"/> then
        /// returns the closest value they have.
        /// </summary>
        /// <param name="creationTimeUtc">The creation time to set.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="InvalidOperationException">The file system is not writable.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="creationTimeUtc"/> is outside the range of times the file system can
        /// store.
        /// </exception>
        /// <exception cref="FileNotFoundException">This entry is a file that no longer exists.</exception>
        /// <exception cref="DirectoryNotFoundException">This entry is a directory that no longer exists.</exception>
        Task SetCreationTimeUtcAsync(DateTime creationTimeUtc, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the time the entry was last written to, in UTC. Creating or writing to a file
        /// updates this automatically; use this method to set a specific value. A value whose
        /// <see cref="DateTime.Kind"/> is <see cref="DateTimeKind.Unspecified"/> is treated as UTC;
        /// a <see cref="DateTimeKind.Local"/> value is converted to UTC.
        /// </summary>
        /// <param name="lastWriteTimeUtc">The last-write time to set.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="InvalidOperationException">The file system is not writable.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="lastWriteTimeUtc"/> is outside the range of times the file system can
        /// store.
        /// </exception>
        /// <exception cref="FileNotFoundException">This entry is a file that no longer exists.</exception>
        /// <exception cref="DirectoryNotFoundException">This entry is a directory that no longer exists.</exception>
        Task SetLastWriteTimeUtcAsync(DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default);
    }
}
