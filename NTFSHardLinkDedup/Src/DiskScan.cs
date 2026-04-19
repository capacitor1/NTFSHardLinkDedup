using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace NTFSHardLinkDedup.Src
{
    /// <summary>
    /// 高性能磁盘扫描器。
    /// <para>
    /// 设计约束：
    /// </para>
    /// <list type="bullet">
    /// <item><description>单个实例只执行一次 <see cref="Scan"/>。</description></item>
    /// <item><description>扫描过程中，外部只会频繁读取计数，不会读取结果列表。</description></item>
    /// <item><description>扫描完成后，可多次调用 <see cref="GetList"/> 获取同一份内部缓存视图。</description></item>
    /// <item><description>返回的所有路径均为相对于构造时 rootpath 的相对路径。</description></item>
    /// </list>
    /// </summary>
    public sealed class DiskScan : IDisposable
    {
        private readonly string _rootPath;
        private readonly int _rootPrefixLength;
        private readonly PooledEntryStore _store;

        private CancellationTokenSource? _cts;
        private TaskCompletionSource _completionSource = NewCompletionSource();
        private Task? _scanTask;

        private long _fileCount;
        private long _directoryCount;

        // 0 = idle, 1 = scanning, 2 = completed, 3 = paused, 4 = closed
        private volatile int _state;

        /// <summary>
        /// 使用指定根路径创建扫描器。
        /// <para>返回的所有路径均为相对于该根路径的相对路径。</para>
        /// </summary>
        /// <param name="rootpath">扫描根路径。</param>
        public DiskScan(string rootpath)
        {
            string full = Path.GetFullPath(rootpath);
            if (!Path.EndsInDirectorySeparator(full))
                full += Path.DirectorySeparatorChar;

            _rootPath = full;
            _rootPrefixLength = full.Length;

            _store = new PooledEntryStore(initialEntryCapacity: 4096, initialCharCapacity: 256 * 1024);
            _state = 0;
        }

        /// <summary>
        /// 当前是否处于扫描中。
        /// </summary>
        public bool IsScanning
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _state == 1;
        }

        /// <summary>
        /// 当前是否已扫描完成。
        /// <para>
        /// 仅当扫描自然结束时为 true。
        /// </para>
        /// <para>
        /// 若调用了 <see cref="Pause"/> 或 <see cref="Close"/>，该值不会变为 true。
        /// </para>
        /// </summary>
        public bool IsCompleted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _state == 2;
        }

        /// <summary>
        /// 当前是否已暂停。
        /// </summary>
        public bool IsPaused
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _state == 3;
        }

        /// <summary>
        /// 表示本次扫描完成的任务。
        /// <para>
        /// 可用于等待扫描结束。
        /// </para>
        /// <para>
        /// 扫描自然完成时，该任务成功完成。
        /// 若扫描被暂停或关闭，该任务会被取消。
        /// </para>
        /// </summary>
        public Task Completion
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _completionSource.Task;
        }

        /// <summary>
        /// 返回当前已扫描到的文件数量。
        /// <para>该方法适合在扫描过程中高频调用，用于 UI 刷新。</para>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long FileCount() => Volatile.Read(ref _fileCount);

        /// <summary>
        /// 返回当前已扫描到的文件夹数量。
        /// <para>该方法适合在扫描过程中高频调用，用于 UI 刷新。</para>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long DirectoryCount() => Volatile.Read(ref _directoryCount);

        /// <summary>
        /// 返回当前已扫描到的总项目数量。
        /// <para>总数 = 文件数 + 文件夹数。</para>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long Count() => Volatile.Read(ref _fileCount) + Volatile.Read(ref _directoryCount);

        /// <summary>
        /// 从头启动扫描。
        /// <para>该方法立即返回，扫描在线程池后台执行。</para>
        /// <para>按设计约束，一个实例通常只调用一次该方法。</para>
        /// </summary>
        public void Scan()
        {
            _cts = new CancellationTokenSource();
            _completionSource = NewCompletionSource();
            _state = 1;

            _scanTask = Task.Run(() => ScanCore(_cts.Token), _cts.Token);
        }

        /// <summary>
        /// 暂停当前扫描。
        /// <para>本库不支持继续扫描。暂停后若要重新扫描，应重新创建实例。</para>
        /// </summary>
        public async Task Pause()
        {
            var cts = _cts;
            var task = _scanTask;

            if (cts == null || task == null || task.IsCompleted)
            {
                _state = 3;
                _completionSource.TrySetCanceled();
                return;
            }

            cts.Cancel();

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _state = 3;
                _completionSource.TrySetCanceled();
            }
        }

        /// <summary>
        /// 获取内部结果列表视图。
        /// <para>不复制内部数据，可多次调用。</para>
        /// <para>按设计约束，应仅在扫描完成后调用。</para>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DiskScanList GetList() => new(_store);

        /// <summary>
        /// 释放当前实例持有的缓存与资源。
        /// <para>调用后，该实例不应继续使用。</para>
        /// </summary>
        public void Close()
        {
            _cts?.Cancel();

            try
            {
                _scanTask?.GetAwaiter().GetResult();
            }
            catch
            {
            }

            _completionSource.TrySetCanceled();

            _cts?.Dispose();
            _cts = null;
            _scanTask = null;

            _store.Dispose();
            _state = 4;
        }

        /// <inheritdoc/>
        public void Dispose() => Close();

        private void ScanCore(CancellationToken token)
        {
            try
            {
                var dirs = new Stack<string>(256);
                dirs.Push(_rootPath);

                while (dirs.Count != 0)
                {
                    token.ThrowIfCancellationRequested();

                    string current = dirs.Pop();

                    IEnumerable<string> subDirs;
                    IEnumerable<string> files;

                    try
                    {
                        subDirs = Directory.EnumerateDirectories(current);
                        files = Directory.EnumerateFiles(current);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        continue;
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    foreach (string dir in subDirs)
                    {
                        token.ThrowIfCancellationRequested();

                        ReadOnlySpan<char> relative = dir.AsSpan(_rootPrefixLength);
                        _store.Add(relative, isDirectory: true);
                        Interlocked.Increment(ref _directoryCount);

                        dirs.Push(dir);
                    }

                    foreach (string file in files)
                    {
                        token.ThrowIfCancellationRequested();

                        ReadOnlySpan<char> relative = file.AsSpan(_rootPrefixLength);
                        _store.Add(relative, isDirectory: false);
                        Interlocked.Increment(ref _fileCount);
                    }
                }

                _state = 2;
                _completionSource.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                _completionSource.TrySetCanceled();
                throw;
            }
            catch (Exception ex)
            {
                _completionSource.TrySetException(ex);
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TaskCompletionSource NewCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// 扫描结果列表的只读视图。
    /// <para>该视图不复制内部数据，可被多次获取与多次遍历。</para>
    /// </summary>
    public readonly struct DiskScanList
    {
        private readonly PooledEntryStore _store;

        internal DiskScanList(PooledEntryStore store)
        {
            _store = store;
        }

        public long Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _store.Count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new(_store);

        public struct Enumerator
        {
            private readonly PooledEntryStore _store;
            private readonly int _count;
            private int _index;

            internal Enumerator(PooledEntryStore store)
            {
                _store = store;
                _count = (int)store.Count;
                _index = -1;
            }

            public DiskScanCursor Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => new(_store, _index);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                int next = _index + 1;
                if (next < _count)
                {
                    _index = next;
                    return true;
                }

                return false;
            }
        }
    }

    public readonly struct DiskScanCursor
    {
        private readonly PooledEntryStore _store;
        private readonly int _index;

        internal DiskScanCursor(PooledEntryStore store, int index)
        {
            _store = store;
            _index = index;
        }

        public ReadOnlySpan<char> Path
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _store.GetPathSpan(_index);
        }

        public ReadOnlyMemory<char> PathMemory
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _store.GetPathMemory(_index);
        }

        public bool IsDirectory
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _store.GetEntry(_index).IsDirectory;
        }

        public string PathString
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _store.GetPathSpan(_index).ToString();
        }
    }


    /// <summary>
    /// 扫描结果项。
    /// </summary>
    /// <param name="Path">相对路径。</param>
    /// <param name="IsDirectory">是否为文件夹。</param>
    public readonly record struct DiskScanItem(ReadOnlyMemory<char> Path, bool IsDirectory);

    internal struct EntryData
    {
        public int Start;
        public int Length;
        public bool IsDirectory;
    }

    internal sealed class PooledEntryStore : IDisposable
    {
        private readonly ArrayPool<char> _charPool = ArrayPool<char>.Shared;
        private readonly ArrayPool<EntryData> _entryPool = ArrayPool<EntryData>.Shared;

        private char[] _charBuffer;
        private EntryData[] _entries;

        private int _charUsed;
        private int _count;

        public PooledEntryStore(int initialEntryCapacity, int initialCharCapacity)
        {
            _entries = _entryPool.Rent(initialEntryCapacity);
            _charBuffer = _charPool.Rent(initialCharCapacity);
        }

        public long Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(ReadOnlySpan<char> path, bool isDirectory)
        {
            int neededCount = _count + 1;
            if (neededCount > _entries.Length)
                GrowEntries(neededCount);

            int neededChars = _charUsed + path.Length;
            if (neededChars > _charBuffer.Length)
                GrowChars(neededChars);

            path.CopyTo(_charBuffer.AsSpan(_charUsed));

            _entries[_count] = new EntryData
            {
                Start = _charUsed,
                Length = path.Length,
                IsDirectory = isDirectory
            };

            _charUsed += path.Length;
            _count++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public EntryData GetEntry(int index) => _entries[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<char> GetPathSpan(int index)
        {
            ref EntryData e = ref _entries[index];
            return _charBuffer.AsSpan(e.Start, e.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyMemory<char> GetPathMemory(int index)
        {
            ref EntryData e = ref _entries[index];
            return _charBuffer.AsMemory(e.Start, e.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyMemory<char> GetPathMemory(EntryData entry)
            => _charBuffer.AsMemory(entry.Start, entry.Length);

        public void Dispose()
        {
            _entryPool.Return(_entries, clearArray: false);
            _charPool.Return(_charBuffer, clearArray: false);

            _entries = Array.Empty<EntryData>();
            _charBuffer = Array.Empty<char>();
            _charUsed = 0;
            _count = 0;
        }

        private void GrowEntries(int needed)
        {
            int newSize = _entries.Length * 2;
            if (newSize < needed)
                newSize = needed;

            EntryData[] newArr = _entryPool.Rent(newSize);
            Array.Copy(_entries, 0, newArr, 0, _count);
            _entryPool.Return(_entries, clearArray: false);
            _entries = newArr;
        }

        private void GrowChars(int needed)
        {
            int newSize = _charBuffer.Length * 2;
            if (newSize < needed)
                newSize = needed;

            char[] newArr = _charPool.Rent(newSize);
            _charBuffer.AsSpan(0, _charUsed).CopyTo(newArr);
            _charPool.Return(_charBuffer, clearArray: false);
            _charBuffer = newArr;
        }
    }
}
