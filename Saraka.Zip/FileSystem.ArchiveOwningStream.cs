using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace Saraka.Zip
{
    public sealed partial class FileSystem
    {
        /// <summary>
        /// A read-only stream over a zip entry that owns the archive it came from, and closes it
        /// when the stream is disposed.
        /// </summary>
        private sealed class ArchiveOwningStream : Stream
        {
            private readonly Stream _inner;
            private readonly ZipArchive _archive;

            /// <summary>
            /// Wraps an entry's stream.
            /// </summary>
            /// <param name="inner">The entry's stream.</param>
            /// <param name="archive">The archive the entry belongs to, which this stream now owns.</param>
            public ArchiveOwningStream(Stream inner, ZipArchive archive)
            {
                _inner = inner;
                _archive = archive;
            }

            /// <inheritdoc/>
            public override bool CanRead => _inner.CanRead;

            /// <inheritdoc/>
            public override bool CanSeek => _inner.CanSeek;

            /// <inheritdoc/>
            public override bool CanWrite => false;

            /// <inheritdoc/>
            public override long Length => _inner.Length;

            /// <inheritdoc/>
            public override long Position
            {
                get => _inner.Position;
                set => _inner.Position = value;
            }

            /// <inheritdoc/>
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

            /// <inheritdoc/>
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                _inner.ReadAsync(buffer, offset, count, cancellationToken);

            /// <inheritdoc/>
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

            /// <inheritdoc/>
            public override void Flush()
            {
            }

            /// <inheritdoc/>
            public override void SetLength(long value) => throw new NotSupportedException("The stream is read-only.");

            /// <inheritdoc/>
            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException("The stream is read-only.");

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                try
                {
                    if (disposing)
                    {
                        _inner.Dispose();
                        _archive.Dispose();
                    }
                }
                finally
                {
                    base.Dispose(disposing);
                }
            }
        }
    }
}
