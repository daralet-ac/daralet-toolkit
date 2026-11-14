using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The list, as the game's (layout 0x21000045's rows, template 0x10000248, 282x20: a 20x20 icon, the name, the value at
    /// the right; picked, the gold-framed bar 06000F93): attributes then vitals, or skills under the game's coloured group
    /// bars (0x10000249-4C: 06000F90 specialized, 06000F86 trained, 06000F98 untrained, 06000F89 unusable). A value above
    /// its base is green, below it red. Icons are the portal dat's: attributes by DID mapper 0x25000006, vitals by
    /// 0x25000007, skills their skill table entry's.
    /// </summary>
    internal sealed partial class AttributesPanel {
        private const float RowHeight = 20;

        private static readonly CharFilterAttributeType[] AttributeTypes = {
            CharFilterAttributeType.Strength, CharFilterAttributeType.Endurance, CharFilterAttributeType.Coordination,
            CharFilterAttributeType.Quickness, CharFilterAttributeType.Focus, CharFilterAttributeType.Self,
        };

        private static readonly CharFilterVitalType[] VitalTypes = {
            CharFilterVitalType.Health, CharFilterVitalType.Stamina, CharFilterVitalType.Mana,
        };

        /// <summary>The attributes' icons, from the portal dat's DID mapper 0x25000006 (by STypeAttribute, 1 to 6).</summary>
        private static readonly Dictionary<CharFilterAttributeType, uint> AttributeIcons =
            new Dictionary<CharFilterAttributeType, uint> {
                [CharFilterAttributeType.Strength] = 0x060002C8,
                [CharFilterAttributeType.Endurance] = 0x060002C4,
                [CharFilterAttributeType.Quickness] = 0x060002C6,
                [CharFilterAttributeType.Coordination] = 0x060002C9,
                [CharFilterAttributeType.Focus] = 0x060002C5,
                [CharFilterAttributeType.Self] = 0x060002C7,
            };

        /// <summary>The vitals' icons, from the DID mapper 0x25000007 (a vital and its maximum share one).</summary>
        private static readonly Dictionary<CharFilterVitalType, uint> VitalIcons = new Dictionary<CharFilterVitalType, uint> {
            [CharFilterVitalType.Health] = 0x06004C3B,
            [CharFilterVitalType.Stamina] = 0x06004C3C,
            [CharFilterVitalType.Mana] = 0x06004C3D,
        };

        // packed by hand: ImGui.GetColorU32 crashes before ImGui has a context
        private static readonly uint HeaderText = PackedColor.Pack(235, 230, 210);

        private List<StatRow> AttributeRows() {
            var character = CoreManager.Current.CharacterFilter;
            var actions = CoreManager.Current.Actions;
            var rows = new List<StatRow>();

            foreach (var type in AttributeTypes) {
                var attribute = character.Attributes[type];
                var stype = (uint)type;
                rows.Add(new StatRow {
                    Key = $"a{(int)type}",
                    Name = attribute.Name,
                    Icon = new IconLayers(0, AttributeIcons[type], 0),
                    Value = character.EffectiveAttribute[type],
                    Base = attribute.Base,
                    Spent = (long)(uint)attribute.Exp,
                    Table = ClientTraining.Attributes,
                    Raise = xp => ClientTraining.RaiseAttribute(stype, xp),
                    Tip = $"Innate {attribute.Creation}, raised to {attribute.Base}.",
                });
            }

            foreach (var type in VitalTypes) {
                var vital = character.Vitals[type];
                // STypeAttribute2nd numbers the maximums 1, 3 and 5; Decal's vitals are 2, 4 and 6, as are its
                // VitalType's current values
                var stype = (uint)type - 1;
                // the client's own maximum, buffs and all (VitalType.MaximumHealth is one below CurrentHealth), as the
                // game's panel shows it: CharacterFilter.EffectiveVital gave the unbuffed base
                var max = actions.Vital[(VitalType)((int)type - 1)];
                var current = actions.Vital[(VitalType)(int)type];
                rows.Add(new StatRow {
                    Key = $"v{(int)type}",
                    Name = type.ToString(),
                    Icon = new IconLayers(0, VitalIcons[type], 0),
                    Value = max,
                    Base = vital.Base,
                    ValueText = $"{current}/{max}",
                    Spent = (long)(uint)vital.XP,
                    Table = ClientTraining.Vitals,
                    Raise = xp => ClientTraining.RaiseVital(stype, xp),
                    Tip = $"Base maximum {vital.Base}, from your attributes and what you've raised it.",
                });
            }

            return rows;
        }

        private List<StatRow> SkillRows(AttributesOptions data) {
            var character = CoreManager.Current.CharacterFilter;
            var groups = new Dictionary<TrainingType, List<StatRow>> {
                [TrainingType.Specialized] = new List<StatRow>(),
                [TrainingType.Trained] = new List<StatRow>(),
                [TrainingType.Untrained] = new List<StatRow>(),
                [TrainingType.Unusable] = new List<StatRow>(),
            };

            foreach (var type in SkillTypes()) {
                SkillInfoWrapper info;

                try {
                    info = character.Skills[type];
                }
                catch {
                    // a skill the character's description didn't have
                    continue;
                }

                if (info == null || !groups.TryGetValue(info.Training, out var group)) {
                    continue;
                }

                var stype = (uint)type;
                var raised = info.Training == TrainingType.Specialized || info.Training == TrainingType.Trained;
                var icon = SkillBase(type)?.IconId ?? 0;
                group.Add(new StatRow {
                    Key = $"s{(int)type}",
                    Name = SkillName(type, info),
                    Icon = icon == 0 ? null : new IconLayers(0, icon < 0x01000000 ? icon | 0x06000000 : icon, 0),
                    Value = character.EffectiveSkill[type],
                    Base = info.Base,
                    ValueText = info.Training == TrainingType.Unusable ? "-" : null,
                    Spent = (long)(uint)info.XP,
                    Table = info.Training == TrainingType.Specialized ? ClientTraining.SpecializedSkills
                        : info.Training == TrainingType.Trained ? ClientTraining.TrainedSkills : null,
                    Raise = raised ? xp => ClientTraining.RaiseSkill(stype, xp) : (Func<long, bool>)null,
                    Untrained = !raised,
                    Skill = type,
                    TrainCost = SkillBase(type)?.TrainedCost ?? 0,
                    Tip = SkillTip(type, info),
                });
            }

            var rows = new List<StatRow>();
            AddGroup(rows, "Specialized Skills", "skill-group-specialized", groups[TrainingType.Specialized]);
            AddGroup(rows, "Trained Skills", "skill-group-trained", groups[TrainingType.Trained]);

            if (data.ShowUntrained) {
                AddGroup(rows, "Untrained Skills", "skill-group-untrained", groups[TrainingType.Untrained]);
            }

            if (data.ShowUnusable) {
                AddGroup(rows, "Unusable Skills", "skill-group-unusable", groups[TrainingType.Unusable]);
            }

            return rows;
        }

        private static void AddGroup(List<StatRow> rows, string title, string art, List<StatRow> group) {
            if (group.Count == 0) {
                return;
            }

            rows.Add(new StatRow { Key = "h" + art, Name = title, HeaderArt = art });
            rows.AddRange(group.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase));
        }

        private void RenderList(string tab, List<StatRow> rows, StatRow picked, bool retail) {
            var height = Math.Max(Widgets.Scaled(RowHeight), ImGui.GetTextLineHeight() + 2);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0));

            foreach (var row in rows) {
                if (row.IsHeader) {
                    RenderGroupBar(row, height, retail);
                }
                else {
                    RenderRow(tab, row, row == picked, height, retail);
                }
            }

            ImGui.PopStyleVar();
        }

        /// <summary>A group's coloured bar, its name on it.</summary>
        private static void RenderGroupBar(StatRow row, float height, bool retail) {
            var min = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var max = min + new Vector2(width, height);
            var drawList = ImGui.GetWindowDrawList();

            if (!retail || !ItemIcons.DrawArtBox(drawList, row.HeaderArt, min, max)) {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
            }

            drawList.AddText(new Vector2(min.X + Widgets.Scaled(6), (float)Math.Round(min.Y + (height
                - ImGui.GetTextLineHeight()) / 2)), HeaderText, row.Name);
            ImGui.Dummy(new Vector2(width, height));
        }

        /// <summary>One row: picked with a click, its icon, its name and its value, green above its base and red below.</summary>
        private void RenderRow(string tab, StatRow row, bool isPicked, float height, bool retail) {
            var min = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var max = min + new Vector2(width, height);

            if (ImGui.InvisibleButton($"##stat-{row.Key}", new Vector2(width, height))) {
                Pick(tab, row);
            }

            var hovered = ImGui.IsItemHovered();
            Widgets.Tip(row.Tip);

            var drawList = ImGui.GetWindowDrawList();

            if (isPicked) {
                if (!retail || !ItemIcons.DrawArtBox(drawList, "stat-row-selected", min, max)) {
                    drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.Header));
                }
            }
            else if (hovered) {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.HeaderHovered, 0.5f));
            }

            if (row.Icon != null) {
                icons.DrawAt(drawList, row.Icon, min, height);
            }

            var textY = (float)Math.Round(min.Y + (height - ImGui.GetTextLineHeight()) / 2);
            drawList.AddText(new Vector2(min.X + height + Widgets.Scaled(5), textY), ImGui.GetColorU32(ImGuiCol.Text), row.Name);

            var value = row.ValueText ?? row.Value.ToString();
            var valueSize = ImGui.CalcTextSize(value).X;
            drawList.AddText(new Vector2(max.X - valueSize - Widgets.Scaled(5), textY), ImGui.GetColorU32(ValueColor(row)),
                value);
        }

        /// <summary>Green while buffed above its base, red while debuffed below it, else the plain text colour.</summary>
        private static Vector4 ValueColor(StatRow row) {
            if (row.ValueText == "-" || row.Buff == 0) {
                return ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
            }

            return row.Buff > 0 ? Colors.Good : Colors.Danger;
        }

        // ---- Daralet's skills ----

        /// <summary>
        /// Daralet's skills: those in the client's portal dat skill table, which leaves out the retired and unused ones
        /// (Axe, Sword, Mace...) that Decal's list still has. Decal's whole list when the table can't be read.
        /// </summary>
        private static IEnumerable<CharFilterSkillType> SkillTypes() {
            Dictionary<uint, ACE.DatLoader.Entity.SkillBase> table = null;

            try {
                table = UBService.PortalDat?.SkillTable?.SkillBaseHash;
            }
            catch {
                // UtilityBelt without its portal dat: Decal's list below
            }

            if (table == null || table.Count == 0) {
                foreach (CharFilterSkillType type in Enum.GetValues(typeof(CharFilterSkillType))) {
                    yield return type;
                }

                yield break;
            }

            foreach (var id in table.Keys) {
                if (Enum.IsDefined(typeof(CharFilterSkillType), (int)id)) {
                    yield return (CharFilterSkillType)(int)id;
                }
            }
        }

        /// <summary>
        /// A skill's name as Daralet has it (Physical Defense, Martial Weapons, Thievery...): the client's portal dat skill
        /// table's, which the game's own panel shows. Decal's names are retail's (Melee Defense, Heavy Weapons, Lockpick).
        /// </summary>
        private static string SkillName(CharFilterSkillType type, SkillInfoWrapper info) {
            var name = SkillBase(type)?.Name;
            return string.IsNullOrEmpty(name) ? info.Name : name;
        }

        private static string SkillTip(CharFilterSkillType type, SkillInfoWrapper info) {
            var description = SkillBase(type)?.Description;
            return $"{SkillName(type, info)}: {info.Training}, base {info.Base}."
                + (string.IsNullOrEmpty(description) ? "" : "\n\n" + description);
        }

        private static ACE.DatLoader.Entity.SkillBase SkillBase(CharFilterSkillType type) {
            try {
                var table = UBService.PortalDat?.SkillTable?.SkillBaseHash;
                return table != null && table.TryGetValue((uint)type, out var skill) ? skill : null;
            }
            catch {
                return null;
            }
        }
    }
}
