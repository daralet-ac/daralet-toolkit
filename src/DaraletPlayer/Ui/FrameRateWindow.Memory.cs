using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's memory beside its frame rate (the user's call, November 2026: "the client's memory leak is my top
    /// concern"): every two seconds from login, whether the window shows or not, the 32-bit address space in use and its
    /// total (GlobalMemoryStatusEx: the game runs out when it can't find room there, whatever the computer has), the
    /// process's private bytes and working set, and the plugin's managed heap; every ten, the largest free piece (the
    /// address space walked with VirtualQuery, which costs a little). The window shows them, how fast the address space in
    /// use grows (MemoryHistory, megabytes a minute over 5 and 15 minutes and since login) and how long until 512 MB is
    /// left at that rate (the draw distance's guard steps in there), and a graph of the last 15 minutes. Each second's
    /// row in the frame rate log carries them too. Untested in the game when written.
    /// </summary>
    internal sealed partial class FrameRateWindow {
        private const double MemoryEvery = 2;
        private const double LargestEvery = 10;
        private const double MemoryGraphSeconds = 15 * 60;
        private const int MemoryGraphBuckets = 90;

        /// <summary>The free room the draw distance's memory guard keeps (DrawDistance.Memory.cs).</summary>
        private const long GuardMb = 512;

        private readonly MemoryHistory memory = new MemoryHistory();
        private Process process;
        private double nextMemorySample;
        private double nextLargest;
        private long largestFreeMb;

        /// <summary>A reading made off the game's thread, waiting to be added on it (MemoryHistory isn't shared).</summary>
        private MemorySample? readySample;
        private readonly object readyLock = new object();
        private int readingMemory;

        /// <summary>
        /// Every MemoryEvery seconds a reading, made on a thread of the pool: the address space walk (VirtualQuery over
        /// 4 GB) and the process's sizes (Process.Refresh, which on .NET Framework asks Windows about every process on
        /// the computer) ran on the game's thread, likely the frame of about 40 ms every 10 seconds in the frame rate log
        /// (November 2026, the user's "lag spikes while running around").
        /// </summary>
        private void SampleMemory(double now) {
            MemorySample? ready;

            lock (readyLock) {
                ready = readySample;
                readySample = null;
            }

            if (ready != null) {
                memory.Add(ready.Value);
            }

            if (now < nextMemorySample || Volatile.Read(ref readingMemory) != 0) {
                return;
            }

            nextMemorySample = now + MemoryEvery;
            var walk = now >= nextLargest;

            if (walk) {
                nextLargest = now + LargestEvery;
            }

            readingMemory = 1;
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    var sample = ReadMemory(now, walk);

                    lock (readyLock) {
                        readySample = sample;
                    }
                }
                catch (Exception ex) {
                    PluginCore.LogFile("[memory] " + ex.Message);
                }
                finally {
                    Volatile.Write(ref readingMemory, 0);
                }
            });
        }

        /// <summary>One reading, on a thread of the pool (one at a time); null if Windows wouldn't say.</summary>
        private MemorySample? ReadMemory(double now, bool walk) {
            if (walk) {
                largestFreeMb = FreePieces().Largest / (1024 * 1024);
            }

            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };

            if (!GlobalMemoryStatusEx(ref status)) {
                return null;
            }

            process = process ?? Process.GetCurrentProcess();
            process.Refresh();
            var total = (long)(status.TotalVirtual / (1024 * 1024));
            var free = (long)(status.AvailVirtual / (1024 * 1024));
            return new MemorySample {
                At = now, TotalMb = total, UsedMb = total - free, LargestFreeMb = largestFreeMb,
                PrivateMb = process.PrivateMemorySize64 / (1024 * 1024),
                WorkingSetMb = process.WorkingSet64 / (1024 * 1024),
                ManagedMb = GC.GetTotalMemory(false) / (1024 * 1024),
            };
        }

        private void RenderMemory(double now) {
            var latest = memory.Latest;
            Form.Section("Memory");

            if (latest == null) {
                Widgets.TextDisabled("Waiting for the first reading...");
                return;
            }

            var m = latest.Value;
            var free = m.TotalMb - m.UsedMb;
            Widgets.TextColored(FreeColor(free), $"{m.UsedMb:N0} of {m.TotalMb:N0} MB used, {free:N0} free");
            Widgets.Tip("The game's address space: a 32-bit program has 4 GB of room at most, and runs out when it can't "
                + "find a big enough piece, however much memory the computer has.");
            Widgets.TextDisabled($"Largest free piece {m.LargestFreeMb:N0} MB");
            Widgets.Tip("Room in one piece. Big loads need it; plenty free in small pieces can still run out.");
            Widgets.TextDisabled($"Private {m.PrivateMb:N0} MB, working set {m.WorkingSetMb:N0} MB, plugin {m.ManagedMb:N0} MB");
            Widgets.Tip("What Windows counts for the game (private: only it uses; working set: in RAM now), and the "
                + "plugin's own share of it.");

            var five = memory.GrowthPerMinute(5 * 60);
            var fifteen = memory.GrowthPerMinute(15 * 60);
            var all = memory.GrowthPerMinute(0);
            Widgets.Text("Growing:");
            ImGui.SameLine();
            Widgets.TextColored(GrowthColor(five), Rate(five) + " (5 min)");
            ImGui.SameLine();
            Widgets.TextColored(GrowthColor(fifteen), Rate(fifteen) + " (15 min)");
            ImGui.SameLine();
            Widgets.TextColored(GrowthColor(all), Rate(all) + $" ({memory.Span / 60:0} min)");
            Widgets.Tip("How fast the address space in use grows, in MB a minute: a line through the readings, so one "
                + "load doesn't decide it. Steady growth while nothing changes is a leak.");

            // only from five minutes of readings on, at the 15-minute rate: a minute that held a landscape loading read as
            // +105 MB/min and warned of five minutes left (seen in the game)
            var steady = memory.Span >= 5 * 60 ? fifteen : null;
            var minutes = steady.HasValue ? MemoryHistory.MinutesUntil(m, steady.Value, GuardMb) : null;

            if (minutes.HasValue) {
                Widgets.TextColored(minutes.Value < 30 ? Colors.Danger : Colors.Warning,
                    $"At this rate, about {FormatMinutes(minutes.Value)} until {GuardMb} MB is left.");
                Widgets.Tip("When only this much is free, the draw distance goes back to the game's own. Restarting the "
                    + "game gives it all back.");
            }

            DrawMemoryGraph(now);
        }

        /// <summary>The address space in use over the last 15 minutes, with the marks.</summary>
        private void DrawMemoryGraph(double now) {
            var scale = window.Scale;
            var size = new Vector2(280 * scale, 60 * scale);
            var origin = ImGui.GetCursorScreenPos();
            var end = origin + size;
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(origin, end, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.35f)));
            var samples = memory.Samples;
            var start = now - MemoryGraphSeconds;
            long low = long.MaxValue, high = long.MinValue;

            foreach (var s in samples) {
                if (s.At >= start) {
                    low = Math.Min(low, s.UsedMb);
                    high = Math.Max(high, s.UsedMb);
                }
            }

            if (low <= high) {
                // a little room either side, and at least 100 MB tall, so steady use is a flat line, not noise
                var bottom = Math.Max(0, low - 50);
                var top = Math.Max(high + 50, bottom + 100);
                var color = ImGui.GetColorU32(Colors.Warning);
                Vector2? last = null;

                foreach (var s in samples) {
                    if (s.At < start) {
                        continue;
                    }

                    var x = origin.X + (float)((s.At - start) / MemoryGraphSeconds) * size.X;
                    var y = end.Y - (float)(s.UsedMb - bottom) / (top - bottom) * size.Y;
                    var point = new Vector2(x, y);

                    if (last.HasValue) {
                        draw.AddLine(last.Value, point, color, Math.Max(1, scale));
                    }

                    last = point;
                }

                var faint = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.35f));
                draw.AddText(new Vector2(origin.X + 2, origin.Y), faint, $"{top:N0} MB");
                draw.AddText(new Vector2(origin.X + 2, end.Y - ImGui.GetTextLineHeight()), faint, $"{bottom:N0} MB");
            }

            foreach (var (at, _) in stats.Marks) {
                var x = origin.X + (float)((at - start) / MemoryGraphSeconds) * size.X;

                if (x >= origin.X && x <= end.X) {
                    draw.AddLine(new Vector2(x, origin.Y), new Vector2(x, end.Y),
                        ImGui.GetColorU32(new Vector4(1, 1, 1, 0.25f)));
                }
            }

            ImGui.Dummy(size);
            Widgets.Tip("The address space in use over the last 15 minutes, newest on the right; the faint lines are "
                + "the marks and settings changes.");
        }

        private static Vector4 FreeColor(long freeMb) {
            return freeMb < 768 ? Colors.Danger : freeMb < 1024 ? Colors.Warning : Colors.Text;
        }

        /// <summary>Red for over a megabyte a minute, gold for over a fifth of one, plain otherwise.</summary>
        private static Vector4 GrowthColor(double? perMinute) {
            return !perMinute.HasValue ? Colors.Text : perMinute > 1 ? Colors.Danger
                : perMinute > 0.2 ? Colors.Warning : Colors.Text;
        }

        private static string Rate(double? perMinute) {
            return perMinute.HasValue ? $"{(perMinute >= 0 ? "+" : "")}{perMinute:0.0} MB/min" : "...";
        }

        private static string FormatMinutes(double minutes) {
            return minutes >= 120 ? $"{minutes / 60:0} hours" : $"{minutes:0} minutes";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
    }
}
