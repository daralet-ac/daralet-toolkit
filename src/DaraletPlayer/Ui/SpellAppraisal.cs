using System;
using System.Collections.Generic;
using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A spell's appraisal in a window of its own (spell-appraisal, hidden to start), opened by Appraise in the Spellbook
    /// window's right-click menu: the icon, name, school and level, the description, then the lines the game's examination
    /// panel writes for a spell, in its words and order (acclient's spell text, 0x4B6A60 and after: "School: ", "Mana: %d"
    /// with " + %d per target", "Duration: %u sec." under a minute and "%u min." from one, "Range: %.1f yds."), and the
    /// components. Everything is the portal dat's spell table (Spells.Info) and the character's skills: the game doesn't
    /// ask the server about a spell either.
    /// </summary>
    internal sealed class SpellAppraisal : IDisposable {
        /// <summary>The game's longest range (0x7AFF58, 75 meters) and meters to a yard (0x7AFE88).</summary>
        private const float MaxRange = 75;
        private const double MetersPerYard = 0.9144;

        /// <summary>Each school's skill, as the client's table (0x597984) has them, for the range.</summary>
        private static readonly Dictionary<string, int> SchoolSkills = new Dictionary<string, int> {
            ["WarMagic"] = 34, ["LifeMagic"] = 33, ["ItemEnchantment"] = 32, ["CreatureEnchantment"] = 31,
            ["VoidMagic"] = 43,
        };

        private readonly ItemIcons icons;
        private readonly FloatingWindow window;
        private int spellId;

        public SpellAppraisal(Settings settings, ItemIcons icons) {
            this.icons = icons;
            window = new FloatingWindow("spell-appraisal", "Spell Appraisal", settings, new Vector2(520, 160), Render,
                visibleByDefault: false);
        }

        public FloatingWindow Window => window;

        /// <summary>Shows this spell's appraisal, the window brought up in front.</summary>
        public void Show(int id) {
            spellId = id;
            window.Visible = true;
            window.BringToFront();
        }

        private void Render() {
            var spell = Spells.Info(spellId);

            if (spell == null) {
                Widgets.TextDisabled("Appraise a spell from the Spellbook window's right-click menu.");
                return;
            }

            var width = Widgets.Scaled(300);
            var iconSize = Widgets.Scaled(32);
            var top = ImGui.GetCursorScreenPos();
            icons.DrawAt(ImGui.GetWindowDrawList(), Spells.Icon(spell.Id), top, iconSize);
            ImGui.Dummy(new Vector2(iconSize, iconSize));
            ImGui.SameLine();
            ImGui.BeginGroup();
            Widgets.Text(spell.Name);
            Widgets.TextColored(Colors.Detail,
                $"{SpellbookPanel.SchoolName(spell.School)}, level {SpellLevels.Roman(spell.Level)}");
            ImGui.EndGroup();

            ImGui.Separator();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);

            if (!string.IsNullOrEmpty(spell.Description)) {
                Widgets.TextWrapped(spell.Description);
                ImGui.Spacing();
            }

            foreach (var line in Lines(spell)) {
                Widgets.TextWrapped(line);
            }

            ImGui.PopTextWrapPos();
            RenderComponents(spell);

            ImGui.Spacing();

            if (ImGui.Button("Cast##spell-appraisal")) {
                Spells.Cast(spell.Id);
            }

            Widgets.Tip("Casts it, in spellcasting mode, on what it's meant for: yourself, your selection or nothing, as the "
                + "hotbars do.");
        }

        /// <summary>The game's lines for a spell, in its order: school, mana, then duration and range when set.</summary>
        private static IEnumerable<string> Lines(SpellInfo spell) {
            yield return "School: " + SpellbookPanel.SchoolName(spell.School);
            yield return $"Mana: {spell.Mana}" + (spell.ManaMod > 0 ? $" + {spell.ManaMod} per target" : "");

            // -1 is for ever, and 0 none: the game writes neither
            if (spell.Duration > 0) {
                yield return spell.Duration < 60 ? $"Duration: {(uint)spell.Duration} sec."
                    : $"Duration: {(uint)(spell.Duration / 60)} min.";
            }

            var range = Range(spell);

            if (range > 0) {
                yield return $"Range: {range / MetersPerYard:0.0} yds.";
            }
        }

        /// <summary>
        /// The spell's range in meters, as the client works it out (0x4AD7D0): its range mod times your skill in its school
        /// (your best magic skill for a spell of none), plus its constant, held to 75 meters.
        /// </summary>
        private static double Range(SpellInfo spell) {
            var character = CoreManager.Current.CharacterFilter;
            int skill;

            if (spell.School != null && SchoolSkills.TryGetValue(spell.School, out var school)) {
                skill = Skill(character, school);
            }
            else {
                skill = 0;

                foreach (var each in SchoolSkills.Values) {
                    skill = Math.Max(skill, Skill(character, each));
                }
            }

            return Math.Min(MaxRange, spell.RangeMod * skill + spell.RangeConstant);
        }

        private static int Skill(CharacterFilter character, int skill) {
            try {
                return character.EffectiveSkill[(CharFilterSkillType)skill];
            }
            catch {
                // not logged in yet, or a skill Decal doesn't know
                return 0;
            }
        }

        /// <summary>The components the spell takes, each with its icon, by the portal dat's component table.</summary>
        private void RenderComponents(SpellInfo spell) {
            if (spell.Components.Count == 0) {
                return;
            }

            Dictionary<uint, ACE.DatLoader.Entity.SpellComponentBase> table = null;

            try {
                table = UBService.PortalDat?.SpellComponentsTable?.SpellComponents;
            }
            catch {
                // UtilityBelt without its portal dat: no components shown
            }

            if (table == null) {
                return;
            }

            ImGui.Spacing();
            Widgets.Text("Components:");
            var size = ImGui.GetTextLineHeight();

            foreach (var id in spell.Components) {
                if (!table.TryGetValue(id, out var component)) {
                    continue;
                }

                var icon = component.Icon < 0x01000000 ? component.Icon | 0x06000000 : component.Icon;
                icons.DrawAt(ImGui.GetWindowDrawList(), new IconLayers(0, icon, 0), ImGui.GetCursorScreenPos(), size);
                ImGui.Dummy(new Vector2(size, size));
                ImGui.SameLine();
                Widgets.Text(component.Name);
            }
        }

        public void Dispose() {
            window.Dispose();
        }
    }
}
