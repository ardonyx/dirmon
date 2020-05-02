using System.Runtime.InteropServices;

namespace Dirmon
{
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