using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A kept caster is let go once the game writes its buffers again. Managed buffers aren't all filled once: kept from
    /// frame to frame, some records drew whatever the game had put in their buffers since, in the wrong places, and the
    /// shadow maps filled with it (seen in the game, November 2026: no shadows, the scene darker, until sun shadows were
    /// switched off and on). So the buffers' Lock (vtable 11, for vertex and index buffers alike; one table per kind,
    /// patched once each) notes every buffer locked to write, and before each frame's replays a record not seen this
    /// frame whose buffer was is let go (PurgeWritten). The patches are put back with the rest (Unhook); their delegates
    /// are kept for good, since another thread may be inside one.
    /// </summary>
    internal static unsafe partial class ShadowCasters {
        private const int LockSlot = 11;
        private const int LockReadOnly = 0x10;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int LockCall(IntPtr buffer, int offset, int size, IntPtr data, int flags);

        /// <summary>Each patched table's own Lock, by table: replaced whole on adding, so another thread reads it safely.</summary>
        private static volatile Dictionary<IntPtr, LockCall> lockOriginals = new Dictionary<IntPtr, LockCall>();

        private static readonly Dictionary<IntPtr, IntPtr> lockPatched = new Dictionary<IntPtr, IntPtr>();
        private static readonly List<Delegate> lockDelegates = new List<Delegate>();
        private static readonly HashSet<IntPtr> written = new HashSet<IntPtr>();
        private static LockCall lockHook;

        /// <summary>Kept records let go for a buffer written since (for the report).</summary>
        public static int DroppedWritten;

        /// <summary>How many of the known casters weren't seen this frame (for the report).</summary>
        public static int KeptOutOfView {
            get {
                var kept = 0;

                for (var i = 0; i < count; i++) {
                    if (pool[i].SeenFrame != frame) {
                        kept++;
                    }
                }

                return kept;
            }
        }

        public static int MovingMeshes => moving.Count;

        /// <summary>A buffer's kind of Lock patched, the first time one of that kind is seen.</summary>
        private static void FollowWrites(IntPtr buffer) {
            var table = *(IntPtr**)buffer;

            if (lockPatched.ContainsKey((IntPtr)table)) {
                return;
            }

            if (lockHook == null) {
                lockHook = LockHook;
                lockDelegates.Add(lockHook);
            }

            var original = table[LockSlot];
            var originals = new Dictionary<IntPtr, LockCall>(lockOriginals) {
                [(IntPtr)table] = Marshal.GetDelegateForFunctionPointer<LockCall>(original),
            };
            lockOriginals = originals;
            lockPatched[(IntPtr)table] = original;
            WriteSlot(table, LockSlot, Marshal.GetFunctionPointerForDelegate(lockHook));
        }

        private static int LockHook(IntPtr buffer, int offset, int size, IntPtr data, int flags) {
            try {
                if ((flags & LockReadOnly) == 0) {
                    lock (written) {
                        written.Add(buffer);
                    }
                }
            }
            catch {
                // nothing may escape into the game
            }

            return lockOriginals[*(IntPtr*)buffer](buffer, offset, size, data, flags);
        }

        /// <summary>Records not seen this frame whose vertex or index buffer was written since let go.</summary>
        private static void PurgeWritten() {
            lock (written) {
                if (written.Count == 0) {
                    return;
                }

                for (var i = count - 1; i >= 0; i--) {
                    var d = pool[i];

                    if (d.SeenFrame != frame && (written.Contains(d.Stream) || written.Contains(d.Indices))) {
                        RemoveAt(i);
                        DroppedWritten++;
                    }
                }

                written.Clear();
            }
        }

        /// <summary>The buffers' own Lock put back.</summary>
        private static void StopFollowingWrites() {
            foreach (var patched in lockPatched) {
                WriteSlot((IntPtr*)patched.Key, LockSlot, patched.Value);
            }

            lockPatched.Clear();

            lock (written) {
                written.Clear();
            }
        }

        private static void WriteSlot(IntPtr* table, int slot, IntPtr value) {
            var at = (IntPtr)(table + slot);
            VirtualProtect(at, (UIntPtr)4, 0x04, out var old);
            table[slot] = value;
            VirtualProtect(at, (UIntPtr)4, old, out _);
        }
    }
}
