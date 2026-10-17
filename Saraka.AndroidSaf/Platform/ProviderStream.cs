using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;


namespace Saraka.AndroidSaf
{
    /// <summary>
    /// A read-only stream over a document provider's stream that reports the provider's Android
    /// and Java exceptions as .NET exceptions.
    /// </summary>
    internal sealed class ProviderStream : Stream
    {
        private readonly Stream _inner;

        public ProviderStream(Stream inner)
        {
            _inner = inner;
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
                throw SafDocumentSource.Translate(ex);
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
                throw SafDocumentSource.Translate(ex);
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

        private static T Translate<T>(Func<T> operation)
        {
            try
            {
                return operation();
            }
            catch (Java.Lang.Throwable ex)
            {
                throw SafDocumentSource.Translate(ex);
            }
        }

        private static void Translate(Action operation)
        {
            Translate(() =>
            {
                operation();
                return true;
            });
        }
    }
}
