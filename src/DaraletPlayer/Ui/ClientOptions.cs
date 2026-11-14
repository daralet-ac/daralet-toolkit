using System;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's character options, read and set on the client's own PlayerModule (ClientComponents.PlayerModule), as the
    /// game's Options panel does: PlayerModule::GetOption(option) (0x5D4AF0) and PlayerModule::SetOption(option, on)
    /// (0x5D4F20), whose change raises CPlayerModule::OnChanged (0x59B8F0): the game's UI is told
    /// (CM_UI::SendNotice_PlayerOptionChanged, so the effect is immediate and the game's own panel agrees) and the server
    /// is sent it (CM_Character::Event_PlayerOptionChangedEvent for the options saved at once, the rest with the
    /// character's options). Unlike sending Event_PlayerOptionChangedEvent alone (the Social tab's first way), the
    /// client's own copy changes too. Game thread only, once logged in.
    /// </summary>
    internal static class ClientOptions {
        private static bool broken;

        public static bool Broken => broken;

        /// <summary>Whether an option is on, or null when that can't be read.</summary>
        public static unsafe bool? Get(uint option) {
            if (broken) {
                return null;
            }

            try {
                var module = ClientComponents.PlayerModule();
                return module == null ? (bool?)null
                    : ((delegate* unmanaged[Thiscall]<void*, uint, byte>)0x5D4AF0)(module, option) != 0;
            }
            catch (Exception ex) {
                Fail("read", ex);
                return null;
            }
        }

        /// <summary>
        /// An option's default, what the game's Defaults button puts back: PlayerModule::GetDefaultOptionValue (0x5D3A30,
        /// a table in the client; off for any it doesn't list). Null when it can't be read.
        /// </summary>
        public static unsafe bool? Default(uint option) {
            if (broken) {
                return null;
            }

            try {
                var module = ClientComponents.PlayerModule();
                return module == null ? (bool?)null
                    : ((delegate* unmanaged[Thiscall]<void*, uint, byte>)0x5D3A30)(module, option) != 0;
            }
            catch (Exception ex) {
                Fail("read", ex);
                return null;
            }
        }

        /// <summary>Turns an option on or off, as the game's checkbox does.</summary>
        public static unsafe bool Set(uint option, bool on) {
            if (broken) {
                return false;
            }

            try {
                var module = ClientComponents.PlayerModule();

                if (module == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, uint, int, void>)0x5D4F20)(module, option, on ? 1 : 0);
                PluginCore.LogFile($"[options] option {option} {(on ? "on" : "off")}");
                return true;
            }
            catch (Exception ex) {
                Fail("set", ex);
                return false;
            }
        }

        private static void Fail(string what, Exception ex) {
            broken = true;
            PluginCore.Log($"The game's options can't be {(what == "read" ? "read" : "set")}, so the Options window is off "
                + "until the plugin next starts: " + ex);
        }
    }
}
