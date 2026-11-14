using System;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's examination panel for a creature or a player (the Examination floaty, 0x100005F7, layout 0x2100006B, its
    /// creature page 0x10000140: name and kind in a box at the top, the level past a divider on the right, then the vitals
    /// and attributes, and the description under a line) in a window of its own. With Replace on, an appraisal the game
    /// starts of a creature, NPC or player is taken from it where it begins (ClientExamine), so the game's panel never
    /// opens, and asked for with Decal's RequestId instead, which the client never shows; the server's answer is read
    /// from its own message (CreatureAppraisal), since Decal doesn't pass on a creature's attributes. Items are left to the
    /// game, or the plugin's Appraisal window. Any later appraisal of the same one (another plugin's, Appraise again)
    /// brings it up to date.
    /// </summary>
    internal sealed class CreatureAppraisalPanel : GamePanel {
        /// <summary>The level box is this wide, past its divider, as the game's (56 + 7).</summary>
        private const float LevelBoxWidth = 64;

        /// <summary>The top box is at least this tall, as the game's (60), at 100% text.</summary>
        private const float HeaderLeastHeight = 60;

        /// <summary>The name, bold, a little larger than the text (GDI+'s em is smaller than ImGui's line).</summary>
        private const float NameScale = 1f;

        /// <summary>The heritages' names by number, from the portal dat once it's read (HeritageName).</summary>
        private static System.Collections.Generic.Dictionary<int, string> heritages;

        /// <summary>How tall the name and the lines under it were last frame, to centre them down the box.</summary>
        private float headerLinesHeight;

        // packed by hand: ImGui.GetColorU32 crashes before ImGui has a context
        private static readonly uint LevelColor = PackedColor.Pack(235, 222, 150);
        private static readonly uint NameColor = PackedColor.Pack(250, 248, 240);

        private readonly ItemIcons icons;

        /// <summary>The creature's model, the game's examination panel cut down to it, under the body (ModelShown).</summary>
        private readonly CreatureModelView model = new CreatureModelView();

        /// <summary>The object the game last asked to appraise and the plugin took over; 0 for none.</summary>
        private int wanted;

        private CreatureAppraisal shown;
        private DateTime shownAt;

        public CreatureAppraisalPanel(Settings settings, ItemIcons icons)
            : base("creature-appraisal", "Creature Appraisal", settings, new uint[0], new Vector2(640, 150),
                hideWhen: () => false, defaultSize: new Vector2(310, 420)) {
            this.icons = icons;
            ClientExamine.Start();
            ClientExamine.TakeOver = TakeOver;
            CoreManager.Current.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
        }

        protected override string ReplaceTip => "Shows creature, NPC and player appraisals in the plugin's Creature "
            + "Appraisal window instead of the game's examination panel, from the appraise key, the magnifier or anywhere "
            + "the game appraises. Items still go to the game's panel.";

        protected override string WindowTip => "A creature's or player's appraisal: level, health, stamina, mana and "
            + "attributes (green raised by a spell, red lowered), ratings, a player's armor, allegiance and character, and "
            + "the description.";

        /// <summary>Takes a creature's, NPC's or player's appraisal from the game while Replace is on.</summary>
        private bool TakeOver(int id) {
            if (!Options.Replace || CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                return false;
            }

            var target = CoreManager.Current.WorldFilter[id];

            if (target == null || !IsCreature(target)) {
                // the game shows this one itself (an item): its panel goes back as it was first
                model.Restore(hide: false);
                return false;
            }

            wanted = id;
            Window.Visible = true;
            Window.BringToFront();

            // with the model, the game does its own appraisal, whose panel is cut down to the model (CreatureModelView)
            if (settings.Data.CreatureAppraisalModel) {
                model.Expect(id);
                PluginCore.LogFile($"[appraisal] the game appraises 0x{id:X8} ({target.Name}) for its model");
                return false;
            }

            model.Restore(hide: true);
            CoreManager.Current.Actions.RequestId(id);
            PluginCore.LogFile($"[appraisal] took the game's appraisal of 0x{id:X8} ({target.Name})");
            return true;
        }

        private static bool IsCreature(WorldObject target) {
            switch (target.ObjectClass) {
                case ObjectClass.Monster:
                case ObjectClass.Player:
                case ObjectClass.Npc:
                case ObjectClass.Vendor:
                    return true;
                default:
                    return false;
            }
        }

        private void EchoFilter_ServerDispatch(object sender, NetworkMessageEventArgs e) {
            try {
                if (e.Message.Type != CooldownMessages.GameEvent) {
                    return;
                }

                var appraisal = CreatureAppraisal.Read(e.Message.RawData);

                if (appraisal == null) {
                    return;
                }

                var id = unchecked((int)appraisal.Guid);

                // the one asked for, or a fresh look at the one shown (another plugin's appraisal of it included)
                if (id == wanted || (shown != null && shown.Guid == appraisal.Guid)) {
                    shown = appraisal;
                    shownAt = DateTime.Now;
                    wanted = id == wanted ? 0 : wanted;
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[appraisal] " + ex);
            }
        }

        protected override void RenderContents() {
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("Not in the world yet.");
                return;
            }

            var retail = Retail;
            var min = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail();

            // the stone goes under everything but the model's hole, which is only known once the header's drawn: drawn
            // last, on a channel under the rest
            var drawList = ImGui.GetWindowDrawList();
            (Vector2 Min, Vector2 Max)? hole = null;
            drawList.ChannelsSplit(2);
            drawList.ChannelsSetCurrent(1);

            try {
                if (shown == null) {
                    Widgets.TextDisabled(wanted != 0 ? "Appraising..." : Options.Replace
                        ? "Appraise a creature or a player to see it here."
                        : "Tick Replace to see the game's creature and player appraisals here.");
                    return;
                }

                var known = CoreManager.Current.WorldFilter[unchecked((int)shown.Guid)];
                RenderHeader(known, retail);
                GamePanelArt.Separator(retail);
                hole = PlaceModel();
                float? barY = null;

                if (Widgets.BeginChild("creature-appraisal-body", Vector2.Zero, false)) {
                    barY = RenderBody(retail, overModel: hole.HasValue);
                }

                Widgets.EndChild();

                if (hole.HasValue) {
                    model.PlaceBar(barY);
                }
            }
            finally {
                drawList.ChannelsSetCurrent(0);

                if (retail) {
                    StoneAround(drawList, min, min + avail, hole);
                }

                drawList.ChannelsMerge();
            }
        }

        /// <summary>
        /// The creature's model under the body (the stats over it, as the game has them), the game's panel placed there,
        /// with the window see-through over it once the game's shown it; the hole, or null with no model.
        /// </summary>
        private (Vector2 Min, Vector2 Max)? PlaceModel() {
            var id = model.For;

            if (!settings.Data.CreatureAppraisalModel || !Options.Replace || id == 0
                || (unchecked((int)shown.Guid) != id && wanted != id)) {
                return null;
            }

            var min = ImGui.GetCursorScreenPos();
            var size = ImGui.GetContentRegionAvail();

            if (size.X < 16 || size.Y < 16) {
                return null;
            }

            model.Show(min, size);

            if (!model.Ready) {
                return null;
            }

            Window.SetHole(min, min + size);
            return (min, min + size);
        }

        /// <summary>The game's stone over the window's contents, round the model's hole when there is one.</summary>
        private static void StoneAround(ImDrawListPtr drawList, Vector2 min, Vector2 max, (Vector2 Min, Vector2 Max)? hole) {
            if (!hole.HasValue) {
                GamePanelArt.Stone(drawList, min, max);
                return;
            }

            var a = Vector2.Max(min, hole.Value.Min);
            var b = Vector2.Min(max, hole.Value.Max);

            // above, below, then left and right of it
            Stone(drawList, min, new Vector2(max.X, a.Y));
            Stone(drawList, new Vector2(min.X, b.Y), max);
            Stone(drawList, new Vector2(min.X, a.Y), new Vector2(a.X, b.Y));
            Stone(drawList, new Vector2(b.X, a.Y), new Vector2(max.X, b.Y));
        }

        private static void Stone(ImDrawListPtr drawList, Vector2 min, Vector2 max) {
            if (max.X > min.X && max.Y > min.Y) {
                GamePanelArt.Stone(drawList, min, max);
            }
        }

        /// <summary>
        /// The name in bold, then the game's lines under it ("Male Gharu'ndim", the template, player killer or not), all
        /// centred across and down the box, and the level past a divider on the right, centred in its own, as the game's
        /// top box. The box is as tall as the taller of the two, at least the game's 60; the lines are centred down it by
        /// how tall they were last frame.
        /// </summary>
        private void RenderHeader(WorldObject known, bool retail) {
            var avail = ImGui.GetContentRegionAvail().X;
            var boxWidth = Widgets.Scaled(LevelBoxWidth);
            var leftWidth = Math.Max(Widgets.Scaled(120), avail - boxWidth - ImGui.GetStyle().ItemSpacing.X);
            var name = CreatureSheet.NameOf(shown, known?.Name);
            var height = Math.Max(headerLinesHeight, Widgets.Scaled(HeaderLeastHeight));

            ImGui.BeginGroup();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + Math.Max(0, (float)Math.Floor((height - headerLinesHeight) / 2)));
            ImGui.BeginGroup();
            GamePanelArt.BoldCentred(icons, name, leftWidth, NameScale, NameColor);

            if (ImGui.IsItemClicked() && known != null) {
                CoreManager.Current.Actions.SelectItem(known.Id);
            }

            Widgets.Tip("Click to select it.");

            foreach (var line in CreatureSheet.HeaderLines(shown, HeritageName)) {
                GamePanelArt.Centred(line, leftWidth, Colors.Detail);
            }

            if (!shown.Success) {
                GamePanelArt.Centred("Your appraisal failed: only its health shows.", leftWidth, Colors.Warning);
            }

            RenderAgain(leftWidth, retail);
            ImGui.EndGroup();
            headerLinesHeight = ImGui.GetItemRectSize().Y;
            ImGui.EndGroup();

            ImGui.SameLine();
            RenderLevelBox(CreatureSheet.LevelOf(shown), boxWidth, Math.Max(height, headerLinesHeight), retail);
        }

        /// <summary>
        /// A heritage's name by number, as the server has it: the portal dat's character creation table (CharGen), read
        /// once it's there; null for one it hasn't (CreatureSheet falls back to retail's).
        /// </summary>
        private static string HeritageName(int id) {
            if (heritages == null) {
                try {
                    var groups = UtilityBelt.Service.UBService.PortalDat?.CharGen?.HeritageGroups;

                    if (groups != null) {
                        heritages = new System.Collections.Generic.Dictionary<int, string>();

                        foreach (var group in groups) {
                            heritages[(int)group.Key] = group.Value?.Name;
                        }
                    }
                }
                catch (Exception ex) {
                    heritages = new System.Collections.Generic.Dictionary<int, string>();
                    PluginCore.LogFile("[appraisal] couldn't read the heritages' names: " + ex);
                }
            }

            return heritages != null && heritages.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name.Trim() : null;
        }

        /// <summary>Appraise again, and how old what's shown is.</summary>
        private void RenderAgain(float width, bool retail) {
            var age = DateTime.Now - shownAt;
            var size = new Vector2(Widgets.Scaled(90), Widgets.Scaled(18));
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (width - size.X) / 2));

            if (GamePanelArt.Button("creature-appraise-again", "Appraise again", size, "cast", retail)) {
                wanted = unchecked((int)shown.Guid);
                CoreManager.Current.Actions.RequestId(wanted);
            }

            Widgets.Tip($"Appraises it again, for its health and spells now. What's shown is from {Widgets.Duration(age.TotalSeconds)} "
                + "ago.");
        }

        /// <summary>The gold divider, "Level" and the level, big, as the game's box on the right.</summary>
        private void RenderLevelBox(int? level, float width, float height, bool retail) {
            var min = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var divider = Widgets.Scaled(5);

            if (!retail || !ItemIcons.DrawArtBox(drawList, "panel-divider", min,
                new Vector2(min.X + divider, min.Y + height))) {
                drawList.AddLine(min, new Vector2(min.X, min.Y + height), ImGui.GetColorU32(ImGuiCol.Separator));
            }

            var inner = new Vector2(min.X + divider, min.Y);
            var innerWidth = width - divider;
            var line = ImGui.GetTextLineHeight();
            var label = ImGui.CalcTextSize("Level");

            var text = level?.ToString() ?? "?";
            var room = Math.Max(line, height - line);
            var pixels = (int)Math.Max(10, Math.Min(room * 0.8f, Widgets.Scaled(28)));
            var size = icons.LabelSize(text, pixels, outlined: false)
                ?? ImGui.CalcTextSize(text) * (pixels / ImGui.GetFontSize());

            if (size.X > innerWidth - 4 && size.X > 0) {
                pixels = (int)Math.Max(10, Math.Floor(pixels * (innerWidth - 4) / size.X));
                size = icons.LabelSize(text, pixels, outlined: false) ?? size;
            }

            // "Level" and the number together, centred down the box as well as across it
            var top = (float)Math.Round(inner.Y + Math.Max(0, (height - line - size.Y) / 2));
            drawList.AddText(new Vector2((float)Math.Round(inner.X + (innerWidth - label.X) / 2), top),
                ImGui.GetColorU32(ImGuiCol.Text), "Level");

            var at = new Vector2((float)Math.Round(inner.X + Math.Max(0, (innerWidth - size.X) / 2)), top + line);
            drawList.PushClipRect(inner, inner + new Vector2(innerWidth, height), true);

            if (!icons.DrawLabel(drawList, text, at, pixels, LevelColor, outlined: false)) {
                drawList.AddText(ImGui.GetFont(), pixels, at, LevelColor, text);
            }

            drawList.PopClipRect();
            ImGui.Dummy(new Vector2(width, Math.Max(height, line * 2)));
        }

        /// <summary>
        /// The vitals, attributes, sections and description. Over the model the parts are set apart by gaps, as the
        /// game's own list is, since anything ImGui draws is in front of the game's model, and the one bar, before the
        /// description, is the game's own (CreatureModelView.PlaceBar), behind the model: its height on screen is
        /// returned, or null for none.
        /// </summary>
        private float? RenderBody(bool retail, bool overModel) {
            var width = ImGui.GetContentRegionAvail().X;

            // the attributes first, then the vitals, as the game's panel lists them (a failed appraisal has health alone)
            var attributes = CreatureSheet.Attributes(shown.Creature);

            if (attributes.Count > 0) {
                Rows(attributes, width);
                Divider(retail, overModel, width);
            }

            Rows(CreatureSheet.Vitals(shown.Creature), width);

            foreach (var section in CreatureSheet.Sections(shown)) {
                Divider(retail, overModel, width);
                Widgets.TextColored(ItemTooltip.Heading, section.Title);
                Rows(section.Rows, width);
            }

            var description = CreatureSheet.Description(shown);
            float? barY = null;

            if (description.Length > 0) {
                barY = Divider(retail, overModel, width);
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
                Widgets.TextColored(Colors.Detail, description);
                ImGui.PopTextWrapPos();
            }

            return barY;
        }

        /// <summary>
        /// The bar between parts: the game's separator, or over the model a gap as tall, where the game's own bar may go
        /// (its top on screen returned, a 5-pixel bar centred in the gap).
        /// </summary>
        private static float? Divider(bool retail, bool overModel, float width) {
            if (!overModel) {
                GamePanelArt.Separator(retail);
                return null;
            }

            var height = Widgets.Scaled(7);
            var top = ImGui.GetCursorScreenPos().Y + (float)Math.Floor((height - 5) / 2);
            ImGui.Dummy(new Vector2(width, height));
            return top;
        }

        private static void Rows(System.Collections.Generic.List<AppraisalRow> rows, float width) {
            foreach (var row in rows) {
                var color = row.Effect > 0 ? Colors.Good : row.Effect < 0 ? Colors.Danger : Colors.Text;
                GamePanelArt.LeftRight(row.Label, row.Value, width, color);

                if (row.Effect != 0) {
                    Widgets.Tip(row.Effect > 0 ? "Raised by a spell." : "Lowered by a spell.");
                }
                else if (row.Tip != null) {
                    Widgets.Tip(row.Tip);
                }
            }
        }

        private const string ModelTip = "Shows the creature's model behind its stats, as the game's panel does: the game "
            + "does its own appraisal, and its panel, cut down to the model, sits under the window. Off, the game's "
            + "appraisal is taken from it and its panel never opens.";

        protected override void RenderOwnMenu() {
            ImGui.Separator();

            if (ImGui.MenuItem("Model", null, settings.Data.CreatureAppraisalModel)) {
                settings.Data.CreatureAppraisalModel = !settings.Data.CreatureAppraisalModel;
                settings.Changed();
            }

            Widgets.Tip(ModelTip);
        }

        protected override void RenderOwnSettings() {
            var first = true;

            if (Form.FlowCheck(ref first, "Model##creature-appraisal", ref settings.Data.CreatureAppraisalModel, ModelTip)) {
                settings.Changed();
            }
        }

        public override void Dispose() {
            CoreManager.Current.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
            ClientExamine.Stop();
            model.Dispose();
            base.Dispose();
        }
    }
}
