using System;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's elements found under one of its windows (UIElement::GetChildRecursive from that window's element, as
    /// the container window's lists are), for a window whose pieces' ids are used elsewhere too (the examination panel's,
    /// CreatureModelView): shown or hidden, and moved and sized, as SetVisible and Place do for one found by id anywhere.
    /// The window itself is passed as its own id for the window.
    /// </summary>
    internal static partial class ClientElements {
        /// <summary>The element under that window (or the window itself), or null. May throw.</summary>
        private static unsafe void* FindUnder(uint windowId, uint id) {
            var window = FindElement(windowId);

            if (window == null || id == windowId) {
                return window;
            }

            return GetChildRecursive(window, id);
        }

        /// <summary>Whether the element under that window is shown; null when there's none, or it can't be told.</summary>
        public static unsafe bool? IsShownUnder(uint windowId, uint id) {
            if (broken) {
                return null;
            }

            try {
                var element = FindUnder(windowId, id);
                return element == null ? (bool?)null : IsVisible(element);
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be found, so the plugin's lookups are off until it next starts: " + ex);
                return null;
            }
        }

        /// <summary>Shows or hides the element under that window (UIElement::SetVisible). False when it can't.</summary>
        public static unsafe bool SetVisibleUnder(uint windowId, uint id, bool shown) {
            if (broken && !shown) {
                return false;
            }

            try {
                var element = FindUnder(windowId, id);

                if (element == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, byte, void>)0x462390)(element, (byte)(shown ? 1 : 0));
                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be " + (shown ? "shown" : "hidden") + ", so the plugin's lookups are off "
                    + "until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>Where the element under that window is in its parent, and its size. False when it can't be told.</summary>
        public static unsafe bool TryGetPlaceUnder(uint windowId, uint id, out int x, out int y, out int width,
            out int height) {
            x = y = width = height = 0;

            if (broken) {
                return false;
            }

            try {
                var element = (byte*)FindUnder(windowId, id);

                if (element == null) {
                    return false;
                }

                x = *(int*)(element + 0x7C);
                y = *(int*)(element + 0x80);
                width = GetWidth(element);
                height = GetHeight(element);
                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be found, so the plugin's lookups are off until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Moves and sizes the element under that window (UIElement::ResizeTo, then MoveTo, called as themselves, as Place
        /// does). False when it can't.
        /// </summary>
        public static unsafe bool PlaceUnder(uint windowId, uint id, int x, int y, int width, int height) {
            if (broken) {
                return false;
            }

            try {
                var element = FindUnder(windowId, id);

                if (element == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, int, int, void>)0x463D60)(element, width, height);
                ((delegate* unmanaged[Thiscall]<void*, int, int, void>)0x4634C0)(element, x, y);
                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be moved, so the plugin's lookups are off until it next starts: " + ex);
                return false;
            }
        }
    }
}
