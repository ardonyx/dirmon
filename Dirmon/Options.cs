using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Dirmon
{
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
            .GetCustomAttribute<AssemblyFileVersionAttribute>()
            ?.Version;

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
}