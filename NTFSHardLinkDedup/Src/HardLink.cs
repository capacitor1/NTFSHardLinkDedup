using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace NTFSHardLinkDedup.Src
{
    public readonly struct HardLinkBuilderSnapshot(
        int entryProcessedCount,
        int linkCreatedCount,
        int linkSkippedCount,
        int linkFailedCount,
        bool isRunning,
        bool isCompleted)
    {
        public readonly int EntryProcessedCount = entryProcessedCount;
        public readonly int LinkCreatedCount = linkCreatedCount;
        public readonly int LinkSkippedCount = linkSkippedCount;
        public readonly int LinkFailedCount = linkFailedCount;
        public readonly bool IsRunning = isRunning;
        public readonly bool IsCompleted = isCompleted;
    }

    public sealed class NtfsHardLinkBuilder(string basepath, HashStorageBuilder storage,HashSet<string> skip,Func<bool>? isCancel = null)
    {
        private const int MaxLinksPerFile = 1024;

        private readonly HashStorageBuilder _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        private readonly string _basepath = basepath;
        private readonly Func<bool>? _isCancel = isCancel;
        private readonly Lock _errorLock = new Lock();
        private readonly List<string> _errors = new List<string>();
        private readonly HashSet<string> _skip = skip;

        private int _entryProcessedCount;
        private int _linkCreatedCount;
        private int _linkSkippedCount;
        private int _linkFailedCount;

        private volatile bool _isRunning;
        private volatile bool _isCompleted;

        [DllImport("Kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(
                string lpFileName,
                string lpExistingFileName,
                IntPtr lpSecurityAttributes);

        public HardLinkBuilderSnapshot GetSnapshot()
        {
            return new HardLinkBuilderSnapshot(
                entryProcessedCount: Volatile.Read(ref _entryProcessedCount),
                linkCreatedCount: Volatile.Read(ref _linkCreatedCount),
                linkSkippedCount: Volatile.Read(ref _linkSkippedCount),
                linkFailedCount: Volatile.Read(ref _linkFailedCount),
                isRunning: _isRunning,
                isCompleted: _isCompleted);
        }
        public int EntryProcessedCount => Volatile.Read(ref _entryProcessedCount);
        public int LinkCreatedCount => Volatile.Read(ref _linkCreatedCount);
        public int LinkSkippedCount => Volatile.Read(ref _linkSkippedCount);
        public int LinkFailedCount => Volatile.Read(ref _linkFailedCount);

        public bool IsRunning => _isRunning;
        public bool IsCompleted => _isCompleted;
        private void ThrowIfCanceled(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_isCancel is not null && _isCancel())
                throw new TaskCanceledException("Build canceled by external flag.");
        }
        public Task BuildAsync(CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                throw new InvalidOperationException("Build is already running.");

            _isRunning = true;
            _isCompleted = false;

            return Task.Run(() =>
            {
                try
                {
                    BuildCore(cancellationToken);
                }
                finally
                {
                    _isRunning = false;
                    _isCompleted = true;
                }
            }, cancellationToken);
        }

        public List<string> DrainErrors()
        {
            lock (_errorLock)
            {
                if (_errors.Count == 0)
                    return [];

                var result = new List<string>(_errors);
                _errors.Clear();
                return result;
            }
        }

        private void BuildCore(CancellationToken cancellationToken)
        {
            foreach (var kv in _storage.EnumerateEntries())
            {
                ThrowIfCanceled(cancellationToken);

                Hash256 hash = kv.Key;
                IReadOnlyList<string> paths = kv.Value.Paths;

                if (hash == Hash256.DirectoryMarker || kv.Value.Size == 0)//空文件跳过
                    continue;

                if (paths.Count <= 1)
                    continue;

                Interlocked.Increment(ref _entryProcessedCount);
                ProcessOneHashGroup(paths, cancellationToken);
            }
        }

        private void ProcessOneHashGroup(IReadOnlyList<string> paths, CancellationToken cancellationToken)
        {
            for (int groupStart = 0; groupStart < paths.Count; groupStart += MaxLinksPerFile)
            {
                ThrowIfCanceled(cancellationToken);

                int groupEnd = Math.Min(groupStart + MaxLinksPerFile, paths.Count);
                string sourcePath = @"\\?\" + Path.Combine(_basepath, paths[groupStart]);

                for (int i = groupStart + 1; i < groupEnd; i++)
                {
                    ThrowIfCanceled(cancellationToken);

                    string targetPath = @"\\?\" + Path.Combine(_basepath, paths[i]);

                    if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Increment(ref _linkSkippedCount);
                        continue;
                    }
                    if(_skip.Contains(paths[i]))
                    {
                        Interlocked.Increment(ref _linkSkippedCount);
                        continue;
                    }

                    try
                    {
                        CreateOrReplaceHardLink(sourcePath, targetPath);
                        Interlocked.Increment(ref _linkCreatedCount);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref _linkFailedCount);
                        AddError($"HardLink Failed: {ex.Message}\r\nSrc = {sourcePath}\r\nDst = {targetPath}");
                    }
                }
            }
        }

        private static void CreateOrReplaceHardLink(string sourcePath, string targetPath)
        {
            // 目标显式删除后再建
            if (File.Exists(targetPath))
            {
                Util.EnsureNormalForReplace(targetPath);
                File.Delete(targetPath);
            }
            if (!CreateHardLink(targetPath, sourcePath, IntPtr.Zero))
            {
                int err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err, $"Win32Error: {err}");
            }
        }

        private void AddError(string message)
        {
            lock (_errorLock)
            {
                _errors.Add(message);
            }
        }
    }
}
