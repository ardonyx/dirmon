using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Dirmon
{
    /// <summary>
    /// Directory monitoring service
    /// </summary>
    internal class DirMon : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly FileSystemWatcher _watcher;
        private readonly CancellationToken _cancellationToken;

        private readonly ConcurrentDictionary<string, int> _sequenceCache;
        private readonly ConcurrentQueue<FileSnapshot> _memoryDb;
        private readonly Thread _memoryCommit;
        private readonly SemaphoreSlim _memoryReady;

        /// <summary>
        /// Start monitoring monitor for changes and/or activity
        /// </summary>
        /// <param name="monitorDir">Directory to watch</param>
        /// <param name="shadowDir">Copy file changes to this directory</param>
        /// <param name="cancellationToken">Cancels the task when signaled</param>
        /// <param name="pattern">File match pattern</param>
        public DirMon(string monitorDir, string shadowDir, CancellationToken cancellationToken, string pattern = "*.*")
        {
            MonitorDir = monitorDir;
            ShadowDir = shadowDir;
            _cancellationToken = cancellationToken;

            _sequenceCache = new ConcurrentDictionary<string, int>();
            _memoryDb = new ConcurrentQueue<FileSnapshot>();
            _memoryCommit = new Thread(CommitMemoryDb);
            _memoryReady = new SemaphoreSlim(0);

            // Create a new FileSystemWatcher and set its properties.
            _watcher = new FileSystemWatcher
            {
                Path = MonitorDir,
                // Watch for changes in LastAccess and LastWrite times, and
                // the renaming of files or directories.
                NotifyFilter = NotifyFilters.LastAccess
                               | NotifyFilters.LastWrite
                               | NotifyFilters.FileName
                               | NotifyFilters.DirectoryName,
                Filter = pattern
            };

            // Add event handlers.
            _watcher.Changed += OnChangeFast;
            _watcher.Created += OnCreated;
            _watcher.Deleted += OnDeleted;
            _watcher.Renamed += OnRenamed;
        }

        /// <summary>
        /// Get the path to the directory being monitored
        /// </summary>
        public string MonitorDir { get; }

        /// <summary>
        /// Get the name of the directory that shadows the directory being monitored
        /// </summary>
        public string ShadowDir { get; }

        /// <summary>
        /// If true, any data in the existing ShadowDir will be deleted
        /// </summary>
        public bool PurgeShadowDir { get; set; }

        /// <summary>
        /// When set, binary files will not be filtered from console output
        /// </summary>
        public bool DisplayBinary { get; set; }

        /// <summary>
        /// Start monitoring immediately
        /// </summary>
        public Task Start()
        {
            Logger.Debug(
                $"Starting monitor Watch={MonitorDir}, Shadow={ShadowDir}, Purge={PurgeShadowDir}, PrintBin={DisplayBinary}");

            // Setup shadow directory
            if (!string.IsNullOrEmpty(ShadowDir))
            {
                if (PurgeShadowDir && Directory.Exists(ShadowDir))
                {
                    Directory.Delete(ShadowDir, true);

                    Logger.Debug("Purged shadow directory");
                }

                if (!Directory.Exists(ShadowDir))
                {
                    Directory.CreateDirectory(ShadowDir);

                    Logger.Debug("Created shadow directory");
                }
            }

            // Start memory monitor thread
            _memoryCommit.Start();

            // Start directory monitor task
            return Task.Run(() =>
            {
                // Begin watching.
                _watcher.EnableRaisingEvents = true;
                _cancellationToken.WaitHandle.WaitOne();
            }, _cancellationToken);
        }

        private void OnChangeFast(object source, FileSystemEventArgs e)
        {
            // We only care about change to non-directories
            if (e.ChangeType != WatcherChangeTypes.Changed || IsDirectory(e.FullPath))
            {
                return;
            }

            try
            {
                // Attempt to capture contents without locking the file
                var fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using (var sr = new StreamReader(fs))
                {
                    var text = sr.ReadToEnd();

                    // We have the contents, determine what change number this is
                    if (!_sequenceCache.TryGetValue(e.FullPath, out var seq))
                    {
                        seq = 0;
                    }

                    _sequenceCache.TryAdd(e.FullPath, seq + 1);

                    // Commit this snapshot
                    _memoryDb.Enqueue(new FileSnapshot(seq, Path.GetFileName(e.FullPath), text));
                }

                // Signal data ready
                _memoryReady.Release();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "OnChangeFast Error ({0} {1})", e.FullPath, e.ChangeType);
            }
        }

        private static void OnCreated(object source, FileSystemEventArgs e)
        {
            Logger.Info("File: {0} created", e.FullPath);
        }

        private static void OnDeleted(object source, FileSystemEventArgs e)
        {
            Logger.Info("File: {0} deleted", e.FullPath);
        }

        private static void OnRenamed(object source, RenamedEventArgs e)
        {
            Logger.Info("File: {0} renamed to {1}", e.OldFullPath, e.FullPath);
        }

        /// <summary>
        /// Returns true if path is a directory
        /// </summary>
        /// <param name="path">Path to test</param>
        private static bool IsDirectory(string path)
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.Directory);
        }

        /// <summary>
        /// Wait for data in memoryDb to persist
        /// </summary>
        private void CommitMemoryDb()
        {
            while (true)
            {
                try
                {
                    // Wait for data
                    _memoryReady.Wait(_cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (_cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!_memoryDb.TryDequeue(out var snapshot))
                {
                    continue;
                }

                var outPath = Path.Combine(ShadowDir, $"{snapshot.Sequence}_{snapshot.FileName}");

                if (!DisplayBinary || DisplayBinary && !snapshot.HasBinaryContent())
                {
                    Logger.Warn("Snapshot {0}: {1}", snapshot.FileName, snapshot.Contents);
                }
                else
                {
                    Logger.Info("Skipping display of binary file: {0}", snapshot.FileName);
                }

                // Try to write this snapshot to the shadow directory
                try
                {
                    File.WriteAllText(outPath, snapshot.Contents);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "CommitMemoryDb Error");
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Stop the producer
            if (!(_watcher is null))
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
            }

            // Wait for consumer to stop
            _memoryCommit.Join();
        }
    }
}