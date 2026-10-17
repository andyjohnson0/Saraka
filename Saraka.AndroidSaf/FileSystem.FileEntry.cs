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
        private sealed class FileEntry : Entry, IFile
        {
            // StreamReader skips a leading byte order mark only if it matches this encoding's
            // preamble, so the encoding must have one. Byte order mark detection stays off, so a
            // UTF-16 or UTF-32 mark never changes the decoding.
            private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

            public FileEntry(FileSystem owner, DirectoryEntry parent, string name, string documentId)
                : base(owner, parent, name, documentId)
            {
            }

            protected override bool IsDirectory => false;

            // Providers needn't report a size, and then the only way to find it is to read the file.
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

            public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
            {
                return RunAsync(() =>
                {
                    EnsureExists();
                    return OpenStream();
                }, cancellationToken);
            }

            public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync<Stream>(cancellationToken);
            }

            public Task DeleteAsync(CancellationToken cancellationToken = default)
            {
                return RejectWriteAsync(cancellationToken);
            }

            protected override IOException NotFound(Exception? innerException) =>
                new FileNotFoundException($"The file '{Path}' does not exist.", innerException);

            private Stream OpenStream()
            {
                return FromSource(() => Owner._source.OpenRead(DocumentId));
            }
        }
    }
}
