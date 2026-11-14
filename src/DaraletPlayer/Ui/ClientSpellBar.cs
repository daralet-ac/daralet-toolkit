using System;
using System.Collections.Generic;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's own spell bar (gmSpellcastingUI, element 0x10000061 in the Combat floaty, its layout 0x21000010), read
    /// and driven through the client's functions at the addresses UtilityBelt.Service's AcClient gives, with plain
    /// pointers, as ClientElements does. Its eight tabs are SpellCastSubMenus, 0x1C bytes each from +0x628; each holds its
    /// item list (+0x4), whose slots (UIElement_UIItem) carry their spellID at +0x600, and the spell selected in it (+0xC).
    /// The open tab is its tab panel's (+0x5FC) current page (+0x6D8), as GetOpenSubMenuIndex (0x4C6500) reads it. Going
    /// through the game's bar keeps the game's keys (cast, next and previous spell and tab) in step with the plugin's,
    /// and adding or removing a spell is the game's own (AddFavorite and RemoveSpellFromMenu, which tell the server).
    /// Game thread only, once logged in.
    /// </summary>
    internal static class ClientSpellBar {
        public const int Tabs = 8;

        private const uint SpellBarId = 0x10000061;
        private const uint SpellcastingUiType = 0x10000015;
        private const uint UiItemType = 0x10000032;

        private const int PanelOffset = 0x5FC;
        private const int PanelPageOffset = 0x6D8;
        private const int SubMenusOffset = 0x628;
        private const int SubMenuSize = 0x1C;
        private const int SubMenuListOffset = 0x4;
        private const int SubMenuSelectedOffset = 0xC;
        private const int SubMenuCountOffset = 0x10;
        private const int UiItemSpellOffset = 0x600;

        /// <summary>The most slots read from one tab, in case a count is read wrong.</summary>
        private const int MaxSpells = 200;

        /// <summary>Each tab's button and page, in order (the tab panel 0x100000A2 in layout 0x21000010).</summary>
        private static readonly uint[] TabButtons = {
            0x100000A3, 0x100000A4, 0x100000A5, 0x100000A6, 0x100000A7, 0x100000A8, 0x100000A9, 0x100005C2,
        };

        private static readonly uint[] TabPages = {
            0x100000AA, 0x100000AB, 0x100000AC, 0x100000AD, 0x100000AE, 0x100000AF, 0x100000B0, 0x100005C3,
        };

        private static bool broken;

        public static bool Broken => broken;

        /// <summary>The tab the game's bar has open, 0 to 7, or -1 when it can't be read.</summary>
        public static unsafe int OpenTab() {
            try {
                var ui = Ui();

                if (ui == null) {
                    return -1;
                }

                var panel = *(byte**)(ui + PanelOffset);
                return panel == null ? -1 : Array.IndexOf(TabPages, *(uint*)(panel + PanelPageOffset));
            }
            catch (Exception ex) {
                Fail("read", ex);
                return -1;
            }
        }

        /// <summary>A tab's spells in the game's order, and the one selected in it (0 for none). False when it can't be read.</summary>
        public static unsafe bool TryReadTab(int tab, List<int> spells, out int selected) {
            spells.Clear();
            selected = 0;

            if (tab < 0 || tab >= Tabs) {
                return false;
            }

            try {
                var menu = SubMenu(tab);

                if (menu == null) {
                    return false;
                }

                selected = *(int*)(menu + SubMenuSelectedOffset);
                var list = *(void**)(menu + SubMenuListOffset);
                var count = Math.Min(MaxSpells, *(int*)(menu + SubMenuCountOffset));

                if (list == null) {
                    return true;
                }

                for (var i = 0; i < count; i++) {
                    var slot = AsUiItem(ListGetItem(list, (uint)i));
                    var spell = slot == null ? 0 : *(int*)((byte*)slot + UiItemSpellOffset);

                    if (spell != 0) {
                        spells.Add(spell);
                    }
                }

                return true;
            }
            catch (Exception ex) {
                Fail("read", ex);
                return false;
            }
        }

        /// <summary>Opens a tab of the game's bar, as clicking it does (UIElement_Panel::OpenTab, 0x46C110).</summary>
        public static unsafe bool Open(int tab) {
            return Try("open a tab", () => {
                var ui = Ui();
                var panel = ui == null || tab < 0 || tab >= Tabs ? null : *(void**)(ui + PanelOffset);

                if (panel == null) {
                    return false;
                }

                return ((delegate* unmanaged[Thiscall]<void*, uint, byte>)0x46C110)(panel, TabButtons[tab]) != 0;
            });
        }

        /// <summary>Selects a spell in a tab, as clicking it does (SpellCastSubMenu::SetSelected, 0x4C66F0).</summary>
        public static unsafe bool Select(int tab, int spell) {
            return Try("select", () => {
                var menu = SubMenu(tab);

                if (menu == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, int, void>)0x4C66F0)(menu, spell);
                return true;
            });
        }

        /// <summary>
        /// Casts a spell from a tab as the game's Cast button and cast key do: the tab opened and the spell selected, then
        /// gmSpellcastingUI::Cast (0x4C6C40), which picks the target and says why a spell can't go.
        /// </summary>
        public static unsafe bool Cast(int tab, int spell) {
            if (OpenTab() != tab && !Open(tab)) {
                return false;
            }

            if (!Select(tab, spell)) {
                return false;
            }

            return Try("cast", () => {
                var ui = Ui();

                if (ui == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, void>)0x4C6C40)(ui);
                return true;
            });
        }

        /// <summary>
        /// Puts a spell in a tab at a place (its end, if past it), as dropping one on the game's bar does:
        /// SpellCastSubMenu::AddFavorite (0x4C7C50, which adds it to the client's favorites and tells the server), then the
        /// bar's endowment icon and Cast tooltip brought up to date, as gmSpellcastingUI::AddSpellShortcut does.
        /// </summary>
        public static unsafe bool Add(int tab, int spell, int place) {
            // a spell already there stays where it is (Move moves one), and the place is never past the end
            var current = new List<int>();

            if (!TryReadTab(tab, current, out _) || current.Contains(spell)) {
                return false;
            }

            place = Math.Max(0, Math.Min(current.Count, place));

            return Try("add a spell", () => {
                var ui = Ui();
                var menu = SubMenu(tab);

                if (ui == null || menu == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, int, int, int, void>)0x4C7C50)(menu, spell, place, 1);
                ((delegate* unmanaged[Thiscall]<void*, void>)0x4C6D10)(ui);
                ((delegate* unmanaged[Thiscall]<void*, void>)0x4C7620)(ui);
                return true;
            });
        }

        /// <summary>
        /// Takes a spell out of a tab, as dragging it off the game's bar does: SpellCastSubMenu::RemoveSpellFromMenu
        /// (0x4C74E0, which takes it from the client's favorites and tells the server).
        /// </summary>
        public static unsafe bool Remove(int tab, int spell) {
            return Try("remove a spell", () => {
                var menu = SubMenu(tab);

                if (menu == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, int, int>)0x4C74E0)(menu, spell);
                return true;
            });
        }

        /// <summary>A spell in a tab moved to another place there: taken out, then put back at the place (counted without it).</summary>
        public static bool Move(int tab, int spell, int place) {
            return Remove(tab, spell) && Add(tab, spell, place);
        }

        /// <summary>The game's spell bar, cast as its PostInit would (UIElement::DynamicCast, vtable +0x94), or null.</summary>
        private static unsafe byte* Ui() {
            if (broken) {
                return null;
            }

            var element = ClientElements.FindElement(SpellBarId);

            if (element == null) {
                return null;
            }

            var cast = (*(void***)element)[0x94 / 4];
            return (byte*)((delegate* unmanaged[Thiscall]<void*, uint, void*>)cast)(element, SpellcastingUiType);
        }

        private static unsafe byte* SubMenu(int tab) {
            var ui = Ui();
            return ui == null || tab < 0 || tab >= Tabs ? null : ui + SubMenusOffset + tab * SubMenuSize;
        }

        /// <summary>UIElement_ListBox::GetItem, as ClientElements.Containers uses it.</summary>
        private static unsafe void* ListGetItem(void* list, uint index) {
            return ((delegate* unmanaged[Thiscall]<void*, uint, void*>)0x46DC50)(list, index);
        }

        private static unsafe void* AsUiItem(void* element) {
            if (element == null) {
                return null;
            }

            var cast = (*(void***)element)[0x94 / 4];
            return ((delegate* unmanaged[Thiscall]<void*, uint, void*>)cast)(element, UiItemType);
        }

        private static bool Try(string what, Func<bool> call) {
            if (broken) {
                return false;
            }

            try {
                var done = call();
                PluginCore.LogFile($"[spell-bar] {what}: {(done ? "done" : "nothing to do it on")}");
                return done;
            }
            catch (Exception ex) {
                Fail(what, ex);
                return false;
            }
        }

        private static void Fail(string what, Exception ex) {
            broken = true;
            PluginCore.Log($"The game's spell bar can't be {(what == "read" ? "read" : "used (" + what + ")")}, so the "
                + "plugin's is off until it next starts: " + ex);
        }
    }
}
