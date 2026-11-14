using System.Numerics;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Which of the game's drop-down menus Combos draws: its field's pictures (each cut into 4-pixel ends and a middle,
    /// name-left...), its arrow's, and the colours ImGui's list is drawn in to match.
    /// </summary>
    internal sealed class ComboLook {
        /// <summary>
        /// The vendor window's kind menu, the default (template 0x1000034B in layout 0x21000043): the field 060012B3, dark
        /// green in a grey bevel, the arrow 060012B1 (open 060012B2, 17x19), the picked row 060012B4.
        /// </summary>
        public static readonly ComboLook Game = new ComboLook {
            Field = "combo",
            PickedRow = "combo-row-selected",
            Arrow = "combo-arrow",
            ArrowOpen = "combo-arrow-open",
            ArrowAspect = 17f / 19,
            ListBg = new Vector4(0x0A / 255f, 0x14 / 255f, 0x04 / 255f, 0.97f),
            ListBorder = new Vector4(0x5D / 255f, 0x69 / 255f, 0x57 / 255f, 1),
            Picked = new Vector4(0x34 / 255f, 0x67 / 255f, 0x15 / 255f, 1),
            PickedHover = new Vector4(0x3C / 255f, 0x6E / 255f, 0x1B / 255f, 1),
        };

        /// <summary>
        /// The book window's page menu (template 0x10000471 in layout 0x21000043): the gold field 06001276 (273x18), its
        /// rows the same with 0600127A for the one picked, the green arrow 06001274 (open 06001275, 20x19), dark words.
        /// </summary>
        public static readonly ComboLook Book = new ComboLook {
            Field = "book-page",
            PickedRow = "book-page-selected",
            Arrow = "book-page-arrow",
            ArrowOpen = "book-page-arrow-open",
            ArrowAspect = 20f / 19,
            ListBg = new Vector4(0x8C / 255f, 0x74 / 255f, 0x2E / 255f, 0.98f),
            ListBorder = new Vector4(0x3A / 255f, 0x2E / 255f, 0x12 / 255f, 1),
            Picked = new Vector4(0xC9 / 255f, 0xB0 / 255f, 0x6A / 255f, 1),
            PickedHover = new Vector4(0xD4 / 255f, 0xBD / 255f, 0x7A / 255f, 1),
            Text = new Vector4(0.08f, 0.06f, 0.02f, 1),
        };

        public string Field { get; private set; }
        public string PickedRow { get; private set; }
        public string Arrow { get; private set; }
        public string ArrowOpen { get; private set; }

        /// <summary>The arrow's width over its height.</summary>
        public float ArrowAspect { get; private set; }

        public Vector4 ListBg { get; private set; }
        public Vector4 ListBorder { get; private set; }
        public Vector4 Picked { get; private set; }
        public Vector4 PickedHover { get; private set; }

        /// <summary>The words' colour on the field and in the list, or null for the theme's.</summary>
        public Vector4? Text { get; private set; }
    }
}
