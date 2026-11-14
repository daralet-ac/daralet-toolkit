using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Indicators window (indicators): the game's indicator bar, INDI (element 0x10000611, layout 0x21000071), in a
    /// window of its own: link status, beneficial and harmful spells on you, vitae, burden, a game of chess, and the X that
    /// logs out. Each shows what the game's own shows, read from the state the game draws it in (ClientElements.GetState:
    /// lit or dimmed, the link's four states, the burden pack's three), so nothing's worked out twice; and a click opens its
    /// panel as the game's does (ClientElements.SetGamePanel), the burden pack the plugin's Character window where Client
    /// UI Override's Character is ticked. The X asks first, then logs out as the game's does (ClientToolbar.LogOut). Retail
    /// draws the game's 20-pixel icons; Modern a row of themed buttons that say the same in words.
    /// </summary>
    internal sealed class IndicatorsPanel : GamePanel {
        /// <summary>The game's indicators, as Decal's UIElementType Indicators names them.</summary>
        public const uint GameIndicators = 0x10000611;

        private const string LogOutPopup = "Log out?##indicators-log-out";

        /// <summary>Each indicator is 20 pixels square in the game.</summary>
        private const float RetailSize = 20;

        /// <summary>The plugin's windows some indicators open instead of the game's panel, while their Func says so.</summary>
        private readonly Dictionary<string, (Func<bool> Instead, FloatingWindow Window)> windows;
        private bool openLogOut;

        public IndicatorsPanel(Settings settings, Dictionary<string, (Func<bool>, FloatingWindow)> windows)
            : base("indicators", "Indicators", settings, new[] { GameIndicators }, new Vector2(640, 500)) {
            this.windows = new Dictionary<string, (Func<bool> Instead, FloatingWindow Window)>();

            foreach (var pair in windows) {
                this.windows[pair.Key] = pair.Value;
            }
        }

        private IndicatorOptions Data => settings.Data.Indicators ?? (settings.Data.Indicators = new IndicatorOptions());

        protected override string ReplaceTip => "Hides the game's indicators (link status, buffs, debuffs, vitae, burden, "
            + "chess and the X that logs out) and shows the plugin's Indicators window in their place.";

        protected override string WindowTip => "Shows the game's indicators in a window of their own: link status, buffs, "
            + "debuffs, vitae, burden and chess, each opening its panel, and the X that logs out.";

        protected override void RenderContents() {
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("Not in the world yet.");
                return;
            }

            var data = Data;
            var retail = Retail;
            var openPanel = ClientElements.OpenGamePanel();
            var first = true;

            foreach (var indicator in IndicatorOptions.All) {
                if (indicator.Key == IndicatorOptions.LogOut && !data.ShowLogOut) {
                    continue;
                }

                if (!first && !data.Vertical) {
                    ImGui.SameLine(0, retail ? 0 : -1);
                }

                first = false;
                var state = ClientElements.GetState(indicator.Element);

                if (retail) {
                    RenderRetail(indicator, state, openPanel);
                }
                else {
                    RenderModern(indicator, state, openPanel);
                }
            }

            RenderLogOutPopup();
        }

        private void RenderRetail(IndicatorInfo indicator, uint? state, uint openPanel) {
            var size = new Vector2(Widgets.Scaled(RetailSize), Widgets.Scaled(RetailSize));
            var min = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton($"##indicator-{indicator.Key}", size);
            var pressed = ImGui.IsItemActive();
            Click(indicator, openPanel);
            Widgets.Tip(Tip(indicator, state));

            var drawList = ImGui.GetWindowDrawList();
            var art = IndicatorOptions.Art(indicator.Key, state)
                + (indicator.Key == IndicatorOptions.LogOut && pressed ? "-pressed" : "");

            if (!ItemIcons.DrawArtBox(drawList, art, min, min + size)) {
                drawList.AddRectFilled(min, min + size, ImGui.GetColorU32(ImGuiCol.Button));
            }

            if (IsOpen(indicator, openPanel)) {
                ItemIcons.DrawArtBox(drawList, "toolbar-character-open", min, min + size);
            }
        }

        private void RenderModern(IndicatorInfo indicator, uint? state, uint openPanel) {
            var text = ModernText(indicator, state);
            var dim = state == IndicatorOptions.DimState;

            if (dim) {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            }
            else if (indicator.Key == IndicatorOptions.Link || indicator.Key == IndicatorOptions.Burden
                || indicator.Key == IndicatorOptions.Debuffs) {
                ImGui.PushStyleColor(ImGuiCol.Text, Severity(indicator.Key, state));
            }

            Form.LitButton($"{text}##indicator-{indicator.Key}", IsOpen(indicator, openPanel));

            if (dim || indicator.Key == IndicatorOptions.Link || indicator.Key == IndicatorOptions.Burden
                || indicator.Key == IndicatorOptions.Debuffs) {
                ImGui.PopStyleColor();
            }

            Click(indicator, openPanel);
            Widgets.Tip(Tip(indicator, state));
        }

        /// <summary>Green, yellow or red, as the game's own icon would be.</summary>
        private static Vector4 Severity(string key, uint? state) {
            switch (key) {
                case IndicatorOptions.Link:
                    return state == IndicatorOptions.LinkFair ? Colors.Warning
                        : state == IndicatorOptions.LinkPoor || state == IndicatorOptions.LinkLost ? Colors.Danger : Colors.Good;
                case IndicatorOptions.Burden:
                    return state == IndicatorOptions.BurdenVeryOver ? Colors.Danger
                        : state == IndicatorOptions.BurdenOver ? Colors.Warning : Colors.Text;
                default:
                    return Colors.Danger;
            }
        }

        private static string ModernText(IndicatorInfo indicator, uint? state) {
            var character = CoreManager.Current.CharacterFilter;

            switch (indicator.Key) {
                case IndicatorOptions.Link:
                    return "Link: " + IndicatorOptions.LinkWord(state);
                case IndicatorOptions.Buffs:
                    return $"Buffs {SpellCounts().Buffs}";
                case IndicatorOptions.Debuffs:
                    return $"Debuffs {SpellCounts().Debuffs}";
                case IndicatorOptions.Burden:
                    return $"Burden {character.Burden}%";
                default:
                    return indicator.Label;
            }
        }

        /// <summary>How many beneficial and harmful spells are on you, as the portal dat says each is (cooldowns left out).</summary>
        private static (int Buffs, int Debuffs) SpellCounts() {
            var buffs = 0;
            var debuffs = 0;

            foreach (var enchantment in CoreManager.Current.CharacterFilter.Enchantments) {
                var info = Spells.Info(enchantment.SpellId);

                if (info == null) {
                    continue;
                }

                if (info.Beneficial) {
                    buffs++;
                }
                else {
                    debuffs++;
                }
            }

            return (buffs, debuffs);
        }

        private string Tip(IndicatorInfo indicator, uint? state) {
            var dim = state == IndicatorOptions.DimState;
            var opens = WindowFor(indicator) != null
                ? $" Click to open or close Daralet Toolkit's {WindowFor(indicator).Title} window."
                : " Click to open or close the game's panel for it, as the game's indicator does.";

            switch (indicator.Key) {
                case IndicatorOptions.Link:
                    return $"Link status: {IndicatorOptions.LinkWord(state)}, your connection to the server as the game "
                        + "judges it." + opens;
                case IndicatorOptions.Buffs:
                    return $"Beneficial spells on you: {SpellCounts().Buffs}." + opens;
                case IndicatorOptions.Debuffs:
                    return $"Harmful spells on you: {SpellCounts().Debuffs}." + opens;
                case IndicatorOptions.Vitae:
                    return (dim ? "Your vitae, your life force, is at full strength."
                        : "You've lost vitae from a recent death: earning experience brings it back.") + opens;
                case IndicatorOptions.Burden:
                    return $"Your burden is {CoreManager.Current.CharacterFilter.Burden}%: the pack turns yellow over 100%, "
                        + "and red at 200%." + opens;
                case IndicatorOptions.MiniGame:
                    return (dim ? "No game of chess going on." : "A game of chess is going on.") + opens;
                default:
                    return "Log out: asks first, then logs out as the game's X does.";
            }
        }

        /// <summary>The plugin's window this indicator opens, while it takes the game's panel's place; else null.</summary>
        private FloatingWindow WindowFor(IndicatorInfo indicator) {
            return windows.TryGetValue(indicator.Key, out var entry) && entry.Instead() ? entry.Window : null;
        }

        private bool IsOpen(IndicatorInfo indicator, uint openPanel) {
            var window = WindowFor(indicator);

            if (window != null) {
                return window.Visible;
            }

            return indicator.Panel != 0 && openPanel == indicator.Panel;
        }

        private void Click(IndicatorInfo indicator, uint openPanel) {
            if (!ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                return;
            }

            if (indicator.Key == IndicatorOptions.LogOut) {
                openLogOut = true;
                return;
            }

            var window = WindowFor(indicator);

            if (window != null) {
                window.Visible = !window.Visible;

                if (window.Visible) {
                    window.BringToFront();
                }

                return;
            }

            var open = openPanel == indicator.Panel;
            ClientElements.SetGamePanel(indicator.Panel, !open);
            PluginCore.LogFile($"[indicators] {indicator.Label}: the game's panel 0x{indicator.Panel:X} "
                + (open ? "closed" : "opened"));
        }

        /// <summary>The X asks before logging out: a stray click shouldn't end the session.</summary>
        private void RenderLogOutPopup() {
            if (openLogOut) {
                openLogOut = false;
                ImGui.OpenPopup(LogOutPopup);
            }

            if (!ImGui.BeginPopup(LogOutPopup)) {
                return;
            }

            Widgets.Text($"Log {CoreManager.Current.CharacterFilter.Name} out?");

            if (ImGui.Button("Log out##indicators")) {
                ClientToolbar.LogOut();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel##indicators")) {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        protected override void RenderOwnMenu() {
            var data = Data;
            var changed = false;
            ImGui.Separator();

            if (ImGui.MenuItem("Log out button", null, data.ShowLogOut)) {
                data.ShowLogOut = !data.ShowLogOut;
                changed = true;
            }

            if (ImGui.MenuItem("One under another", null, data.Vertical)) {
                data.Vertical = !data.Vertical;
                changed = true;
            }

            if (changed) {
                settings.Changed();
            }
        }

        protected override void RenderOwnSettings() {
            var data = Data;
            var first = true;
            var changed = false;

            changed |= Form.FlowCheck(ref first, "Log out button##indicators", ref data.ShowLogOut,
                "Shows the X that logs out, as the game's indicators have. It asks first.");
            changed |= Form.FlowCheck(ref first, "One under another##indicators", ref data.Vertical,
                "Puts the indicators in a column, rather than in a row as the game has them.");

            if (changed) {
                settings.Changed();
            }
        }
    }
}
