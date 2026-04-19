using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;


namespace NTFSHardLinkDedup.Src
{
    public sealed class IoCounter : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);

        private readonly Process _process;
        private System.Threading.Timer? _timer;
        private readonly object _syncRoot = new();

        private IO_COUNTERS _lastCounters;
        private long _lastTimestamp;
        private bool _disposed;

        /// <summary>
        /// 当前读速度，单位：字节/秒
        /// </summary>
        public long ReadSpeed { get; private set; }

        /// <summary>
        /// 当前写速度，单位：字节/秒
        /// </summary>
        public long WriteSpeed { get; private set; }

        public IoCounter()
        {
            _process = Process.GetCurrentProcess();

            if (!GetProcessIoCounters(_process.Handle, out _lastCounters))
            {
                throw new InvalidOperationException("GetProcessIoCounters 初始化失败。");
            }
        }
        public void Start()
        {
            _lastTimestamp = Stopwatch.GetTimestamp();

            _timer = new System.Threading.Timer(
                callback: TimerCallback,
                state: null,
                dueTime: 200,
                period: 200);
        }

        private void TimerCallback(object? state)
        {
            if (_disposed)
                return;

            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                if (!GetProcessIoCounters(_process.Handle, out var currentCounters))
                    return;

                long currentTimestamp = Stopwatch.GetTimestamp();
                long elapsedTicks = currentTimestamp - _lastTimestamp;
                if (elapsedTicks <= 0)
                    return;

                double elapsedSeconds = (double)elapsedTicks / Stopwatch.Frequency;
                if (elapsedSeconds <= 0)
                    return;

                ulong readBytes = currentCounters.ReadTransferCount - _lastCounters.ReadTransferCount;
                ulong writeBytes = currentCounters.WriteTransferCount - _lastCounters.WriteTransferCount;

                ReadSpeed = (long)(readBytes / elapsedSeconds);
                WriteSpeed = (long)(writeBytes / elapsedSeconds);

                _lastCounters = currentCounters;
                _lastTimestamp = currentTimestamp;
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _timer?.Dispose();
                _process.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}
