using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

// We are using reflection to set properties on Options class
// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace Dirmon
{
    internal class Options
    {
        /// <summary>
        /// Path to directory to monitor
        /// </summary>
        [ArgumentAttribute("monitor", ShortName = 'm', Description = "Path to directory to monitor",
            Example = "--monitor /path/to/file", Required = true)]
        public string MonitorDir { get; private set; }

        /// <summary>
        /// Path to shadow backup directory
        /// </summary>
        [ArgumentAttribute("shadow", ShortName = 's', Description = "Path to directory to receive file change copies",
            Example = "--shadow /path/to/shadow",
            Required = true)]
        public string ShadowDir { get; private set; }

        /// <summary>
        /// If true, purge existing shadow backup
        /// </summary>
        [ArgumentAttribute("purge-shadow", ShortName = 'p', Description = "Purge existing shadow directory",
            IsFlag = true)]
        public bool PurgeShadowDir { get; private set; }

        /// <summary>
        /// Do not try to filter binary files from console display
        /// </summary>
        [ArgumentAttribute("display-binary", ShortName = 'b',
            Description = "Try not to print binary contents to console", IsFlag = true)]
        public bool DisplayBinary { get; private set; }

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
            var opts = new Options();
            var attrs = GetArgumentAttributes(opts).ToList();
            var required = attrs.Where(a => a.Required).ToList();
            var seen = new List<string>();

            // Use a capture action for two-part key:value options
            Action<string> nextCapture = null;
            foreach (var key in args)
            {
                // No pending capture, read as key
                if (nextCapture is null)
                {
                    // Make sure this is not a duplicate input
                    if (seen.Contains(key))
                    {
                        throw new Exception($"Duplicate parameter specified {key}");
                    }

                    // Since the attributes are built from properties which cannot be duplicated in the
                    // same type, there can never be more than once match. Null means unknown input.
                    var match = attrs.FirstOrDefault(a => a.IsMatch(key));
                    if (match is null)
                    {
                        throw new Exception($"Unknown parameter: {key}");
                    }

                    // Boolean flag, presence of which means "true"
                    if (match.IsFlag)
                    {
                        SetProperty(opts, match, true);
                    }
                    else
                    {
                        // Captures next string as the value to this "key"
                        nextCapture = value => SetProperty(opts, match, value);
                    }

                    // Clear from required list if present
                    if (match.Required)
                    {
                        required.Remove(match);
                    }

                    // Mark this option as seen
                    seen.Add(key);
                }
                else
                {
                    // Previous input was a key, capture the value
                    nextCapture.Invoke(key);
                    nextCapture = null;
                }
            }

            if (required.Any())
            {
                // Then show the problem
                var sb = new StringBuilder();
                sb.AppendLine("Missing required parameters:");
                foreach (var r in required)
                {
                    sb.AppendLine($"\t{r.Usage}");
                }

                throw new Exception(sb.ToString());
            }

            return opts;
        }

        /// <summary>
        /// Returns usage as a string
        /// </summary>
        public static string Usage()
        {
            var attrs = GetArgumentAttributes(new Options()).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"Dirmon v{AppVersion}");

            // Usage section
            sb.AppendLine("Usage:");
            sb.Append("\tDirmon.exe ");
            foreach (var attr in attrs)
            {
                sb.Append($"{attr.Usage} ");
            }

            sb.AppendLine();

            // Required arg description section
            sb.AppendLine();
            sb.AppendLine("Required:");
            foreach (var attr in attrs.Where(a => a.Required))
            {
                sb.AppendLine($"\t{attr.Usage}\t{attr.Description}. {attr.Example}");
            }

            // Optional arg description section
            sb.AppendLine();
            sb.AppendLine("Optional:");
            foreach (var attr in attrs.Where(a => !a.Required))
            {
                sb.AppendLine($"\t{attr.Usage}\t{attr.Description}. {attr.Example}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns list of argument attributes on all properties of this instance
        /// </summary>
        /// <returns>List of attributes</returns>
        private static IEnumerable<ArgumentAttribute> GetArgumentAttributes(Options instance)
        {
            var props = instance.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);

            var result = new List<ArgumentAttribute>();
            foreach (var prop in props)
            {
                var attrs = prop.GetCustomAttributes(typeof(ArgumentAttribute), false);
                foreach (var attr in attrs)
                {
                    if (attr is ArgumentAttribute t)
                    {
                        result.Add(t);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Helper to set a property by name
        /// </summary>
        /// <param name="instance">Instance to modify</param>
        /// <param name="attr">Attribute describing target</param>
        /// <param name="value">New value to set</param>
        private static void SetProperty<T>(Options instance, ArgumentAttribute attr, T value)
        {
            var prop = instance.GetType().GetProperty(attr.PropertyName,
                BindingFlags.Public | BindingFlags.Instance);
            if (null != prop && prop.CanWrite)
            {
                prop.SetValue(instance, value, null);
            }
        }
    }
}