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

            protected Entry(FileSystem owner, DirectoryEntry? parent, string name, string documentId)
            {
                Owner = owner;
                _parent = parent;
                Name = name;
                DocumentId = documentId;
                Path = parent == null ? RootPath : CombinePath(parent.Path, name);
            }

            protected FileSystem Owner { get; }

            protected string DocumentId { get; }

            protected Android.Net.Uri DocumentUri => Owner.DocumentUri(DocumentId);

            protected abstract bool IsDirectory { get; }

            public string Name { get; }

            public string Path { get; }

            public IDirectory? Parent => _parent;

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

            // The SAF doesn't record creation times, so the last-modified time stands in for it.
            public Task<DateTime> GetCreationTimeUtcAsync(CancellationToken cancellationToken = default)
            {
                return GetLastWriteTimeUtcAsync(cancellationToken);
            }

            public Task<DateTime> GetLastWriteTimeUtcAsync(CancellationToken cancellationToken = default)
            {
                return RunAsync(() => FromUnixTime(GetExistingDetails().LastModified), cancellationToken);
            }

            public Task SetAttributesAsync(FileAttributes attributes, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken);
            }

            public Task SetCreationTimeUtcAsync(DateTime creationTimeUtc, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken);
            }

            public Task SetLastWriteTimeUtcAsync(DateTime lastWriteTimeUtc, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken);
            }

            public Task RenameAsync(string newName, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken, () => ValidateName(newName, nameof(newName)));
            }

            public Task MoveToAsync(IDirectory destination, CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken, () => ArgumentNullException.ThrowIfNull(destination));
            }

            public override string ToString() => Path;

            /// <summary>
            /// Creates the exception thrown when this entry's document doesn't exist.
            /// </summary>
            protected abstract IOException NotFound(Exception? innerException);

            protected void EnsureExists()
            {
                GetExistingDetails();
            }

            // A document that has become the other kind of entry counts as missing.
            protected DocumentDetails GetExistingDetails()
            {
                var details = Owner.QueryDocument(DocumentId);
                if (details == null || details.Value.IsDirectory != IsDirectory)
                    throw NotFound(null);
                return details.Value;
            }

            protected async Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                return await RunTranslatedAsync(() => Task.FromResult(operation()), NotFound, cancellationToken)
                    .ConfigureAwait(false);
            }

            protected async Task<T> RunStreamingAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                return await RunTranslatedAsync(operation, NotFound, cancellationToken).ConfigureAwait(false);
            }

            // Write operations make the usual checks, in the same order as the other
            // implementations, and then fail because the file system is read-only.
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

            protected Task RejectWriteAsync(CancellationToken cancellationToken, Action? validateArguments = null)
            {
                return RejectWriteAsync<bool>(cancellationToken, validateArguments);
            }
        }
    }
}
