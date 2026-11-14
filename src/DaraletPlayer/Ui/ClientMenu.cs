using System;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What the game's Options panel's Gameplay Options page does with its buttons (gmGameplayOptionsUI::
    /// ListenToElementMessage, 0x49E390, read with a disassembler), called the same way: Exit to Character Selection
    /// (0x10000203) is CM_UI::SendNotice_EndCharacterSession(1) (0x479F40, as the indicators' X); Exit Game (0x10000617)
    /// and Use Mouse Turning Settings (0x100005CC) are UIElementManager::BroadcastGlobalMessage (0x45B4C0) with 1 and
    /// 0x10000027, and 0xC and 0. Urgent Assistance (0x10000206) and Report Abuse (0x10000207) open
    /// support.turbine.com in a browser, long gone, so the plugin opens the game's own panels for them instead (0xE and
    /// 1). Configure Keyboard and In-Game Help Files (0x10000204, 0x10000205) aren't handled there: what a button does
    /// with its property 0x12 is fire that input action, as its key would (the handler at 0x472210), and theirs are
    /// 0x1000001F, which opens and closes the keyboard window (layout 0x21000009, root element 0x1000001F, gmKeyboardUI
    /// 0x10000020), and 0x7B, the help. Game thread only.
    /// </summary>
    internal static class ClientMenu {
        /// <summary>UIElementManager::s_pInstance.</summary>
        private const int ManagerAddress = 0x83E03C;

        /// <summary>What 0x472210 asks first (its vtable +0x44) before firing a button's input action.</summary>
        private const int GateAddress = 0x837FF4;

        /// <summary>The input event's constructor, and room for it (it sets fields up to +0x40).</summary>
        private const int EventConstructor = 0x430EE0;
        private const int EventSize = 0x48;

        /// <summary>The input actions on the Configure Keyboard and In-Game Help Files buttons (their property 0x12).</summary>
        private const uint KeyboardAction = 0x1000001F;
        private const uint HelpAction = 0x7B;

        private static bool broken;

        public static bool Broken => broken;

        public static bool ExitToCharacterSelection() {
            return ClientToolbar.LogOut();
        }

        public static bool ExitGame() {
            return Broadcast("exit game", 1, 0x10000027);
        }

        public static bool MouseTurningSettings() {
            return Broadcast("mouse turning settings", 0xC, 0);
        }

        public static bool ConfigureKeyboard() {
            return InputAction("configure keyboard", KeyboardAction);
        }

        public static bool HelpFiles() {
            return InputAction("help files", HelpAction);
        }

        /// <summary>
        /// Fires an input action as the game's buttons do (0x472210): only when the object at 0x837FF4 says yes (its vtable
        /// +0x44), an input event (made by 0x430EE0, 0x44 bytes) with the action at +0, 3 at +0x1C and 1 (pressed) at
        /// +0x20, handed to the UIElementManager's first virtual function.
        /// </summary>
        private static unsafe bool InputAction(string what, uint action) {
            if (broken) {
                return false;
            }

            try {
                var gate = *(void**)GateAddress;
                var manager = *(void**)ManagerAddress;

                if (gate == null || manager == null) {
                    return false;
                }

                var allowed = ((delegate* unmanaged[Thiscall]<void*, int>)(*(void***)gate)[0x44 / 4])(gate);

                if (allowed == 0) {
                    PluginCore.LogFile($"[options] {what}: the game isn't taking input actions now");
                    return false;
                }

                var evt = stackalloc byte[EventSize];
                ((delegate* unmanaged[Thiscall]<byte*, void>)EventConstructor)(evt);
                *(uint*)evt = action;
                *(int*)(evt + 0x1C) = 3;
                evt[0x20] = 1;
                ((delegate* unmanaged[Thiscall]<void*, byte*, void>)(*(void***)manager)[0])(manager, evt);
                PluginCore.LogFile($"[options] {what}: input action {action:X}");
                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log($"The game's menu can't be used ({what}), so it's off until the plugin next starts: " + ex);
                return false;
            }
        }

        private static unsafe bool Broadcast(string what, uint message, uint data) {
            if (broken) {
                return false;
            }

            try {
                var manager = *(void**)ManagerAddress;

                if (manager == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, uint, uint, void>)0x45B4C0)(manager, message, data);
                PluginCore.LogFile($"[options] {what}: global message {message}, {data:X}");
                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log($"The game's menu can't be used ({what}), so it's off until the plugin next starts: " + ex);
                return false;
            }
        }
    }
}
