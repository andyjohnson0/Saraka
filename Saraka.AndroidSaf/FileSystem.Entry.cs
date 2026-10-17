using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Saraka.Abstractions;


namespace Saraka.AndroidSaf
{
    public sealed partial class FileSystem
    {
        /// <summary>
        /// The behaviour shared by files and directories. An entry holds its document ID and the
        /// path it was found at, and queries the provider each time an operation runs.
        /// </summary>
        private abstract class Entry : IFileSystemEntry
        {
            private readonly DirectoryEntry? _parent;

            /// <summary>
            /// Creates an entry for a document found in a listing.
            /// </summary>
            /// <param name="owner">The file system the entry belongs to.</param>
            /// <param name="parent">The directory the document was found in, or null for the root.</param>
            /// <param name="name">The document's display name, or "" for the root.</param>
            /// <param name="documentId">The provider's document ID.</param>
            protected Entry(FileSystem owner, DirectoryEntry? parent, string name, string documentId)
            {
                Owner = owner;
                _parent = parent;
                Name = name;
                DocumentId = documentId;
                Path = parent == null ? RootPath : CombinePath(parent.Path, name);
            }

            /// <summary>
            /// The file system the entry belongs to.
            /// </summary>
            protected FileSystem Owner { get; }

            /// <summary>
            /// The provider's document ID for this entry.
            /// </summary>
            protected string DocumentId { get; }

            /// <summary>
            /// True if this entry is a directory.
            /// </summary>
            protected abstract bool IsDirectory { get; }

            /// <inheritdoc/>
            public string Name { get; }

            /// <inheritdoc/>
            public string Path { get; }

            /// <inheritdoc/>
            public IDirectory? Parent => _parent;

            /// <inheritdoc/>
            public IFileSystem FileSystem => Owner;

            /// <inheritdoc/>
            public Task<FileAttributes> GetAttributesAsync(CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    EnsureExists();
                    var attributes = IsDirectory ? FileAttributes.Directory : FileAttributes.Normal;
                    if (Name.StartsWith('.'))
                        attributes = IsDirectory ? attributes | FileAttributes.Hidden : FileAttributes.Hidden;
                    return attributes;
                }, cancellationToken);
            }

            /// <inheritdoc/>
            /// <remarks>The SAF doesn't record creation times, so the last-modified time stands in for it.</remarks>
            public Task<DateTime> GetCreationTimeUtcAsync(CancellationToken cancellationToken = default)
            {
                return GetLastWriteTimeUtcAsync(cancellationToken);
            }

            /// <inheritdoc/>
            public Task<DateTime> GetLastWriteTimeUtcAsync(CancellationToken cancellationToken = default)
            {
                return RunAsync(() => FromUnixTime(GetExistingDetails().LastModified), cancellationToken);
            }

