using System;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The client's own combat system (ClientCombatSystem, the one the game's combat panel, gmCombatUI, reads and drives),
    /// read and called at the addresses UtilityBelt.Service's AcClient gives, with plain pointers, as ClientElements does.
    /// Its fields' offsets are AcClient's layout of it (Marshal.OffsetOf on AcClient.ClientCombatSystem, pack 8); the calls
    /// are what gmCombatUI::ListenToElementMessage (0x4CD020) does for the panel's own buttons and slider, read with a
    /// disassembler. Game thread only, and only once logged in: the system's pointer is null before.
    /// </summary>
    internal static class ClientCombat {
        /// <summary>ClientCombatSystem::s_pCombatSystem, which GetCombatSystem (0x56B210) returns.</summary>
        private const int SystemAddress = 0x87166C;

        private const int CombatModeOffset = 0x1C;
        private const int RequestedHeightOffset = 0x24;
        private const int BuildInProgressOffset = 0x30;
        private const int LatestLevelOffset = 0x38;
        private const int AttackRequestInProgressOffset = 0x3E;
        private const int DesiredPowerOffset = 0x4C;
        private const int AdvancedCombatOffset = 0x50;

        /// <summary>
        /// The float gmCombatUI hands EndAttackRequest as the power (-1 in the exe: the bar's own level), read each time, as
        /// the game does.
        /// </summary>
        private const int ReleasePowerAddress = 0x7B9420;

        private static bool broken;

        /// <summary>A read or call has failed, so nothing more is tried until the next start.</summary>
        public static bool Broken => broken;

        /// <summary>What the game's combat panel shows now; false before login, or once a call has failed.</summary>
        public static unsafe bool TryRead(out CombatSnapshot snapshot) {
            snapshot = default;

            if (broken) {
                return false;
            }

            try {
                var system = *(byte**)SystemAddress;

                if (system == null) {
                    return false;
                }

                var building = *(system + BuildInProgressOffset) != 0;
                snapshot = new CombatSnapshot {
                    Mode = *(int*)(system + CombatModeOffset),
                    Height = *(int*)(system + RequestedHeightOffset),
                    Building = building,
                    // while it builds, its level now (GetPowerBarLevel, 0x56BB20, worked out from the time); else the last
                    // one, which the bar keeps showing
                    Level = PowerBarOptions.Clamp(building ? GetPowerBarLevel(system)
                        : *(float*)(system + LatestLevelOffset)),
                    AttackHeld = *(system + AttackRequestInProgressOffset) != 0,
                    Desired = PowerBarOptions.Clamp(*(float*)(system + DesiredPowerOffset)),
                    Advanced = *(system + AdvancedCombatOffset) != 0,
                };
                return true;
            }
            catch (Exception ex) {
                Fail("read", ex);
                return false;
            }
        }

        /// <summary>The client's combat mode (PowerBarOptions.NonCombat...), or 0 when it can't be read.</summary>
        public static int Mode => TryRead(out var snapshot) ? snapshot.Mode : 0;

        /// <summary>
        /// A height button pressed: ClientCombatSystem::SetRequestedAttackHeight (0x56D640), which also starts building the
        /// attack, as holding the game's button does.
        /// </summary>
        public static unsafe bool PressHeight(int height) {
            try {
                var system = System();

                if (system == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, int, void>)0x56D640)(system, height);
                return true;
            }
            catch (Exception ex) {
                Fail("press", ex);
                return false;
            }
        }

        /// <summary>
        /// A height button let go: ClientCombatSystem::EndAttackRequest (0x56CE30), which attacks at the bar's level (no
        /// higher than the desired power), as letting go of the game's button does.
        /// </summary>
        public static unsafe bool ReleaseHeight(int height) {
            try {
                var system = System();

                if (system == null) {
                    return false;
                }

                var power = *(float*)ReleasePowerAddress;
                ((delegate* unmanaged[Thiscall]<void*, int, float, void>)0x56CE30)(system, height, power);
                return true;
            }
            catch (Exception ex) {
                Fail("release", ex);
                return false;
            }
        }

        /// <summary>
        /// The desired power, the green mark the game's slider sets: what gmCombatUI does with its slider, stored in the
        /// system, then CM_Combat::SendNotice_DesiredAttackPowerChanged (0x6AAE60, static), so the game's own slider
        /// (hidden or not) follows.
        /// </summary>
        public static unsafe bool SetDesiredPower(float fraction) {
            try {
                var system = System();

                if (system == null) {
                    return false;
                }

                var value = PowerBarOptions.Clamp(fraction);
                *(float*)(system + DesiredPowerOffset) = value;
                ((delegate* unmanaged[Cdecl]<float, byte>)0x6AAE60)(value);
                return true;
            }
            catch (Exception ex) {
                Fail("desired power", ex);
                return false;
            }
        }

        /// <summary>The combat system, or null before login or once a call has failed.</summary>
        private static unsafe byte* System() {
            return broken ? null : *(byte**)SystemAddress;
        }

        private static unsafe float GetPowerBarLevel(byte* system) {
            return ((delegate* unmanaged[Thiscall]<void*, float>)0x56BB20)(system);
        }

        private static void Fail(string what, Exception ex) {
            broken = true;
            PluginCore.Log("The game's combat system can't be " + (what == "read" ? "read" : "used (" + what + ")")
                + ", so the power bar is off until the plugin next starts: " + ex);
        }
    }
}
