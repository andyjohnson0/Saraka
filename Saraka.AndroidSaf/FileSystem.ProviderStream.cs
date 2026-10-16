using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace Saraka.AndroidSaf
{
    public sealed partial class FileSystem
    {
        /// <summary>
        /// A read-only stream over a document provider's stream that reports the provider's Java
        /// exceptions as .NET exceptions.
        /// </summary>
        private sealed class ProviderStream : Stream
        {
            private readonly Stream _inner;
            private readonly Func<Exception, IOException> _notFound;

            public ProviderStream(Stream inner, Func<Exception, IOException> notFound)
            {
                _inner = inner;
                _notFound = notFound;
            }

            public override bool CanRead => _inner.CanRead;

            public override bool CanSeek => _inner.CanSeek;

            public override bool CanWrite => false;

            public override long Length => Translate(() => _inner.Length);

            public override long Position
            {
                get => Translate(() => _inner.Position);
                set => Translate(() => _inner.Position = value);
            }

            public override int Read(byte[] buffer, int offset, int count) =>
                Translate(() => _inner.Read(buffer, offset, count));

            public override int Read(Span<byte> buffer)
            {
                try
                {
                    return _inner.Read(buffer);
                }
                catch (Java.Lang.Throwable ex)
                {
                    throw TranslateProviderException(ex, _notFound);
                }
            }

            public override int ReadByte() => Translate(() => _inner.ReadByte());

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                try
                {
                    return await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                }
                catch (Java.Lang.Throwable ex)
                {
                    throw TranslateProviderException(ex, _notFound);
                }
            }

            public override long Seek(long offset, SeekOrigin origin) => Translate(() => _inner.Seek(offset, origin));

            public override void Flush()
            {
            }

            public override void SetLength(long value) => throw new NotSupportedException("The stream is read-only.");

            public override void Write(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException("The stream is read-only.");

            protected override void Dispose(bool disposing)
            {
                try
                {
                    if (disposing)
                        Translate(() => _inner.Dispose());
                }
                finally
                {
                    base.Dispose(disposing);
                }
            }

            private T Translate<T>(Func<T> operation)
            {
                try
                {
                    return operation();
                }
                catch (Java.Lang.Throwable ex)
                {
                    throw TranslateProviderException(ex, _notFound);
                }
            }

            private void Translate(Action operation)
            {
                Translate(() =>
                {
                    operation();
                    return true;
                });
            }
        }
    }
}
