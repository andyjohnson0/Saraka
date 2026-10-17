using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Saraka.Abstractions;


namespace Saraka.AndroidSaf
{
    public sealed partial class FileSystem
    {
        /// <summary>
        /// A file in the document tree.
        /// </summary>
        private sealed class FileEntry : Entry, IFile
        {
            /// <summary>
            /// The encoding for <see cref="ReadAllTextAsync"/>. StreamReader skips a leading byte
            /// order mark only if it matches the encoding's preamble, so the encoding must have one.
            /// Byte order mark detection stays off, so a UTF-16 or UTF-32 mark never changes the
            /// decoding.
            /// </summary>
            private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

            /// <summary>
            /// Creates an entry for a file.
            /// </summary>
            /// <param name="owner">The file system the file belongs to.</param>
            /// <param name="parent">The folder it was found in.</param>
            /// <param name="name">The file's display name.</param>
            /// <param name="documentId">The provider's document ID.</param>
            public FileEntry(FileSystem owner, DirectoryEntry parent, string name, string documentId)
                : base(owner, parent, name, documentId)
            {
            }

            /// <inheritdoc/>
            protected override bool IsDirectory => false;

            /// <inheritdoc/>
            /// <remarks>Providers needn't report a size, and then the only way to find it is to read the file.</remarks>
            public Task<long> GetLengthAsync(CancellationToken cancellationToken = default)
            {
                return RunStreamingAsync(async () =>
                {
                    var size = GetExistingDetails().Size;
                    if (size.HasValue)
                        return size.Value;

                    using var stream = OpenStream();
                    return await FromSourceAsync(async () =>
                    {
                        var buffer = new byte[CopyBufferSize];
                        long length = 0;
                        int read;
                        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                            length += read;
                        return length;
                    }).ConfigureAwait(false);
                }, cancellationToken);
            }

            /// <inheritdoc/>
            public Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
            {
                return RunStreamingAsync(async () =>
                {
                    EnsureExists();
                    using var stream = OpenStream();
                    using var buffer = new MemoryStream();
                    await FromSourceAsync(async () =>
                    {
                        await stream.CopyToAsync(buffer, CopyBufferSize, cancellationToken).ConfigureAwait(false);
                        return true;
                    }).ConfigureAwait(false);
                    return buffer.ToArray();
                }, cancellationToken);
            }

            /// <inheritdoc/>
            public Task<string> ReadAllTextAsync(CancellationToken cancellationToken = default)
            {
                return RunStreamingAsync(async () =>
                {
                    EnsureExists();
                    using var stream = OpenStream();
                    using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false);
                    return await FromSourceAsync(() => reader.ReadToEndAsync(cancellationToken)).ConfigureAwait(false);
                }, cancellationToken);
            }

            /// <inheritdoc/>
            public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    EnsureExists();
                    return OpenStream();
                }, cancellationToken);
            }

            /// <inheritdoc/>
            public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync<Stream>(cancellationToken);
            }

            /// <inheritdoc/>
            public Task DeleteAsync(CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken);
            }

            /// <inheritdoc/>
            protected override IOException NotFound(Exception? innerException) =>
                new FileNotFoundException($"The file '{Path}' does not exist.", innerException);

            /// <summary>
            /// Opens a read-only stream over this file through the document source.
            /// </summary>
            /// <returns>The stream.</returns>
            /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
            private Stream OpenStream()
            {
                return FromSource(() => Owner._source.OpenRead(DocumentId));
            }
        }
    }
}
