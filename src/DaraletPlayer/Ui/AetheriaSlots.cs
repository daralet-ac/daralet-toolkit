using System;
using Decal.Adapter;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Your unlocked sigil slots, for the paperdoll, which leaves out the locked ones as the game's panel does. Read from
    /// Decal's copy of your properties from login, and from the server's own update when one unlocks mid-session, in case
    /// Decal doesn't follow that.
    /// </summary>
    internal static class AetheriaSlots {
        private static bool listening;

        /// <summary>The flags the server last sent, and to which character, which come before Decal's for them.</summary>
        private static int? updated;
        private static int updatedFor;

        public static void Start() {
            if (listening) {
                return;
            }

            listening = true;
            CoreManager.Current.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
        }

        public static void Stop() {
            if (!listening) {
                return;
            }

            listening = false;
            CoreManager.Current.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
        }

        /// <summary>Whether a paperdoll place is there to use; before login, every one is.</summary>
        public static bool Unlocked(uint slotMask) {
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                return true;
            }

            var me = CoreManager.Current.CharacterFilter.Id;
            var flags = updated.HasValue && updatedFor == me
                ? updated.Value
                : CoreManager.Current.CharacterFilter.GetCharProperty(AetheriaFlags.Property);
            return AetheriaFlags.Unlocked(slotMask, flags);
        }

        private static void EchoFilter_ServerDispatch(object sender, NetworkMessageEventArgs e) {
            try {
                if (e.Message.Type != AetheriaFlags.PrivateUpdateInt) {
                    return;
                }

                var flags = AetheriaFlags.Read(e.Message.RawData);

                if (flags.HasValue) {
                    updated = flags;
                    updatedFor = CoreManager.Current.CharacterFilter.Id;
                    PluginCore.LogFile($"[aetheria] sigil slots now {flags.Value}");
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }
    }
}
