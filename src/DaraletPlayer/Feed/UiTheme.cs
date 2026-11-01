using System.Collections.Generic;
using System.Numerics;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// One of the ready-made looks for the plugin's windows (UiThemes.All): a few base colours, from which every one of
    /// ImGui's colours is worked out (Colors), and how round the corners are. The UtilityBelt theme has none: its own
    /// look is left as it is.
    /// </summary>
    internal sealed class UiTheme {
        public string Name;
        public string Tip;
        /// <summary>The windows' background.</summary>
        public Vector4 Window;
        /// <summary>Text boxes, checkboxes, sliders and drop-downs.</summary>
        public Vector4 Frame;
        /// <summary>The title bar.</summary>
        public Vector4 Title;
        /// <summary>Buttons, tabs, headings, the selected row and checkmarks.</summary>
        public Vector4 Accent;
        public Vector4 Text;
        /// <summary>Buttons in a solid colour of their own, as the game's are; null for a see-through Accent.</summary>
        public Vector4? Button;
        /// <summary>Selected and hovered rows and scrollbars in a colour of their own; null to work them out as usual.</summary>
        public Vector4? Selection;
        /// <summary>Solid window borders and separators, as the game's panels have; null for a faint Accent.</summary>
        public Vector4? Edge;
        /// <summary>Corners, in pixels; 0 for square ones.</summary>
        public float Rounding;

        /// <summary>UtilityBelt's own look: nothing is changed.</summary>
        public bool Unstyled;

        private Dictionary<string, Vector4> colors;

        /// <summary>
        /// Every colour the theme sets, by ImGuiCol's name. Names, not the enum, so this needs no ImGui (the tests use it),
        /// and so a colour ImGui renamed (TabActive became TabSelected) is set under whichever name the one loaded has:
        /// one it doesn't know is skipped (UiThemeStyle.Apply).
        /// </summary>
        public IReadOnlyDictionary<string, Vector4> Colors => colors = colors ?? Work();

        private Dictionary<string, Vector4> Work() {
            var c = new Dictionary<string, Vector4>();

            if (Unstyled) {
                return c;
            }

            var none = new Vector4(0, 0, 0, 0);
            var border = Alpha(Mix(Window, Accent, 0.45f), 0.6f);
            var header = Alpha(Accent, 0.31f);
            var headerHovered = Alpha(Accent, 0.8f);
            var headerActive = Accent;
            var titleActive = Mix(Title, Accent, 0.35f);
            var tab = Mix(header, titleActive, 0.8f);
            var tabActive = Mix(headerActive, titleActive, 0.6f);

            c["Text"] = Text;
            c["TextDisabled"] = Mix(Text, Window, 0.5f);
            c["WindowBg"] = Alpha(Window, 0.94f);
            c["ChildBg"] = none;
            c["PopupBg"] = Alpha(Lighter(Window, 0.04f), 0.96f);
            c["Border"] = border;
            c["BorderShadow"] = none;
            c["FrameBg"] = Alpha(Frame, 0.75f);
            c["FrameBgHovered"] = Alpha(Mix(Frame, Accent, 0.3f), 0.8f);
            c["FrameBgActive"] = Alpha(Mix(Frame, Accent, 0.5f), 0.85f);
            c["TitleBg"] = Title;
            c["TitleBgActive"] = titleActive;
            c["TitleBgCollapsed"] = Alpha(Title, 0.6f);
            c["MenuBarBg"] = Frame;
            c["ScrollbarBg"] = Alpha(Darker(Window, 0.03f), 0.6f);
            c["ScrollbarGrab"] = Mix(Frame, Text, 0.25f);
            c["ScrollbarGrabHovered"] = Mix(Frame, Text, 0.35f);
            c["ScrollbarGrabActive"] = Mix(Frame, Text, 0.45f);
            c["CheckMark"] = Lighter(Accent, 0.2f);
            c["SliderGrab"] = Lighter(Accent, 0.1f);
            c["SliderGrabActive"] = Lighter(Accent, 0.25f);
            c["Button"] = Alpha(Accent, 0.45f);
            c["ButtonHovered"] = Alpha(Lighter(Accent, 0.05f), 0.9f);
            c["ButtonActive"] = Lighter(Accent, 0.15f);
            c["Header"] = header;
            c["HeaderHovered"] = headerHovered;
            c["HeaderActive"] = headerActive;
            c["Separator"] = border;
            c["SeparatorHovered"] = Alpha(Accent, 0.78f);
            c["SeparatorActive"] = Accent;
            c["ResizeGrip"] = Alpha(Accent, 0.2f);
            c["ResizeGripHovered"] = Alpha(Accent, 0.67f);
            c["ResizeGripActive"] = Alpha(Accent, 0.95f);
            c["Tab"] = tab;
            c["TabHovered"] = headerHovered;
            c["TabActive"] = tabActive;
            c["TabSelected"] = tabActive;
            c["TabUnfocused"] = Mix(tab, Title, 0.8f);
            c["TabDimmed"] = Mix(tab, Title, 0.8f);
            c["TabUnfocusedActive"] = Mix(tabActive, Title, 0.4f);
            c["TabDimmedSelected"] = Mix(tabActive, Title, 0.4f);
            c["TableHeaderBg"] = Mix(Window, Accent, 0.25f);
            c["TableBorderStrong"] = Mix(Window, Text, 0.25f);
            c["TableBorderLight"] = Mix(Window, Text, 0.15f);
            c["TableRowBg"] = none;
            c["TableRowBgAlt"] = Alpha(Text, 0.04f);
            c["TextSelectedBg"] = Alpha(Accent, 0.4f);
            c["NavHighlight"] = Accent;
            c["NavCursor"] = Accent;

            if (Button is Vector4 button) {
                c["Button"] = button;
                c["ButtonHovered"] = Lighter(button, 0.08f);
                c["ButtonActive"] = Lighter(button, 0.15f);
            }

            if (Selection is Vector4 selection) {
                c["Header"] = Alpha(selection, 0.85f);
                c["HeaderHovered"] = Lighter(selection, 0.1f);
                c["HeaderActive"] = Lighter(selection, 0.2f);
                c["ScrollbarGrab"] = selection;
                c["ScrollbarGrabHovered"] = Lighter(selection, 0.1f);
                c["ScrollbarGrabActive"] = Lighter(selection, 0.2f);
                c["TextSelectedBg"] = Alpha(Lighter(selection, 0.1f), 0.8f);
            }

            if (Edge is Vector4 edge) {
                c["Border"] = edge;
                c["Separator"] = edge;
            }

            return c;
        }

        public static Vector4 Rgb(int r, int g, int b) {
            return new Vector4(r / 255f, g / 255f, b / 255f, 1);
        }

        private static Vector4 Mix(Vector4 a, Vector4 b, float t) {
            return Vector4.Lerp(a, b, t);
        }

        private static Vector4 Alpha(Vector4 c, float alpha) {
            return new Vector4(c.X, c.Y, c.Z, alpha);
        }

        private static Vector4 Lighter(Vector4 c, float by) {
            return new Vector4(Clamp(c.X + by), Clamp(c.Y + by), Clamp(c.Z + by), c.W);
        }

        private static Vector4 Darker(Vector4 c, float by) {
            return Lighter(c, -by);
        }

        private static float Clamp(float v) {
            return v < 0 ? 0 : v > 1 ? 1 : v;
        }
    }
}
