using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's Positive and Negative Magic panels (panels 4 and 5, gmEffectsUI, layout 0x2100001B, 300x362, one class
    /// with property 0x1000000C 1 or 2) in windows of their own, Buffs and Debuffs, by GamePanelInterceptor: the spells on
    /// you that are beneficial, or harmful, as the portal dat's spell table says (cooldowns left out, as the indicators
    /// count them), from Decal's CharacterFilter.Enchantments. As the game's: a list (0x10000123) of 300x32 rows (template
    /// 0x10000128: the row 06001396, picked 06001397, a 32x32 icon, the name at 37 and the time left at 225), and under it
    /// the picked spell's description on 06001395 (0x10000125, 300x88). Ordered by name, then by time left.
    /// </summary>
    internal sealed class EffectsPanel : GamePanel {
        public const uint BuffsPanel = 4;
        public const uint DebuffsPanel = 5;

        private const float RowHeight = 32;
        private const float BoxHeight = 88;

        private readonly ItemIcons icons;
        private readonly GamePanelInterceptor interceptor;
        private readonly bool beneficial;

        /// <summary>The picked row: its spell and layer.</summary>
        private (int Spell, int Layer) picked;

        public EffectsPanel(Settings settings, ItemIcons icons, bool beneficial)
            : base(beneficial ? "buffs" : "debuffs", beneficial ? "Buffs" : "Debuffs", settings, new uint[0],
                new Vector2(beneficial ? 520 : 840, 160), hideWhen: () => false, defaultSize: new Vector2(300, 362)) {
            this.icons = icons;
            this.beneficial = beneficial;
            interceptor = new GamePanelInterceptor(Name, beneficial ? BuffsPanel : DebuffsPanel, () => Options.Replace,
                Window);
        }

        protected override string ReplaceTip => $"Opens the plugin's {Name} window instead of the game's "
            + $"{(beneficial ? "Positive" : "Negative")} Magic panel, from the indicators, the game's key or anywhere.";

        protected override string WindowTip => $"Shows the {(beneficial ? "beneficial" : "harmful")} spells on you, with "
            + "the time each has left and what the one picked does, as the game's panel does.";

        /// <summary>A spell on you, as the list shows it.</summary>
        private sealed class Effect {
            public int Spell;
            public int Layer;
            public SpellInfo Info;
            /// <summary>Seconds left; null for one that never runs out.</summary>
            public double? Left;
        }

        protected override void RenderContents() {
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("Not in the world yet.");
                return;
            }

            var retail = Retail;
            var effects = Effects();

            if (!effects.Any(e => e.Spell == picked.Spell && e.Layer == picked.Layer)) {
                picked = effects.Count > 0 ? (effects[0].Spell, effects[0].Layer) : (0, 0);
            }

            var spacing = ImGui.GetStyle().ItemSpacing.Y;
            var box = Math.Max(Widgets.Scaled(BoxHeight), ImGui.GetTextLineHeightWithSpacing() * 4 + Widgets.Scaled(12));

            if (Widgets.BeginChild($"{Key}-list", new Vector2(0, -(box + spacing)), false)) {
                RenderRows(effects, retail);
            }

            Widgets.EndChild();
            RenderDescription(effects.FirstOrDefault(e => e.Spell == picked.Spell && e.Layer == picked.Layer), box, retail);
        }

        private List<Effect> Effects() {
            var effects = new List<Effect>();

            foreach (EnchantmentWrapper enchantment in CoreManager.Current.CharacterFilter.Enchantments) {
                var info = Spells.Info(enchantment.SpellId);

                // a cooldown isn't in the spell table; the vitae has its own panel
                if (info == null || info.Beneficial != beneficial || enchantment.SpellId == VitaeInfo.VitaeSpell) {
                    continue;
                }

                effects.Add(new Effect {
                    Spell = enchantment.SpellId,
                    Layer = enchantment.Layer,
                    Info = info,
                    Left = enchantment.Duration > 0 ? Math.Max(0, enchantment.TimeRemaining) : (double?)null,
                });
            }

            return effects.OrderBy(e => e.Info.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Left ?? double.MaxValue).ToList();
        }

        private void RenderRows(List<Effect> effects, bool retail) {
            if (effects.Count == 0) {
                Widgets.TextDisabled(beneficial ? "No beneficial spells on you." : "No harmful spells on you.");
                return;
            }

            var height = Math.Max(Widgets.Scaled(RowHeight), ImGui.GetTextLineHeight() + 4);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0));

            for (var i = 0; i < effects.Count; i++) {
                RenderRow(effects[i], i, height, retail);
            }

            ImGui.PopStyleVar();
        }

        private void RenderRow(Effect effect, int index, float height, bool retail) {
            var min = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var max = min + new Vector2(width, height);
            var isPicked = effect.Spell == picked.Spell && effect.Layer == picked.Layer;

            if (ImGui.InvisibleButton($"##{Key}-row-{index}", new Vector2(width, height))) {
                picked = (effect.Spell, effect.Layer);
            }

            var hovered = ImGui.IsItemHovered();
            Widgets.Tip(effect.Left.HasValue ? $"{effect.Info.Name}: {Widgets.Duration(effect.Left.Value)} left."
                : $"{effect.Info.Name}: it doesn't run out.");

            var drawList = ImGui.GetWindowDrawList();

            if (!retail || !ItemIcons.DrawArtBox(drawList, isPicked ? "effect-row-selected" : "effect-row", min, max)) {
                if (isPicked || hovered) {
                    drawList.AddRectFilled(min, max, ImGui.GetColorU32(isPicked ? ImGuiCol.Header : ImGuiCol.HeaderHovered));
                }
            }
            else if (hovered && !isPicked) {
                drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.HeaderHovered));
            }

            var icon = Math.Min(height, Widgets.Scaled(32));
            var layers = Spells.Icon(effect.Spell);

            if (layers != null) {
                icons.DrawAt(drawList, layers, new Vector2(min.X, (float)Math.Round(min.Y + (height - icon) / 2)), icon);
            }

            var textY = (float)Math.Round(min.Y + (height - ImGui.GetTextLineHeight()) / 2);
            var time = effect.Left.HasValue ? Widgets.Duration(effect.Left.Value) : "";
            var timeWidth = ImGui.CalcTextSize(time).X;
            var nameX = min.X + icon + Widgets.Scaled(5);
            var timeX = max.X - timeWidth - Widgets.Scaled(6);

            // the name stops short of the time
            drawList.PushClipRect(new Vector2(nameX, min.Y), new Vector2(Math.Max(nameX, timeX - Widgets.Scaled(4)), max.Y),
                true);
            drawList.AddText(new Vector2(nameX, textY), ImGui.GetColorU32(ImGuiCol.Text), effect.Info.Name);
            drawList.PopClipRect();

            if (time.Length > 0) {
                drawList.AddText(new Vector2(timeX, textY), ImGui.GetColorU32(ImGuiCol.Text), time);
            }
        }

        /// <summary>The picked spell's name and what it does, in the box under the list (its gold rim on top in Retail).</summary>
        private void RenderDescription(Effect effect, float height, bool retail) {
            var min = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var drawList = ImGui.GetWindowDrawList();

            if (!retail || !ItemIcons.DrawArtBox(drawList, "effect-description", min, min + new Vector2(width, height))) {
                drawList.AddRectFilled(min, min + new Vector2(width, height), ImGui.GetColorU32(ImGuiCol.FrameBg));
            }

            var rim = retail ? Widgets.Scaled(8) : Widgets.Scaled(4);
            ImGui.SetCursorScreenPos(min + new Vector2(Widgets.Scaled(6), rim));

            if (Widgets.BeginChild($"{Key}-description", new Vector2(width - Widgets.Scaled(8), height - rim - Widgets.Scaled(2)),
                false)) {
                if (effect == null) {
                    Widgets.TextDisabled("Pick a spell to see what it does.");
                }
                else {
                    Widgets.TextColored(Colors.Detail, effect.Info.Name);
                    ImGui.PushTextWrapPos(0);
                    Widgets.Text(effect.Info.Description ?? "");
                    ImGui.PopTextWrapPos();
                }
            }

            Widgets.EndChild();
        }

        public override void Dispose() {
            interceptor.Dispose();
            base.Dispose();
        }
    }
}
