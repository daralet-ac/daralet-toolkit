using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Attributes window (attributes): your attributes, vitals and skills, raised with your unassigned XP and trained
    /// with your skill credits, in place of the game's Character Detail panel (panel 0xB in the panel frame, layout
    /// 0x2100002E: Attributes, Skills and Titles tabs). That panel shares the frame with every other, so there's no element
    /// of its own to hide: while Replace is on, GamePanelInterceptor closes it the moment it opens and shows this window
    /// instead, from the toolbar's button, the game's key or anywhere; Titles is the plugin's own too
    /// (AttributesPanel.Titles.cs). Laid out as the game's pages are (layout 0x21000045): the character at the top of both
    /// (AttributesPanel.Header.cs), the list with icons, buffed values green and debuffed red, and the game's selection
    /// bar (AttributesPanel.Rows.cs), and the picked one's raise at the bottom (AttributesPanel.Footer.cs). The numbers are
    /// Decal's CharacterFilter (what the server told the client), the costs the client's own experience tables and the
    /// raises the client's own messages (ClientTraining), as the game's panel does. Retail draws the panel's art; Modern
    /// the same layout in ImGui's.
    /// </summary>
    internal sealed partial class AttributesPanel : GamePanel {
        /// <summary>The game's Character Detail panel, as the toolbar's arm button and the panel frame's layout number it.</summary>
        public const uint GamePanelId = ToolbarOptions.AttributesPanel;


        private readonly PlayerFeed feed;
        private readonly ItemIcons icons;

        /// <summary>Closes the game's panel when it opens and shows this window instead, while Replace is on.</summary>
        private readonly GamePanelInterceptor interceptor;

        /// <summary>The row picked on each tab, by tab: its raise shows at the bottom.</summary>
        private readonly Dictionary<string, string> picked = new Dictionary<string, string>();

        public AttributesPanel(Settings settings, ItemIcons icons, PlayerFeed feed)
            : base("attributes", "Attributes", settings, new uint[0], new Vector2(470, 120), hideWhen: () => false,
                defaultSize: new Vector2(320, 560)) {
            this.icons = icons;
            this.feed = feed;
            interceptor = new GamePanelInterceptor("Attributes", GamePanelId, () => Options.Replace, Window);
        }

        private AttributesOptions Data => settings.Data.Attributes ?? (settings.Data.Attributes = new AttributesOptions());

        protected override string ReplaceTip => "Opens the plugin's Attributes window instead of the game's Character "
            + "Detail panel (attributes, skills and titles), from the toolbar's button, the game's key or anywhere.";

        protected override string WindowTip => "Shows your attributes, vitals and skills as the game's Character Detail "
            + "panel does, with buttons to raise them with your unassigned XP and to train skills with your skill credits.";

        protected override void RenderContents() {
            var character = CoreManager.Current.CharacterFilter;

            if (character.LoginStatus != 3) {
                Widgets.TextDisabled("Not in the world yet.");
                return;
            }

            var data = Data;
            var retail = Retail;
            var skills = data.Tab == AttributesOptions.SkillsTab;

            RenderTabs(data, retail);

            // the game's Titles page is a page of its own, without the character at the top
            if (data.Tab == AttributesOptions.TitlesTab) {
                RenderTitles(retail);
                return;
            }

            RenderHeader(retail);
            GamePanelArt.Separator(retail);

            var rows = skills ? SkillRows(data) : AttributeRows();
            var row = Picked(data.Tab, rows);
            var footer = FooterHeight();

            // the list fills what the window has between the character and the raise, scrolling when it's short
            if (Widgets.BeginChild("attributes-list", new Vector2(0, -footer), false)) {
                RenderList(data.Tab, rows, row, retail);
            }

            Widgets.EndChild();
            GamePanelArt.Separator(retail);
            RenderFooter(row, retail);

            if (ClientTraining.Broken) {
                Widgets.TextColored(Colors.Danger, "Raising is off: the game's training couldn't be used (see log.txt).");
            }
        }

        /// <summary>The row picked on this tab, or its first that isn't a heading.</summary>
        private StatRow Picked(string tab, List<StatRow> rows) {
            picked.TryGetValue(tab ?? "", out var key);
            StatRow first = null;

            foreach (var row in rows) {
                if (row.IsHeader) {
                    continue;
                }

                if (row.Key == key) {
                    return row;
                }

                first = first ?? row;
            }

            return first;
        }

        private void Pick(string tab, StatRow row) {
            picked[tab ?? ""] = row.Key;
        }

        // ---- tabs ----

        private void RenderTabs(AttributesOptions data, bool retail) {
            var clicked = GamePanelArt.Tabs("attributes", new[] {
                ("Attributes", AttributesOptions.AttributesTab, "Shows your attributes and vitals."),
                ("Skills", AttributesOptions.SkillsTab, "Shows your skills."),
                    ("Titles", AttributesOptions.TitlesTab, "Shows your character titles, to pick the one other players "
                    + "see."),
            }, data.Tab, retail);

            if (clicked != null) {
                data.Tab = clicked;
                settings.Changed();
            }
        }

        // ---- settings ----

        protected override void RenderOwnMenu() {
            var data = Data;
            ImGui.Separator();

            if (ImGui.MenuItem("Untrained skills", null, data.ShowUntrained)) {
                data.ShowUntrained = !data.ShowUntrained;
                settings.Changed();
            }

            if (ImGui.MenuItem("Unusable skills", null, data.ShowUnusable)) {
                data.ShowUnusable = !data.ShowUnusable;
                settings.Changed();
            }
        }

        protected override void RenderOwnSettings() {
            var data = Data;
            var first = true;
            var changed = false;

            changed |= Form.FlowCheck(ref first, "Untrained skills##attributes", ref data.ShowUntrained,
                "Lists the untrained skills you can still use, with Train, under your trained ones.");
            changed |= Form.FlowCheck(ref first, "Unusable skills##attributes", ref data.ShowUnusable,
                "Lists the skills you can't use until they're trained, with Train, at the end.");

            if (changed) {
                settings.Changed();
            }
        }

        public override void Dispose() {
            interceptor.Dispose();
            titles.Dispose();
            base.Dispose();
        }
    }
}
