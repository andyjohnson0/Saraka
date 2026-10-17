using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Saraka.Abstractions;

namespace Saraka.SystemIO
{
    public sealed partial class FileSystem
    {
        /// <summary>
        /// A file in the file system.
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
            /// Creates an entry for the file at a Saraka path.
            /// </summary>
            /// <param name="owner">The file system the file belongs to.</param>
            /// <param name="path">The file's Saraka path.</param>
            public FileEntry(FileSystem owner, string path)
                : base(owner, path)
            {
            }

            /// <inheritdoc/>
            public Task<long> GetLengthAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareRead(cancellationToken);
                    return new FileInfo(FullPath).Length;
                });
            }

            /// <inheritdoc/>
            public async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
            {
                PrepareRead(cancellationToken);
                using (var stream = OpenForRead())
                using (var buffer = new MemoryStream())
                {
                    await stream.CopyToAsync(buffer, CopyBufferSize, cancellationToken).ConfigureAwait(false);
                    return buffer.ToArray();
                }
            }

            /// <inheritdoc/>
            public async Task<string> ReadAllTextAsync(CancellationToken cancellationToken = default)
            {
                PrepareRead(cancellationToken);
                using (var stream = OpenForRead())
                using (var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false))
                {
                    // StreamReader.ReadToEndAsync has no cancellable overload in .NET Standard 2.1.
                    var text = await reader.ReadToEndAsync().ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    return text;
                }
            }

            /// <inheritdoc/>
            public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareRead(cancellationToken);
                    return (Stream)OpenForRead();
                });
            }

            /// <inheritdoc/>
            public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareWrite(cancellationToken);
                    return (Stream)new FileStream(FullPath, FileMode.Truncate, FileAccess.Write, FileShare.None,
                        StreamBufferSize, FileOptions.Asynchronous);
                });
            }

            /// <inheritdoc/>
            public Task DeleteAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareWrite(cancellationToken);
                    File.Delete(FullPath);
                });
            }

            /// <inheritdoc/>
            protected override FileSystemInfo CreateInfo() => new FileInfo(FullPath);

            /// <inheritdoc/>
            protected override IOException NotFound() =>
                new FileNotFoundException($"The file '{Path}' does not exist.");

            /// <inheritdoc/>
            protected override void MoveOnDisk(string sourceFullPath, string destinationFullPath) =>
                File.Move(sourceFullPath, destinationFullPath);

            /// <summary>
            /// Makes the checks for reading this file.
            /// </summary>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            private void PrepareRead(CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExists();
            }

            /// <summary>
            /// Makes the checks for writing to or deleting this file.
            /// </summary>
            /// <param name="cancellationToken">A token to cancel the operation.</param>
            private void PrepareWrite(CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                Owner.EnsureWritable();
                EnsureExists();
            }

            /// <summary>
            /// Opens this file for asynchronous reading, sharing it with other readers.
            /// </summary>
            /// <returns>A read-only stream over the file.</returns>
            private FileStream OpenForRead()
            {
                return new FileStream(FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    StreamBufferSize, FileOptions.Asynchronous);
            }
        }
    }
}
