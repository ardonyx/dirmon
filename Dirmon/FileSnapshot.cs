using System.Linq;

namespace Dirmon
{
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
}