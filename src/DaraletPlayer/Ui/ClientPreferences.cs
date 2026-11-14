using System;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The client's own preferences (sound, camera, graphics, input: the Options panel's Config page), read and changed
    /// as the game does: UIPreferences' statics (cdecl), each taking the preference's name as the client's own string
    /// object. Those names live in globals the client fills at start (gmClient::InitUIPreferences, 0x4037B0, attaching
    /// each with UIPreferences::AttachPreference(&amp;name, type, 0x10000003, label, help), 0x5DEE50), so the plugin passes
    /// the global's address (ConfigOptions) and never builds a string. Bool items (type 4) are read and set through
    /// InqPreferenceValue 0x5DEC90 / ModifyPreference 0x5DEB90 (the item's vtable +4/+8), floats (type 3) through
    /// 0x5DED50 / 0x5DEC50 (+0x30/+0x34), a float's range through InqPreferenceRange 0x5DE9E0 (+0x38); lists (type 2,
    /// +0x1C/+0x20) aren't changed here yet. Each looks the name up in the table at 0x82514C. A change that took is then
    /// announced (SendNotice_PreferenceChanged), as the game's controls do. Game thread only.
    /// </summary>
    internal static class ClientPreferences {
        private static bool broken;

        public static bool Broken => broken;

        public static unsafe bool? GetBool(uint name) {
            return Try("read", () => {
                byte value = 0;
                var found = ((delegate* unmanaged[Cdecl]<void*, byte*, byte>)0x5DEC90)((void*)name, &value) != 0;
                return found ? value != 0 : (bool?)null;
            });
        }

        public static unsafe bool SetBool(uint name, bool on) {
            return Try("set", () => {
                var changed = ((delegate* unmanaged[Cdecl]<void*, int, byte>)0x5DEB90)((void*)name, on ? 1 : 0) != 0;
                Changed(name, changed);
                PluginCore.LogFile($"[options] preference {name:X}: {on}");
                return (bool?)true;
            }) == true;
        }

        public static unsafe float? GetFloat(uint name) {
            return Try("read", () => {
                float value = 0;
                var found = ((delegate* unmanaged[Cdecl]<void*, float*, byte>)0x5DED50)((void*)name, &value) != 0;
                return found ? value : (float?)null;
            });
        }

        public static unsafe bool SetFloat(uint name, float value) {
            return Try("set", () => {
                var changed = ((delegate* unmanaged[Cdecl]<void*, float, byte>)0x5DEC50)((void*)name, value) != 0;
                Changed(name, changed);
                PluginCore.LogFile($"[options] preference {name:X}: {value}");
                return (bool?)true;
            }) == true;
        }

        /// <summary>A float preference's range, as the game's slider has it; null when it can't be read.</summary>
        public static unsafe (float Min, float Max)? Range(uint name) {
            return Try("read", () => {
                float min = 0, max = 0;
                var found = ((delegate* unmanaged[Cdecl]<void*, float*, float*, byte>)0x5DE9E0)((void*)name, &min, &max)
                    != 0;
                return found && max > min ? (min, max) : ((float, float)?)null;
            });
        }

        /// <summary>
        /// Tells the client a preference changed, as the game's own preference controls do straight after a change that
        /// took (CM_UI::SendNotice_PreferenceChanged, 0x47A740, cdecl, the name): it's what has the camera, sound and the
        /// rest pick the new value up.
        /// </summary>
        private static unsafe void Changed(uint name, bool changed) {
            if (changed) {
                ((delegate* unmanaged[Cdecl]<void*, void>)0x47A740)((void*)name);
            }
        }

        private static T Try<T>(string what, Func<T> call) {
            if (broken) {
                return default;
            }

            try {
                return call();
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log($"The game's preferences can't be {(what == "read" ? "read" : "changed")}, so the Config "
                    + "page is off until the plugin next starts: " + ex);
                return default;
            }
        }
    }
}
