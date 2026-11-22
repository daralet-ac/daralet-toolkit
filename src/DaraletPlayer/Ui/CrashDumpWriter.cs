using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DaraletPlayer.Feed;
using Microsoft.Win32.SafeHandles;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A test run's crash dumps, while `Test Runs\dumps.txt` is there (November 21, 2026): Windows Error Reporting is off
    /// on the test machine, so no teleport crash had left a dump since November 20, and full page heap (the hunt for
    /// what damages the game's memory, `tools\pageheap-on.ps1`) is only worth anything with the dump of where it stopped.
    /// The game's last-chance exception filter (SetUnhandledExceptionFilter, called by Windows with the crash's
    /// EXCEPTION_POINTERS) is pointed here first: it writes a minidump of the game with the crash's context into
    /// `Test Runs\dumps` (dbghelp's MiniDumpWriteDump: thread stacks, data segments, memory the stacks point at), then
    /// hands the crash to the filter that was there before (the CLR's). Written from inside the dying process, which
    /// usually works but isn't promised. Test only. `[crash-dump]` lines in log.txt.
    /// </summary>
    internal static class CrashDumpWriter {
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ExceptionFilter(IntPtr exceptionPointers);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct ExceptionInformation {
            public uint ThreadId;
            public IntPtr ExceptionPointers;
            public int ClientPointers;
        }

        // MiniDumpWithDataSegs | WithHandleData | WithUnloadedModules | WithIndirectlyReferencedMemory |
        // WithProcessThreadData | WithThreadInfo
        private const uint DumpType = 0x1 | 0x4 | 0x20 | 0x40 | 0x100 | 0x1000;

        // kept for as long as it's the filter: Windows calls it by address
        private static ExceptionFilter filter;
        private static IntPtr previous;
        private static IntPtr ours;
        private static bool written;

        [DllImport("kernel32.dll")]
        private static extern IntPtr SetUnhandledExceptionFilter(IntPtr filter);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(IntPtr process, uint processId, SafeFileHandle file, uint type,
            ref ExceptionInformation exception, IntPtr userStream, IntPtr callback);

        [DllImport("kernel32.dll")]
        private static extern bool IsDebuggerPresent();

        private static string Folder => Path.Combine(DataFolder.For("Test Runs"), "dumps");

        /// <summary>
        /// While `Test Runs\hold-for-debugger.txt` is there, the plugin's start waits on the game's thread (so the game
        /// waits too) until a debugger has attached, 60 seconds at most (November 21, 2026). Under page heap backwards
        /// the game stopped in ImGui's font picture 2 to 5 seconds after the plugin started, too soon for ProcDump to be
        /// attached from the plugin's first line, and attached any earlier it keeps Decal out of the game. Test only.
        /// </summary>
        public static void HoldForDebugger() {
            try {
                if (!File.Exists(Path.Combine(DataFolder.For("Test Runs"), "hold-for-debugger.txt")) || IsDebuggerPresent()) {
                    return;
                }

                PluginCore.LogFile("[crash-dump] holding for a debugger (Test Runs\\hold-for-debugger.txt)");
                var watch = Stopwatch.StartNew();

                while (!IsDebuggerPresent() && watch.Elapsed < TimeSpan.FromSeconds(60)) {
                    System.Threading.Thread.Sleep(100);
                }

                PluginCore.LogFile(IsDebuggerPresent()
                    ? $"[crash-dump] a debugger attached after {watch.Elapsed.TotalSeconds:0.0} s"
                    : "[crash-dump] no debugger in 60 s: going on");
            }
            catch (Exception ex) {
                PluginCore.LogFile("[crash-dump] " + ex.Message);
            }
        }

        /// <summary>The filter put in, when Test Runs\dumps.txt asks for it.</summary>
        public static void Start() {
            try {
                if (filter != null || !File.Exists(Path.Combine(DataFolder.For("Test Runs"), "dumps.txt"))) {
                    return;
                }

                filter = OnCrash;
                ours = Marshal.GetFunctionPointerForDelegate(filter);
                previous = SetUnhandledExceptionFilter(ours);
                AppDomain.CurrentDomain.UnhandledException += Domain_UnhandledException;
                PluginCore.LogFile($"[crash-dump] writing a dump of any crash into {Folder}");
            }
            catch (Exception ex) {
                PluginCore.LogFile("[crash-dump] " + ex.Message);
            }
        }

        /// <summary>The filter that was there before put back (a hot reload's old copy must leave nothing of its own in).</summary>
        public static void Stop() {
            if (filter == null) {
                return;
            }

            AppDomain.CurrentDomain.UnhandledException -= Domain_UnhandledException;

            // only if nothing has put a filter in since, which would be left pointing at a filter of ours if ours went
            var current = SetUnhandledExceptionFilter(previous);

            if (current != ours) {
                SetUnhandledExceptionFilter(current);
            }

            filter = null;
        }

        /// <summary>
        /// A crash under a call from .NET (Decal's events, the plugin's hooks): the CLR ends the game itself and never
        /// reaches the last-chance filter, so the dump is written from here, the crash's pointers being the ones .NET is
        /// handling (Marshal.GetExceptionPointers). The first full page heap runs died that way in Decal's Inject.dll and
        /// left no dump.
        /// </summary>
        private static void Domain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
            try {
                PluginCore.LogFile("[crash-dump] unhandled: " + (e.ExceptionObject as Exception)?.Message);
                Write(Marshal.GetExceptionPointers());
            }
            catch {
            }
        }

        /// <summary>
        /// While `Test Runs\memory-watch.txt` is there, the game's free address space is logged once a second for two
        /// minutes from the plugin's start, from a thread of its own, so it goes on while the game's thread is stuck
        /// (November 21, 2026): under full page heap the game stopped in ImGui's font picture, or sat silent behind
        /// what looked like an ImGui error box, and this says whether page heap had used up the 32-bit address space, so
        /// that ImGui's allocation came back empty. Test only.
        /// </summary>
        public static void WatchMemory() {
            try {
                if (!File.Exists(Path.Combine(DataFolder.For("Test Runs"), "memory-watch.txt"))) {
                    return;
                }

                var thread = new System.Threading.Thread(() => {
                    try {
                        var watch = Stopwatch.StartNew();

                        while (watch.Elapsed < TimeSpan.FromSeconds(120)) {
                            var (free, largest, pieces) = FrameRateWindow.FreePieces();
                            var own = Process.GetCurrentProcess().PrivateMemorySize64;
                            PluginCore.LogFile($"[crash-dump] memory at {watch.Elapsed.TotalSeconds:0} s: "
                                + $"{free >> 20} MB free, largest piece {largest >> 20} MB, {pieces} pieces of 1 MB, "
                                + $"private {own >> 20} MB");
                            System.Threading.Thread.Sleep(1000);
                        }
                    }
                    catch (Exception ex) {
                        PluginCore.LogFile("[crash-dump] memory watch: " + ex.Message);
                    }
                }) { IsBackground = true, Name = "Daralet memory watch" };
                thread.Start();
            }
            catch (Exception ex) {
                PluginCore.LogFile("[crash-dump] " + ex.Message);
            }
        }

        private static int OnCrash(IntPtr exceptionPointers) {
            Write(exceptionPointers);

            if (previous != IntPtr.Zero) {
                var before = Marshal.GetDelegateForFunctionPointer<ExceptionFilter>(previous);
                return before(exceptionPointers);
            }

            // EXCEPTION_CONTINUE_SEARCH: Windows ends the game as it would have
            return 0;
        }

        private static void Write(IntPtr exceptionPointers) {
            if (!written) {
                written = true;

                try {
                    Directory.CreateDirectory(Folder);
                    var id = Process.GetCurrentProcess().Id;
                    var path = Path.Combine(Folder, $"acclient {DateTime.Now:yyyy-MM-dd HH-mm-ss} {id}.dmp");

                    using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) {
                        var info = new ExceptionInformation {
                            ThreadId = GetCurrentThreadId(),
                            ExceptionPointers = exceptionPointers,
                            ClientPointers = 0,
                        };
                        var ok = MiniDumpWriteDump(GetCurrentProcess(), (uint)id, file.SafeFileHandle, DumpType, ref info,
                            IntPtr.Zero, IntPtr.Zero);
                        PluginCore.LogFile(ok ? $"[crash-dump] wrote {path}"
                            : $"[crash-dump] couldn't write {path}: error {Marshal.GetLastWin32Error()}");
                    }
                }
                catch (Exception ex) {
                    PluginCore.LogFile("[crash-dump] " + ex.Message);
                }
            }
        }
    }
}
