using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Saraka.Abstractions;

namespace Saraka.SystemIO
{
    public sealed partial class FileSystem
    {
        private sealed class FileEntry : Entry, IFile
        {
            // StreamReader skips a leading byte order mark only if it matches this encoding's
            // preamble, so the encoding must have one. Byte order mark detection stays off, so a
            // UTF-16 or UTF-32 mark never changes the decoding.
            private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

            public FileEntry(FileSystem owner, string path)
                : base(owner, path)
            {
            }

            public Task<long> GetLengthAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareRead(cancellationToken);
                    return new FileInfo(FullPath).Length;
                });
            }

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

            public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareRead(cancellationToken);
                    return (Stream)OpenForRead();
                });
            }

            public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareWrite(cancellationToken);
                    return (Stream)new FileStream(FullPath, FileMode.Truncate, FileAccess.Write, FileShare.None,
                        StreamBufferSize, FileOptions.Asynchronous);
                });
            }

            public Task DeleteAsync(CancellationToken cancellationToken = default)
            {
                return Complete(() =>
                {
                    PrepareWrite(cancellationToken);
                    File.Delete(FullPath);
                });
            }

            protected override FileSystemInfo CreateInfo() => new FileInfo(FullPath);

            protected override IOException NotFound() =>
                new FileNotFoundException($"The file '{Path}' does not exist.");

            protected override void MoveOnDisk(string sourceFullPath, string destinationFullPath) =>
                File.Move(sourceFullPath, destinationFullPath);

            private void PrepareRead(CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                EnsureExists();
            }

            private void PrepareWrite(CancellationToken cancellationToken)
            {
                Owner.ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                Owner.EnsureWritable();
                EnsureExists();
            }

            private FileStream OpenForRead()
            {
                return new FileStream(FullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    StreamBufferSize, FileOptions.Asynchronous);
            }
        }
    }
}
