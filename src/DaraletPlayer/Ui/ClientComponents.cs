using System;
using System.Collections.Generic;
using ACE.DatLoader.FileTypes;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Spell component buy levels: how many of each the game's /fillcomps buys up to at a vendor
    /// (ClientCommunicationSystem::DoFillComponents, 0x570B20, the client's own command). The client keeps them on its
    /// PlayerModule (CPlayerSystem::GetPlayerSystem, 0x55E1D0, its playerModule at +0x30, the PlayerModule in it at +0x34)
    /// and the server keeps a copy (Character fill components, sent back at login). Setting one is what the game's
    /// components page does (its handler at 0x48A7C3): CM_Character::Event_SetDesiredComponentLevel(wcid, amount)
    /// (0x6A3170, static cdecl) to the server, then PlayerModule::SetDesiredCompLevel(wcid, amount) (0x5D5A90) on the
    /// client's copy, so /fillcomps sees it at once. Levels go by the component's weenie class id: the portal dat's dual
    /// DID mapper 0x27000002 turns a spell formula's component id into one (daralet-ACE's Spell.GetComponentWCID).
    /// Game thread only, once logged in.
    /// </summary>
    internal static class ClientComponents {
        private const uint ComponentWcids = 0x27000002;
        private const int PlayerModuleOffset = 0x34;

        private static Dictionary<uint, uint> wcids;
        private static bool broken;

        public static bool Broken => broken;

        /// <summary>The weenie class id of a spell component by its spell component table id, or 0.</summary>
        public static uint Wcid(uint componentId) {
            try {
                if (wcids == null) {
                    var map = UBService.PortalDat?.ReadFromDat<DualDidMapper>(ComponentWcids);

                    if (map == null) {
                        return 0;
                    }

                    wcids = new Dictionary<uint, uint>(map.ClientEnumToID);
                }

                return wcids.TryGetValue(componentId, out var wcid) ? wcid : 0;
            }
            catch (Exception ex) {
                if (wcids == null) {
                    wcids = new Dictionary<uint, uint>();
                    PluginCore.LogFile("[components] the component wcid table can't be read: " + ex.Message);
                }

                return 0;
            }
        }

        /// <summary>How many of a component /fillcomps buys up to (PlayerModule::GetDesiredCompLevel), or null.</summary>
        public static unsafe int? Level(uint wcid) {
            if (broken || wcid == 0) {
                return null;
            }

            try {
                var module = PlayerModule();
                return module == null ? (int?)null
                    : ((delegate* unmanaged[Thiscall]<void*, uint, int>)0x5D5850)(module, wcid);
            }
            catch (Exception ex) {
                Fail("read", ex);
                return null;
            }
        }

        /// <summary>Sets how many of a component /fillcomps buys up to, as the game's components page does; 0 for none.</summary>
        public static unsafe bool SetLevel(uint wcid, int amount) {
            if (broken || wcid == 0) {
                return false;
            }

            amount = Math.Max(0, amount);

            try {
                var module = PlayerModule();

                if (module == null) {
                    return false;
                }

                ((delegate* unmanaged[Cdecl]<uint, int, byte>)0x6A3170)(wcid, amount);
                ((delegate* unmanaged[Thiscall]<void*, uint, int, byte>)0x5D5A90)(module, wcid, amount);
                PluginCore.LogFile($"[components] buy level for {wcid}: {amount}");
                return true;
            }
            catch (Exception ex) {
                Fail("set", ex);
                return false;
            }
        }

        /// <summary>The client's PlayerModule (its options, its component buy levels), or null before login.</summary>
        internal static unsafe void* PlayerModule() {
            var system = ((delegate* unmanaged[Cdecl]<byte*>)0x55E1D0)();
            return system == null ? null : system + PlayerModuleOffset;
        }

        private static void Fail(string what, Exception ex) {
            broken = true;
            PluginCore.Log($"The game's component buy levels can't be {(what == "read" ? "read" : "set")}, so they're off "
                + "until the plugin next starts: " + ex);
        }
    }
}
