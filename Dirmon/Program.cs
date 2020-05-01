using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Dirmon
{
    /// <summary>
    /// Accept configuration via commandline to launch an instance of Dirmon
    /// Use this tool to monitor a specified directory for file add/edit/delete operations.
    /// Optionally, files can be shadowed copied to monitor the change history of files.
    /// </summary>
    internal class Program
    {
        private static readonly CancellationTokenSource TokenSource = new CancellationTokenSource();
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static void Main(string[] args)
        {
            // Capture ctrl+c to stop process
            ConsoleInterrupt.SetConsoleCtrlHandler(ConsoleHandler, true);

            Options opts;
            try
            {
                opts = Options.Parse(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(Options.Usage());
                return;
            }

            var dirMon = new DirMon(opts.MonitorDir, opts.ShadowDir, TokenSource.Token)
            {
                TryFilterBinaryContent = opts.NoDisplayBinary,
                PurgeShadowDir = opts.PurgeShadowDir
            };

            Logger.Info("Dirmon v{0}", Options.AppVersion);
            Logger.Info("Monitoring {0}", dirMon.MonitorDir);
            Logger.Info("Backup to {0}", dirMon.ShadowDir);

            Logger.Info("Starting.... CTRL+C to stop");

            var monTask = dirMon.Start();

            try
            {
                monTask.Wait();
            }
            catch (Exception ex) when (ex is OperationCanceledException)

            {
                Logger.Info("Monitoring cancelled, shutting down...");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Dirmon Error");
            }
        }

        /// <summary>
        ///     Console interrupt handler
        /// </summary>
        /// <param name="ctrl">Console control code</param>
        /// <returns>true if interrupt was handled, false otherwise</returns>
        private static bool ConsoleHandler(ConsoleInterrupt.CtrlTypes ctrl)
        {
            // Only handle ctrl+c
            if (ctrl != ConsoleInterrupt.CtrlTypes.CtrlCEvent)
            {
                return false;
            }

            // Stop the zForce task
            TokenSource.Cancel();

            // Detach this handler
            ConsoleInterrupt.SetConsoleCtrlHandler(ConsoleHandler, false);

            // Yes, we handled the interrupt
            return true;
        }
    }

    internal readonly struct Options
    {
        private Options(string monitorDir, string shadowDir, bool purgeShadowDir, bool noDisplayBinary)
        {
            MonitorDir = monitorDir;
            ShadowDir = shadowDir;
            PurgeShadowDir = purgeShadowDir;
            NoDisplayBinary = noDisplayBinary;
        }

        /// <summary>
        /// Path to directory to monitor
        /// </summary>
        public string MonitorDir { get; }

        /// <summary>
        /// Path to shadow backup directory
        /// </summary>
        public string ShadowDir { get; }

        /// <summary>
        /// If true, purge existing shadow backup
        /// </summary>
        public bool PurgeShadowDir { get; }

        /// <summary>
        /// Try not to display the contents of binary files on the console
        /// </summary>
        public bool NoDisplayBinary { get; }

        /// <summary>
        /// Returns the version of this assembly
        /// </summary>
        public static string AppVersion => typeof(Options).Assembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>().Version;

        /// <summary>
        /// Parse CLI options into Options instance
        /// </summary>
        /// <param name="args">Program launch arguments</param>
        public static Options Parse(IEnumerable<string> args)
        {

            string monitorDir = null;
            string shadowDir = null;
            var purgeShadowDir = false;
            var noDisplayBinary = false;

            // Use a capture action for two-part key:value options
            Action<string> nextCapture = null;
            foreach (var s in args)
            {
                // No pending capture
                if (nextCapture is null)
                {
                    switch (s)
                    {
                        case "-m":
                        case "--monitor":
                            nextCapture = t => monitorDir = t;
                            break;

                        case "-s":
                        case "--shadow":
                            nextCapture = t => shadowDir = t;
                            break;

                        case "-p":
                        case "--purge-shadow-dir":
                            purgeShadowDir = true;
                            break;

                        case "-n":
                        case "--no-display-binary":
                            noDisplayBinary = true;
                            break;
                        
                        default:
                            throw new Exception($"Unknown parameter: {s}");
                    }
                }
                else
                {
                    nextCapture.Invoke(s);
                    nextCapture = null;
                }
            }

            var opts = new Options(monitorDir, shadowDir, purgeShadowDir, noDisplayBinary);

            opts.Validate();

            return opts;
        }

        /// <summary>
        /// Returns usage as a string
        /// </summary>
        public static string Usage()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Dirmon v{AppVersion}");
            sb.AppendLine("Usage --monitor|-m --shadow|-s [--purge-shadow|-p] [--no-display-binary|-n]");
            sb.AppendLine("--monitor,-m\tPath to directory to monitor");
            sb.AppendLine("--shadow,-s\tPath to directory to receive file change copies");
            sb.AppendLine("--purge-shadow,-p\tPurge existing shadow directory");
            sb.AppendLine("--no-display-binary,-b\tTry not to print binary contents to console");
            return sb.ToString();
        }

        /// <summary>
        /// Check for invalid parameters
        /// </summary>
        /// <exception cref="Exception">Raised if invalid parameters are found</exception>
        private void Validate()
        {
            if (string.IsNullOrEmpty(MonitorDir))
            {
                throw new Exception("--monitor is required");
            }

            if (string.IsNullOrEmpty(ShadowDir))
            {
                throw new Exception("--shadow is required");
            }
        }
    }

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
        /// When set, an effort will be made to not print binary file contents to console
        /// In this case, the file will only be copied
        /// </summary>
        public bool TryFilterBinaryContent { get; set; }

        /// <summary>
        /// Start monitoring immediately
        /// </summary>
        public Task Start()
        {
            // Setup shadow directory
            if (!string.IsNullOrEmpty(ShadowDir))
            {
                if (PurgeShadowDir && Directory.Exists(ShadowDir))
                {
                    Directory.Delete(ShadowDir, true);

                    Logger.Info("Purged shadow directory");
                }

                if (!Directory.Exists(ShadowDir))
                {
                    Directory.CreateDirectory(ShadowDir);

                    Logger.Info("Created shadow directory");
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

                if (!TryFilterBinaryContent || TryFilterBinaryContent && !snapshot.HasBinaryContent())
                {
                    Logger.Warn("Snapshot: {0}", snapshot.Contents);
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

    /// <summary>
    /// Captures data about a file being monitored
    /// </summary>
    readonly struct FileSnapshot
    {
        public FileSnapshot(int sequence, string fileName, string text)
        {
            Sequence = sequence;
            FileName = fileName;
            Contents = text;
        }

        /// <summary>
        /// Version history sequence
        /// </summary>
        public int Sequence { get; }

        /// <summary>
        /// Filename portion of file
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// File contents at this time
        /// </summary>
        public string Contents { get; }

        /// <summary>
        /// Returns true if the file contains suspected binary data
        /// </summary>
        public bool HasBinaryContent()
        {
            var allowedControlCodes = new[] {'\r', '\n', '\t'};
            return Contents.Where(char.IsControl).Any(ch => !allowedControlCodes.Contains(ch));
        }
    }

    /// <summary>
    /// Set a console CTRL+C interrupt handler to avoid polling the keyboard input
    /// </summary>
    public static class ConsoleInterrupt
    {
        /// <summary>
        /// Called when the specified ctrltype is detected
        /// </summary>
        /// <param name="ctrlType"></param>
        public delegate bool HandlerRoutine(CtrlTypes ctrlType);

        /// <summary>
        /// Console event types
        /// </summary>
        public enum CtrlTypes
        {
            CtrlCEvent = 0,
            CtrlBreakEvent,
            CtrlCloseEvent,
            CtrlLogoffEvent = 5,
            CtrlShutdownEvent
        }

        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);
    }
}