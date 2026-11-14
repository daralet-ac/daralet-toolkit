using System;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's Vitae panel (panel 0xF, gmVitaeUI, layout 0x21000020: a title, and its words at 8,25 in 284x337 on the
    /// stone tile 06004CC2) in a window of its own, by GamePanelInterceptor: how much vitae you've lost and how much more
    /// experience brings a percent back (VitaeInfo), in the game's own words. The vitae is Decal's enchantment 666; the
    /// pool and the level you died at are your int properties 129 and 139 (PrivateInts).
    /// </summary>
    internal sealed class VitaePanel : GamePanel {
        /// <summary>The game's Vitae panel, as the indicators and the panel frame number it.</summary>
        public const uint GamePanelId = 0xF;

        private readonly GamePanelInterceptor interceptor;
        private readonly PrivateInts properties = new PrivateInts("game-panels", VitaeInfo.CpPoolProperty,
            VitaeInfo.DeathLevelProperty);

        public VitaePanel(Settings settings)
            : base("vitae", "Vitae", settings, new uint[0], new Vector2(520, 160), hideWhen: () => false,
                defaultSize: new Vector2(300, 220)) {
            interceptor = new GamePanelInterceptor("Vitae", GamePanelId, () => Options.Replace, Window);
        }

        protected override string ReplaceTip => "Opens the plugin's Vitae window instead of the game's Vitae panel, from "
            + "the indicators, the game's key or anywhere.";

        protected override string WindowTip => "Shows how much vitae, your life force, you've lost from a recent death, "
            + "and how much more experience brings each percent back, as the game's Vitae panel does.";

        /// <summary>What's left of your vitae (0.95 for 5% lost), null for none.</summary>
        public static double? Remaining() {
            foreach (EnchantmentWrapper enchantment in CoreManager.Current.CharacterFilter.Enchantments) {
                if (enchantment.SpellId == VitaeInfo.VitaeSpell) {
                    return enchantment.Adjustment;
                }
            }

            return null;
        }

        protected override void RenderContents() {
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("Not in the world yet.");
                return;
            }

            var text = VitaeInfo.Text(Remaining(), properties[VitaeInfo.DeathLevelProperty],
                properties[VitaeInfo.CpPoolProperty], AttributesOptions.Xp);
            var min = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail();
            var padding = Widgets.Scaled(8);

            if (Retail) {
                GamePanelArt.Stone(ImGui.GetWindowDrawList(), min, min + avail);
            }

            ImGui.SetCursorScreenPos(min + new Vector2(padding, padding));
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(Widgets.Scaled(60), avail.X - padding * 2));
            Widgets.Text(text);
            ImGui.PopTextWrapPos();
        }

        public override void Dispose() {
            properties.Dispose();
            interceptor.Dispose();
            base.Dispose();
        }
    }
}
