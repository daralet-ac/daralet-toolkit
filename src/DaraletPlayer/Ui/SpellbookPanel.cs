using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Spellbook window (spellbook): the spells you know and the components you carry, in place of the game's Spell
    /// Management panel (panel 0xD in the panel frame, layout 0x21000034: its tabs 0x100002A9 and 0x100002AA, the frame's
    /// tab template; its pages the spellbook 0x100002AC, layout 0x21000032, a list over a filter box of school and level
    /// checkboxes on 06002722, and the components 0x100002AD, layout 0x21000033). GamePanelInterceptor closes the game's
    /// the moment it opens and shows this instead, while Replace is on; it sizes by the player and can be one of the shared
    /// window's (spellbook). Spells are Decal's CharacterFilter.SpellBook, what each is the portal dat's spell table
    /// (Spells.Info: name, school, level by daralet-ACE's power rule, SpellLevels, mana, duration, components); a
    /// double-click casts (Spells.Cast, as the hotbars do), and a spell drags onto the Spell bar or a hotbar
    /// (SpellPayload) or goes on a spell bar tab from its right-click menu (ClientSpellBar.Add). Retail draws the
    /// frame's tabs, the picked row's bar and the filter box's stone; Modern ImGui's.
    /// </summary>
    internal sealed partial class SpellbookPanel : GamePanel {
        /// <summary>The game's Spell Management panel, as the toolbar's star button and the panel frame number it.</summary>
        public const uint GamePanelId = ToolbarOptions.SpellbookPanel;

        private const float RowHeight = 22;
        private const string MenuId = "spellbook-spell-menu";

        /// <summary>Daralet's skill for each school of magic, for the school's name as Daralet has it (Portal Magic...).</summary>
        private static readonly Dictionary<string, uint> SchoolSkills = new Dictionary<string, uint> {
            ["WarMagic"] = 34, ["LifeMagic"] = 33, ["ItemEnchantment"] = 32, ["CreatureEnchantment"] = 31,
            ["VoidMagic"] = 43,
        };

        private readonly ItemIcons icons;
        private readonly GamePanelInterceptor interceptor;
        private readonly SpellAppraisal appraisal;

        private int picked;
        private int menuSpell;
        private bool openMenu;

        /// <summary>
        /// How tall the filters and the picked spell under the list were last frame, which the list leaves room for: the
        /// filters wrap to more rows in a narrow window, and the text grows with the Text size, so an estimate fell short
        /// and the window scrolled to show Cast.
        /// </summary>
        private float bottomHeight;

        public SpellbookPanel(Settings settings, ItemIcons icons)
            : base("spellbook", "Spellbook", settings, new uint[0], new Vector2(480, 120), hideWhen: () => false,
                defaultSize: new Vector2(320, 560)) {
            this.icons = icons;
            interceptor = new GamePanelInterceptor("Spellbook", GamePanelId, () => Options.Replace, Window);
            appraisal = new SpellAppraisal(settings, icons);
        }

        private SpellbookOptions Data => settings.Data.Spellbook ?? (settings.Data.Spellbook = new SpellbookOptions());

        /// <summary>The Spell Appraisal window its menu's Appraise opens (it shares the appraisals' window).</summary>
        public SpellAppraisal Appraisal => appraisal;

        protected override string ReplaceTip => "Opens the plugin's Spellbook window instead of the game's Spell Management "
            + "panel (spellbook and components), from the toolbar's button, the game's key or anywhere.";

        protected override string WindowTip => "Shows the spells you know, to search, filter, cast and put on your spell "
            + "bar or hotbars, and the spell components you carry.";

        protected override void RenderContents() {
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("Not in the world yet.");
                return;
            }

            var data = Data;
            var retail = Retail;
            var clicked = GamePanelArt.Tabs("spellbook", new[] {
                ("Spellbook", SpellbookOptions.SpellsTab, "The spells you know."),
                ("Components", SpellbookOptions.ComponentsTab, "The spell components you carry, and what the picked "
                    + "spell takes."),
            }, data.Tab, retail, 150);

            if (clicked != null) {
                data.Tab = clicked;
                settings.Changed();
            }

            if (data.Tab == SpellbookOptions.ComponentsTab) {
                RenderComponents(data, retail);
            }
            else {
                RenderSpells(data, retail);
            }

            RenderMenu();
        }

        // ---- the spellbook ----

        private void RenderSpells(SpellbookOptions data, bool retail) {
            var search = data.Search ?? "";
            var byLevelWidth = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize("By level").X;
            ImGui.SetNextItemWidth(Math.Max(Widgets.Scaled(60), ImGui.GetContentRegionAvail().X - byLevelWidth
                - ImGui.GetStyle().ItemSpacing.X));

            if (ImGui.InputTextWithHint("##spellbook-search", "Search spells", ref search, 64)) {
                data.Search = search;
                settings.Changed();
            }

            ImGui.SameLine();

            if (ImGui.Checkbox("By level##spellbook-by-level", ref data.ByLevel)) {
                settings.Changed();
            }

            Widgets.Tip("Lists your spells by level, then by name. Off, they're by name alone, as the game's spellbook has "
                + "them.");

            var spells = data.Order(KnownSpells(), s => s.Name, s => s.Level);
            var shown = spells.Where(s => data.Shows(s.Name, s.School, s.Level)).ToList();

            if (picked == 0 || !spells.Any(s => s.Id == picked)) {
                picked = shown.FirstOrDefault()?.Id ?? 0;
            }

            var footer = bottomHeight > 0 ? bottomHeight : FooterHeight() + FiltersHeight(spells);

            if (Widgets.BeginChild("spellbook-list", new Vector2(0, -footer), false)) {
                RenderSpellRows(shown, retail);
            }

            Widgets.EndChild();
            var start = ImGui.GetCursorPosY();
            RenderFilters(data, spells, retail);
            GamePanelArt.Separator(retail);
            RenderPicked(Spells.Info(picked), retail);
            // from just under the list to just under Cast, with the spacing between the list and the filters
            bottomHeight = ImGui.GetCursorPosY() - start;
        }

        /// <summary>The spells you know, as the spell table has them (SpellbookOptions.Order puts them in order).</summary>
        private static List<SpellInfo> KnownSpells() {
            var spells = new List<SpellInfo>();

            foreach (var id in CoreManager.Current.CharacterFilter.SpellBook) {
                var info = Spells.Info(id);

                if (info != null) {
                    spells.Add(info);
                }
            }

            return spells;
        }

        private void RenderSpellRows(List<SpellInfo> spells, bool retail) {
            if (spells.Count == 0) {
                Widgets.TextDisabled("No spells match.");
                return;
            }

            var height = Math.Max(Widgets.Scaled(RowHeight), ImGui.GetTextLineHeight() + 2);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0));

            foreach (var spell in spells) {
                RenderSpellRow(spell, height, retail);
            }

            ImGui.PopStyleVar();
        }

        private void RenderSpellRow(SpellInfo spell, float height, bool retail) {
            var min = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var max = min + new Vector2(width, height);

            if (ImGui.InvisibleButton($"##spell-{spell.Id}", new Vector2(width, height))) {
                picked = spell.Id;
            }

            if (ImGui.IsItemHovered() && Widgets.DoubleClicked()) {
                Spells.Cast(spell.Id);
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
                picked = spell.Id;
                menuSpell = spell.Id;
                openMenu = true;
            }

            SpellPayload.Source(spell.Id, icons, height);
            Widgets.Tip($"{spell.Name}: double-click to cast, drag to your spell bar or a hotbar, right-click for more.");

            var drawList = ImGui.GetWindowDrawList();

            if (picked == spell.Id) {
                if (!retail || !ItemIcons.DrawArtBox(drawList, "stat-row-selected", min, max)) {
                    drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.Header));
                }
            }
            else if (ImGui.IsItemHovered()) {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.HeaderHovered, 0.5f));
            }

            icons.DrawAt(drawList, Spells.Icon(spell.Id), min, height);
            var textY = (float)Math.Round(min.Y + (height - ImGui.GetTextLineHeight()) / 2);
            drawList.AddText(new Vector2(min.X + height + Widgets.Scaled(5), textY), ImGui.GetColorU32(ImGuiCol.Text),
                spell.Name);

            var level = SpellLevels.Roman(spell.Level);
            var levelSize = ImGui.CalcTextSize(level).X;
            drawList.AddText(new Vector2(max.X - levelSize - Widgets.Scaled(5), textY),
                ImGui.GetColorU32(ImGuiCol.TextDisabled), level);
        }

        // ---- filters ----

        private static float FiltersHeight(List<SpellInfo> spells) {
            // a row of schools and a row of levels, on the filter box
            return ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetStyle().WindowPadding.Y + Widgets.Scaled(7);
        }

        /// <summary>The school and level checkboxes, on the game's stone box (06002722) in Retail.</summary>
        private void RenderFilters(SpellbookOptions data, List<SpellInfo> spells, bool retail) {
            GamePanelArt.Separator(retail);
            var min = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var height = ImGui.GetFrameHeightWithSpacing() * 2;

            if (retail) {
                ItemIcons.DrawArtBox(ImGui.GetWindowDrawList(), "spellbook-filters", min, min + new Vector2(width, height));
            }

            var changed = false;
            var first = true;

            foreach (var school in spells.Select(s => s.School).Where(s => s != null).Distinct().OrderBy(SchoolName)) {
                var on = !data.HiddenSchools.Contains(school);
                var label = SchoolName(school);

                if (Form.FlowCheck(ref first, $"{label}##spellbook-school-{school}", ref on,
                    $"Shows your {label} spells.")) {
                    Toggle(data.HiddenSchools, school, on);
                    changed = true;
                }
            }

            first = true;

            for (var level = 1; level <= SpellLevels.MaxLevel; level++) {
                var on = !data.HiddenLevels.Contains(level);

                if (Form.FlowCheck(ref first, $"{SpellLevels.Roman(level)}##spellbook-level-{level}", ref on,
                    $"Shows your level {SpellLevels.Roman(level)} spells.")) {
                    Toggle(data.HiddenLevels, level, on);
                    changed = true;
                }
            }

            if (changed) {
                settings.Changed();
            }
        }

        private static void Toggle<T>(List<T> hidden, T value, bool shown) {
            hidden.Remove(value);

            if (!shown) {
                hidden.Add(value);
            }
        }

        /// <summary>
        /// A school as Daralet names it: its skill's name in the client's skill table (Item Enchantment is Portal Magic),
        /// or the school's own words.
        /// </summary>
        public static string SchoolName(string school) {
            if (school == null) {
                return "";
            }

            try {
                var table = UBService.PortalDat?.SkillTable?.SkillBaseHash;

                if (table != null && SchoolSkills.TryGetValue(school, out var skill) && table.TryGetValue(skill, out var info)
                    && !string.IsNullOrEmpty(info.Name)) {
                    return info.Name;
                }
            }
            catch {
                // UtilityBelt without its portal dat: the school's own words below
            }

            return string.Concat(school.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
        }

        // ---- the picked spell ----

        private static float FooterHeight() {
            return ImGui.GetTextLineHeightWithSpacing() * 5 + ImGui.GetFrameHeightWithSpacing() + Widgets.Scaled(7);
        }

        /// <summary>The picked spell: its name, school, level, mana and duration, what it does, and Cast.</summary>
        private void RenderPicked(SpellInfo spell, bool retail) {
            if (spell == null) {
                Widgets.TextDisabled("Pick a spell.");
                return;
            }

            var width = ImGui.GetContentRegionAvail().X;
            GamePanelArt.Centred(spell.Name, width, Colors.Text);
            var duration = spell.Duration > 0 ? " · " + Widgets.Duration(spell.Duration) : "";
            GamePanelArt.Centred($"{SchoolName(spell.School)}, level {SpellLevels.Roman(spell.Level)} · {spell.Mana} mana"
                + duration, width, Colors.Detail);

            // what it does, cut to three lines here (the whole on hovering)
            var description = spell.Description ?? "";
            var lines = ImGui.GetTextLineHeight() * 3;
            var min = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRect(min, min + new Vector2(width, lines), true);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
            Widgets.TextDisabled(description);
            ImGui.PopTextWrapPos();
            drawList.PopClipRect();
            ImGui.SetCursorScreenPos(min);
            ImGui.Dummy(new Vector2(width, lines));
            Widgets.Tip(description);

            if (ImGui.Button("Cast##spellbook")) {
                Spells.Cast(spell.Id);
            }

            Widgets.Tip("Casts it, in spellcasting mode, on what it's meant for: yourself, your selection or nothing, as the "
                + "hotbars do.");
            ImGui.SameLine();

            if (ImGui.Button("Add to spell bar##spellbook")) {
                menuSpell = spell.Id;
                openMenu = true;
            }

            Widgets.Tip("Puts it on one of your spell bar's tabs, as dragging it onto the game's spell bar does.");
        }

        private void RenderMenu() {
            if (openMenu) {
                openMenu = false;
                ImGui.OpenPopup(MenuId);
            }

            if (!ImGui.BeginPopup(MenuId)) {
                return;
            }

            Widgets.TextDisabled(Spells.Name(menuSpell));
            ImGui.Separator();

            if (ImGui.MenuItem("Cast")) {
                Spells.Cast(menuSpell);
            }

            if (ImGui.MenuItem("Appraise")) {
                appraisal.Show(menuSpell);
            }

            if (ImGui.BeginMenu("Add to spell bar tab")) {
                for (var tab = 0; tab < ClientSpellBar.Tabs; tab++) {
                    if (ImGui.MenuItem(SpellBarOptions.TabName(tab))) {
                        ClientSpellBar.Add(tab, menuSpell, int.MaxValue);
                    }
                }

                ImGui.EndMenu();
            }

            ImGui.EndPopup();
        }

        public override void Dispose() {
            appraisal.Dispose();
            interceptor.Dispose();
            base.Dispose();
        }
    }
}
