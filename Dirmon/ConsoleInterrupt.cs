using System.Runtime.InteropServices;

namespace Dirmon
{
    /// <summary>
    /// Set a console CTRL+C interrupt handler to avoid polling the keyboard input
    /// </summary>
    public static class ConsoleInterrupt
    {
        /// <summary>
        /// Called when the specified CtrlTypes is detected
        /// </summary>
        /// <param name="ctrlType"></param>
        public delegate bool HandlerRoutine(CtrlTypes ctrlType);

        /// <summary>
        /// Console event types
        /// </summary>
        // ReSharper disable UnusedMember.Global
        public enum CtrlTypes
        {
            /// <summary>
            /// Control+C event
            /// </summary>
            CtrlCEvent = 0,
            /// <summary>
            /// Control+BREAK event
            /// </summary>
            CtrlBreakEvent,
            /// <summary>
            /// Control+Close event
            /// </summary>
            CtrlCloseEvent,
            /// <summary>
            /// Control+Logoff event
            /// </summary>
            CtrlLogoffEvent = 5,
            /// <summary>
            /// Control+Shutdown event
            /// </summary>
            CtrlShutdownEvent
        }
        // ReSharper restore UnusedMember.Global

        /// <summary>
        /// Adds or removes an application-defined HandlerRoutine function from the list of handler functions for the
        /// calling process. If no handler function is specified, the function sets an inheritable attribute that
        /// determines whether the calling process ignores CTRL+C signals.
        /// </summary>
        /// <param name="handler">Receives control event</param>
        /// <param name="add">true to add, false to remove</param>
        /// <returns>true on success</returns>
        [DllImport("Kernel32")]
        public static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);
    }
}