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

        /// <summary>
        /// Wraps a provider's stream.
        /// </summary>
        /// <param name="inner">The provider's stream. Disposing this stream disposes it.</param>
        public ProviderStream(Stream inner)
        {
            _inner = inner;
        }

        /// <inheritdoc/>
        public override bool CanRead => _inner.CanRead;

        /// <inheritdoc/>
        public override bool CanSeek => _inner.CanSeek;

        /// <inheritdoc/>
        public override bool CanWrite => false;

        /// <inheritdoc/>
        public override long Length => Translate(() => _inner.Length);

        /// <inheritdoc/>
        public override long Position
        {
            get => Translate(() => _inner.Position);
            set => Translate(() => _inner.Position = value);
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count) =>
            Translate(() => _inner.Read(buffer, offset, count));

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override int ReadByte() => Translate(() => _inner.ReadByte());

        /// <inheritdoc/>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin) => Translate(() => _inner.Seek(offset, origin));

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
                    Translate(() => _inner.Dispose());
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// Runs an operation on the provider's stream, reporting Android and Java exceptions as
        /// .NET exceptions.
        /// </summary>
        /// <typeparam name="T">The operation's result type.</typeparam>
        /// <param name="operation">The operation.</param>
        /// <returns>The operation's result.</returns>
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

        /// <summary>
        /// Runs an operation that has no result on the provider's stream, reporting Android and
        /// Java exceptions as .NET exceptions.
        /// </summary>
        /// <param name="operation">The operation.</param>
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