            /// <inheritdoc/>
            public Task SetAttributesAsync(FileAttributes attributes, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken);
            }

            /// <inheritdoc/>
            public Task SetCreationTimeUtcAsync(DateTime creationTimeUtc, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken);
            }

            /// <inheritdoc/>
            public Task SetLastWriteTimeUtcAsync(DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken);
            }

            /// <inheritdoc/>
            public Task RenameAsync(string newName, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken, () => ValidateName(newName, nameof(newName)));
            }

            /// <inheritdoc/>
            public Task MoveToAsync(IDirectory destination, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken, () => ArgumentNullException.ThrowIfNull(destination));
            }

            /// <summary>
            /// Queries the provider for this entry's document, on the thread pool.
            /// </summary>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The document's details.</returns>
            /// <exception cref="IOException">The document doesn't exist; see <see cref="NotFound"/>.</exception>
            public Task<DocumentDetails> GetDetailsAsync(CancellationToken cancellationToken)
            {
                return RunAsync(GetExistingDetails, cancellationToken);
            }

            /// <summary>
            /// Returns the entry's path.
            /// </summary>
            /// <returns>The entry's Saraka path.</returns>
            public override string ToString() => Path;

            /// <summary>
            /// Creates the exception thrown when this entry's document doesn't exist.
            /// </summary>
            /// <param name="innerException">The exception that reported it, if any.</param>
            /// <returns>A <see cref="FileNotFoundException"/> or <see cref="DirectoryNotFoundException"/> naming the path.</returns>
            protected abstract IOException NotFound(Exception? innerException);

            /// <summary>
            /// Throws if this entry's document no longer exists.
            /// </summary>
            /// <exception cref="IOException">The document doesn't exist; see <see cref="NotFound"/>.</exception>
            protected void EnsureExists()
            {
                GetExistingDetails();
            }

            /// <summary>
            /// Queries the provider for this entry's document. A document that has become the other
            /// kind of entry counts as missing.
            /// </summary>
            /// <returns>The document's details.</returns>
            /// <exception cref="IOException">The document doesn't exist; see <see cref="NotFound"/>.</exception>
            protected DocumentDetails GetExistingDetails()
            {
                var details = FromSource(() => Owner._source.QueryDocument(DocumentId));
                if (details == null || details.Value.IsDirectory != IsDirectory)
                    throw NotFound(null);
                return details.Value;
            }

            /// <summary>
            /// Calls the document source, reporting a missing document as this entry's own
            /// not-found exception.
            /// </summary>
            /// <typeparam name="T">The call's result type.</typeparam>
            /// <param name="call">The call to make.</param>
            /// <returns>The call's result.</returns>
            protected T FromSource<T>(Func<T> call)
            {
                try
                {
                    return call();
                }
                catch (FileNotFoundException ex)
                {
                    throw NotFound(ex);
                }
                catch (DirectoryNotFoundException ex)
                {
                    throw NotFound(ex);
                }
            }

            /// <summary>
            /// Like <see cref="FromSource{T}"/>, for reading a stream from the source.
            /// </summary>
            /// <typeparam name="T">The call's result type.</typeparam>
            /// <param name="call">The asynchronous call to make.</param>
            /// <returns>The call's result.</returns>
            protected async Task<T> FromSourceAsync<T>(Func<Task<T>> call)
            {
                try
                {
                    return await call().ConfigureAwait(false);
                }
                catch (FileNotFoundException ex)
                {
                    throw NotFound(ex);
                }
                catch (DirectoryNotFoundException ex)
                {
                    throw NotFound(ex);
                }
            }

            /// <summary>
            /// Runs a synchronous operation on the thread pool, because provider calls block.
            /// </summary>
            /// <typeparam name="T">The operation's result type.</typeparam>
            /// <param name="operation">The operation.</param>
            /// <param name="cancellationToken">A token to cancel the operation before it starts.</param>
            /// <returns>The operation's result.</returns>
            protected Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
            {
                return RunStreamingAsync(() => Task.FromResult(operation()), cancellationToken);
            }

            /// <summary>
            /// Runs an asynchronous operation on the thread pool, after checking that the file
            /// system hasn't been disposed and the operation hasn't been cancelled.
            /// </summary>
            /// <typeparam name="T">The operation's result type.</typeparam>
            /// <param name="operation">The operation.</param>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            /// <returns>The operation's result.</returns>
            protected async Task<T> RunStreamingAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                return await Task.Run(operation, cancellationToken).ConfigureAwait(false);
            }

            /// <summary>
            /// Fails a write operation. It makes the usual checks, in the same order as the other
            /// implementations, and then fails because the file system is read-only.
            /// </summary>
            /// <typeparam name="T">The write operation's result type.</typeparam>
            /// <param name="cancellationToken">The operation's cancellation token.</param>
            /// <param name="validateArguments">Checks the operation's arguments, or null if it has none.</param>
            /// <returns>A faulted or cancelled task.</returns>
            protected Task<T> RejectWriteAsync<T>(CancellationToken cancellationToken, Action? validateArguments = null)
            {
                try
                {
                    Owner.ThrowIfDisposed();
                    validateArguments?.Invoke();
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("The file system is not writable.");
                }
                catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
                {
                    return Task.FromCanceled<T>(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    return Task.FromException<T>(ex);
                }
            }

            /// <summary>
            /// Fails a write operation that has no result; see <see cref="RejectWriteAsync{T}"/>.
            /// </summary>
            /// <param name="cancellationToken">The operation's cancellation token.</param>
            /// <param name="validateArguments">Checks the operation's arguments, or null if it has none.</param>
            /// <returns>A faulted or cancelled task.</returns>
            protected Task RejectWriteAsync(CancellationToken cancellationToken, Action? validateArguments = null)
            {
                return RejectWriteAsync<bool>(cancellationToken, validateArguments);
            }
        }
    }
}
