using System.Collections.Generic;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A drop-down for a key to hold (Tooltips only while holding, Right-click menu only while holding): Alt, Ctrl or
    /// Shift, or Other key..., which takes the next key pressed. Each control keeps one, for its own capture.
    /// </summary>
    internal class KeyChoice {
        /// <summary>The modifier keys, which Other key... takes on their own too.</summary>
        private static readonly int[] Modifiers = { Keys.Alt, Keys.Control, Keys.Shift };

        /// <summary>Modifiers already held as Other key... began, not taken until let go and pressed again.</summary>
        private readonly HashSet<int> modifiersHeld = new HashSet<int>();

        /// <summary>Other key... picked: the next key pressed is the one.</summary>
        private bool capturing;

        public static string Name(int key) {
            return key == Keys.Alt ? "Alt" : key == Keys.Control ? "Ctrl" : key == Keys.Shift ? "Shift" : Keys.Name(key);
        }

        /// <summary>
        /// Draws the drop-down for key (0 meaning Alt) and, while Other key... waits, reads the keyboard. True when key was
        /// changed. A tooltip for it goes straight after, on the drop-down.
        /// </summary>
        public bool Render(string id, ref int key) {
            var shown = key != 0 ? key : Keys.Alt;
            var changed = false;
            ImGui.SetNextItemWidth(Widgets.Scaled(110));

            if (Combos.Begin(id, capturing ? "Press a key..." : Name(shown))) {
                foreach (var choice in Modifiers) {
                    if (Combos.Item(Name(choice), shown == choice)) {
                        key = choice;
                        changed = true;
                        capturing = false;
                    }
                }

                if (Combos.Item("Other key...", capturing)) {
                    capturing = true;
                    modifiersHeld.Clear();

                    foreach (var modifier in Modifiers) {
                        if (Hotkeys.IsDown(modifier)) {
                            modifiersHeld.Add(modifier);
                        }
                    }
                }

                Combos.End();
            }

            return Capture(ref key) || changed;
        }

        private bool Capture(ref int key) {
            if (!capturing || !Hotkeys.GameInFront()) {
                return false;
            }

            // Alt, Ctrl or Shift on its own: the key list Hotkeys.Pressed reads (the hotbars' keys) leaves them out, so
            // Other key... ignored them. One that goes down since the capture began is the key.
            foreach (var modifier in Modifiers) {
                var down = Hotkeys.IsDown(modifier);

                if (down && !modifiersHeld.Contains(modifier)) {
                    key = modifier;
                    capturing = false;
                    return true;
                }

                if (!down) {
                    modifiersHeld.Remove(modifier);
                }
            }

            foreach (var pressed in Hotkeys.Pressed()) {
                capturing = false;

                if (pressed == Keys.Escape) {
                    EscapeCloses.Taken();
                    return false;
                }

                key = pressed;
                return true;
            }

            return false;
        }
    }
}
