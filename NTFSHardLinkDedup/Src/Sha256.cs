using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace NTFSHardLinkDedup.Src
{
    public sealed class SHA256Calc : IDisposable
    {
        private readonly string _path;
        private readonly Func<bool> _isCanceled;
        private readonly int _bufferSize;

        private FileStream _fileStream;
        private IncrementalHash? _incrementalHash;
        private byte[]? _result;
        private bool _disposed;
        private bool _calculated;
        private ulong _flen = ulong.MaxValue;

        public SHA256Calc(string root,ReadOnlySpan<char> path, Func<bool> isCanceled, int bufferSize = 1024 * 1024)
        {
            _path = Path.Combine(root,path.ToString());
            _isCanceled = isCanceled ?? throw new ArgumentNullException(nameof(isCanceled));
            _bufferSize = bufferSize > 0 ? bufferSize : throw new ArgumentOutOfRangeException(nameof(bufferSize));
            _fileStream = new FileStream(
                    path: _path,
                    mode: FileMode.Open,
                    access: FileAccess.Read,
                    share: FileShare.Read,
                    bufferSize: _bufferSize,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);
            _flen = (ulong)_fileStream.Length;
        }
        public ulong GetFileLen()
        {
            ThrowIfDisposed();
            return _flen;
        }
        public async ValueTask<ReadOnlyMemory<byte>> CalcSha256()
        {
            ThrowIfDisposed();

            if (_calculated)
                return _result!;

            byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(_bufferSize);

            try
            {
                ThrowIfCanceled();

                
                _incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

                while (true)
                {
                    ThrowIfCanceled();

                    int bytesRead = await _fileStream
                        .ReadAsync(rentedBuffer.AsMemory(0, _bufferSize))
                        .ConfigureAwait(false);

                    if (bytesRead == 0)
                        break;

                    _incrementalHash.AppendData(rentedBuffer, 0, bytesRead);

                    ThrowIfCanceled();
                }

                _result = _incrementalHash.GetHashAndReset();
                _calculated = true;

                return _result;
            }
            finally
            {
                Array.Clear(rentedBuffer, 0, rentedBuffer.Length);
                ArrayPool<byte>.Shared.Return(rentedBuffer);

                _fileStream?.Dispose();

                _incrementalHash?.Dispose();
                _incrementalHash = null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _fileStream?.Dispose();

            _incrementalHash?.Dispose();
            _incrementalHash = null;

            if (_result is not null)
            {
                Array.Clear(_result, 0, _result.Length);
                _result = null;
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void ThrowIfCanceled()
        {
            if (_isCanceled())
                throw new TaskCanceledException();
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }
    }
}
