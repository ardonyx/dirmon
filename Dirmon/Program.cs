using System;
using System.Threading;
using NLog;

namespace Dirmon
{
    /// <summary>
    /// Accept configuration via commandline to launch an instance of Dirmon
    /// Use this tool to monitor a specified directory for file add/edit/delete operations.
    /// Optionally, files can be shadowed copied to monitor the change history of files.
    /// </summary>
    internal static class Program
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
                Console.WriteLine(Options.Usage());
                Console.WriteLine(ex.Message);
                return;
            }

            var dirMon = new DirMon(opts.MonitorDir, opts.ShadowDir, TokenSource.Token)
            {
                DisplayBinary = opts.DisplayBinary,
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
}