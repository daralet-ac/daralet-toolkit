using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// A game crash or hang as Windows records it (the Application event log's "Application Error" and "Application
    /// Hang" entries for acclient.exe), read back into log.txt at the next start (Ui/SessionLog.cs; the user's call,
    /// November 2026: a player's log.txt alone should say what crashed). The crash address is the module's base plus the
    /// fault offset; for acclient.exe (based at 0x400000) the nearest of the client's known functions at or below it
    /// names it (UtilityBelt.Service's AcClient.Entrypoint). Kept apart from the event log so it can be tested.
    /// </summary>
    public sealed class CrashReport {
        public const uint ClientBase = 0x400000;

        public string Module;
        public uint Offset;
        public string Code;
        public bool Hang;

        /// <summary>The fault's address in the game, for a crash in acclient.exe itself; 0 otherwise.</summary>
        public uint Address => Module != null && Module.Equals("acclient.exe", StringComparison.OrdinalIgnoreCase)
            ? ClientBase + Offset : 0;

        /// <summary>A crash entry's text read, or null for one that isn't the game's.</summary>
        public static CrashReport Read(string message, bool hang) {
            if (message == null || message.IndexOf("acclient.exe", StringComparison.OrdinalIgnoreCase) < 0) {
                return null;
            }

            var report = new CrashReport { Hang = hang };

            if (hang) {
                return report;
            }

            var module = Regex.Match(message, @"Faulting module name:\s*([^,\r\n]+)");
            var offset = Regex.Match(message, @"Fault offset:\s*0x([0-9a-fA-F]+)");
            var code = Regex.Match(message, @"Exception code:\s*(0x[0-9a-fA-F]+)");
            report.Module = module.Success ? module.Groups[1].Value.Trim() : "unknown";
            report.Offset = offset.Success ? uint.Parse(offset.Groups[1].Value, NumberStyles.HexNumber) : 0;
            report.Code = code.Success ? code.Groups[1].Value : "unknown";
            return report;
        }

        /// <summary>The nearest known function at or below an address, within 64 KB, as "Name +0x16", or null.</summary>
        public static string NameOf(uint address, IReadOnlyList<(uint Address, string Name)> sorted) {
            string found = null;
            uint at = 0;

            foreach (var (entry, name) in sorted) {
                if (entry > address) {
                    break;
                }

                found = name;
                at = entry;
            }

            return found == null || address - at > 0x10000 ? null : $"{found.Replace("__", "::")} +0x{address - at:X}";
        }

        public string Describe(IReadOnlyList<(uint Address, string Name)> functions) {
            if (Hang) {
                return "the game stopped responding (Windows closed it)";
            }

            var where = Address != 0 ? $"acclient.exe at 0x{Address:X}" + (NameOf(Address, functions) is string name
                ? $" ({name})" : "") : $"{Module} at offset 0x{Offset:X}";
            return $"the game crashed in {where}, exception {Code}";
        }
    }
}
