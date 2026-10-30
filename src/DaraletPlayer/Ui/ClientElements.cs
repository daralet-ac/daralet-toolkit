using System;
using System.Numerics;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Where the game draws a piece of its own UI, found by its element id (the ids in client_local_English.dat's
    /// LayoutDescs), by calling the client's own functions at the addresses UtilityBelt.Service's AcClient gives them.
    /// Not through AcClient's types: they aren't blittable, so .NET won't marshal a call that takes or returns one
    /// ("Pointers cannot reference marshaled structures", from UIElement.GetChildRecursive), and their fields' places in
    /// memory aren't promised either. Every pointer here is a plain void*.
    /// </summary>
    internal static class ClientElements {
        /// <summary>UIElementManager::s_pInstance, read each time: it's made after the plugin may start.</summary>
        private const int ManagerAddress = 0x83E03C;

        // the toolbar itself (UIElement_ID TBAR), and its buttons, in layout 0x21000016
        public const uint Toolbar = 0x10000603;
        public const uint ToolbarBackpack = 0x100001B1;
        public const uint ToolbarContracts = 0x1000055A;
        public const uint ToolbarMap = 0x1000019A;
        public const uint ToolbarSocial = 0x10000197;

        /// <summary>
        /// The frame the game shows its panels in (inventory, character, skills, options...: element type 0x1000004F, its
        /// layout 0x2100006E), right above the toolbar and as wide, in the main UI's layout 0x21000005: 310x372 at
        /// 490,128 at 800x600, its panels 300x362 inside a 5-pixel border. Shown while one of them is open. A
        /// gmFloatyPanelUI: a window of its own that stays where it was last put, not where the toolbar is.
        /// </summary>
        public const uint PanelFrame = 0x100005FF;

        // the indicators by the toolbar (INDI), and their Character Information button (the burden pack), in layout
        // 0x21000071
        public const uint Indicators = 0x10000611;
        public const uint IndicatorBurden = 0x100000F7;

        /// <summary>
        /// The panel along the bottom (UIElement_ID ENVP) where the game shows an open vendor (its layout 0x21000012,
        /// whose own root element 0x100000B7 the game never makes), chest or corpse.
        /// </summary>
        public const uint Environment = 0x100005FD;

        // the vendor window's tabs (a UIElement_Panel, element type 8), its Selling tab, and the Selling page's list of
        // 32-pixel slots, in its layout 0x21000012
        public const uint VendorTabs = 0x100000B8;
        public const uint VendorSellingTab = 0x100000BB;
        public const uint VendorSellingList = 0x100000CE;

        /// <summary>The number box beside the toolbar's stack slider (gmToolbarUI's m_pStackSizeEntryBox).</summary>
        public const uint StackSizeEntry = 0x100001A3;

        /// <summary>UIElementManager.m_focusElement, as AcClient lays it out: the element typed into.</summary>
        private const int FocusOffset = 0x328;

        /// <summary>UIRegion.m_parent, which UIRegion::GetScreenX0 climbs.</summary>
        private const int ParentOffset = 0xAC;

        /// <summary>How much bigger than an element its parent may be, each way, and still be taken for its frame.</summary>
        private const float FrameSlack = 48;

        /// <summary>
        /// gmPanelUI's element type (its DynamicCast, which gmFloatyPanelUI's, type 0x1000004F, passes on), and where its
        /// notice handler starts in it (gmPanelUI::gmPanelUI sets its vtable at +0x5F8).
        /// </summary>
        private const uint PanelUiType = 0x10000008;
        private const int PanelNoticeOffset = 0x5F8;

        /// <summary>Set once a call has failed: logged once, and nothing is looked up again until the next start.</summary>
        private static bool broken;

        /// <summary>A call has failed, so nothing's found until the next start.</summary>
        public static bool Broken => broken;

        /// <summary>
        /// The element's box on screen, in the game window's pixels (which are ImGui's), while it and the panel it's in
        /// (Toolbar, Indicators) are shown. False when there's no such element, it's hidden (its panel closed), or the
        /// client's functions can't be called.
        /// </summary>
        public static unsafe bool TryGetScreenBox(uint id, uint panelId, out Vector2 min, out Vector2 max) {
            min = max = Vector2.Zero;

            if (broken) {
                return false;
            }

            try {
                var manager = *(void**)ManagerAddress;

                if (manager == null) {
                    return false;
                }

                var element = GetElement(manager, id);
                var panel = GetElement(manager, panelId);

                if (element == null || !IsVisible(element) || (panel != null && !IsVisible(panel))) {
                    return false;
                }

                var x = GetScreenX0(element);
                var y = GetScreenY0(element);
                var width = GetWidth(element);
                var height = GetHeight(element);

                if (width <= 0 || height <= 0) {
                    return false;
                }

                min = new Vector2(x, y);
                max = new Vector2(x + width, y + height);
                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's toolbar buttons can't be found, so the plugin's are off until it next starts: "
                    + ex);
                return false;
            }
        }

        /// <summary>
        /// Opens a tab of a panel of tabs (UIElement_Panel::OpenTab, by the tab's element id), as the game's own code does:
        /// gmVendorUI::UpdateDragOver opens the Selling tab this way while something's dragged over the vendor window.
        /// False when the panel isn't there or the call fails.
        /// </summary>
        public static unsafe bool OpenTab(uint panelId, uint tabId) {
            if (broken) {
                return false;
            }

            try {
                var manager = *(void**)ManagerAddress;
                var panel = manager == null ? null : GetElement(manager, panelId);

                if (panel == null || !IsVisible(panel)) {
                    return false;
                }

                return ((delegate* unmanaged[Thiscall]<void*, uint, byte>)0x46C110)(panel, tabId) != 0;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's panels can't be reached, so the plugin's lookups are off until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Takes the keyboard from the element if it has it, as a click anywhere else in the game does:
        /// UIElementManager::SetFocusElement(null), which tells the element it's lost focus (message 0x2F, 0). A click on
        /// an ImGui window never reaches the game, so a box typed into keeps it. True when it had it.
        /// </summary>
        public static unsafe bool Unfocus(uint id) {
            if (broken) {
                return false;
            }

            try {
                var manager = *(byte**)ManagerAddress;

                if (manager == null) {
                    return false;
                }

                var focus = *(void**)(manager + FocusOffset);

                if (focus == null || focus != GetElement(manager, id)) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, void*, void>)0x45B970)(manager, null);
                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be reached, so the plugin's lookups are off until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>UIFlow::m_instance, which runs the game's UI modes (character select, in the world...).</summary>
        private const int UiFlowAddress = 0x83E72C;

        /// <summary>UIFlow::_curUI: the mode's UI, a UIMainFramework.</summary>
        private const int CurrentUiOffset = 0x94;

        /// <summary>gmGamePlayUI's vtable, which tells the in-world UI from the other modes'.</summary>
        private const int GamePlayUiVtable = 0x7C3828;

        /// <summary>gmGamePlayUI's root element, which gmGamePlayUI::SetupChildren makes and every panel is in.</summary>
        private const int GamePlayRootOffset = 0x9C;

        /// <summary>
        /// Shows or hides all of the game's in-world UI: UIElement::SetVisible on gmGamePlayUI's root element. Not
        /// UIElementManager::SetVisible, which only sees to the cursor and mouse capture, nor UIFramework::ForceHidden,
        /// which only hides panels flagged as hideable, and none start that way. False when the game isn't showing its
        /// in-world UI, the call fails, or the root isn't as asked after it. Showing is tried even after another lookup
        /// broke, so a hidden UI can always be put back.
        /// </summary>
        public static unsafe bool SetUiVisible(bool shown) {
            if (broken && !shown) {
                return false;
            }

            try {
                var flow = *(byte**)UiFlowAddress;
                var ui = flow == null ? null : *(byte**)(flow + CurrentUiOffset);

                // character select, or between modes: nothing of the in-world UI to show or hide
                if (ui == null || *(int*)ui != GamePlayUiVtable) {
                    return false;
                }

                var root = *(void**)(ui + GamePlayRootOffset);

                if (root == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, byte, void>)0x462390)(root, (byte)(shown ? 1 : 0));
                return IsVisible(root) == shown;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be " + (shown ? "shown" : "hidden") + ", so the plugin's lookups are off "
                    + "until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Shows or hides one element (UIElement::SetVisible, as SetUiVisible does for the whole UI). False when there's no
        /// such element or the call fails. Showing is tried even after another lookup broke, so it can always be put back.
        /// </summary>
        public static unsafe bool SetVisible(uint id, bool shown) {
            if (broken && !shown) {
                return false;
            }

            try {
                var element = FindElement(id);

                if (element == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, byte, void>)0x462390)(element, (byte)(shown ? 1 : 0));
                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be " + (shown ? "shown" : "hidden") + ", so the plugin's lookups are off "
                    + "until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Where the element is on screen whether it's shown or not (the game's panel frame, while no panel is open). False
        /// when there's no such element or the client's functions can't be called.
        /// </summary>
        public static unsafe bool TryGetBox(uint id, out Vector2 min, out Vector2 max) {
            min = max = Vector2.Zero;

            if (broken) {
                return false;
            }

            try {
                var element = FindElement(id);

                if (element == null || GetWidth(element) <= 0 || GetHeight(element) <= 0) {
                    return false;
                }

                min = new Vector2(GetScreenX0(element), GetScreenY0(element));
                max = min + new Vector2(GetWidth(element), GetHeight(element));
                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be found, so the plugin's lookups are off until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Where the element's frame is on screen, shown or not: its parent (UIRegion.m_parent) when that fits snugly round
        /// it (a border's width or so, the toolbar's frame round TBAR), else the element itself. False as TryGetBox.
        /// </summary>
        public static unsafe bool TryGetFrameBox(uint id, out Vector2 min, out Vector2 max) {
            if (!TryGetBox(id, out min, out max)) {
                return false;
            }

            try {
                var parent = *(void**)((byte*)FindElement(id) + ParentOffset);

                if (parent == null || GetWidth(parent) <= 0 || GetHeight(parent) <= 0) {
                    return true;
                }

                var parentMin = new Vector2(GetScreenX0(parent), GetScreenY0(parent));
                var parentMax = parentMin + new Vector2(GetWidth(parent), GetHeight(parent));
                var snug = parentMin.X <= min.X && parentMin.Y <= min.Y && parentMax.X >= max.X && parentMax.Y >= max.Y
                    && (parentMax - parentMin - (max - min)).X <= FrameSlack
                    && (parentMax - parentMin - (max - min)).Y <= FrameSlack;

                if (snug) {
                    min = parentMin;
                    max = parentMax;
                }

                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be found, so the plugin's lookups are off until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Closes whichever of the game's panels is open (inventory, spellbook, options...), as its toolbar button does:
        /// CM_UI::SendNotice_SetPanelVisibility(panel id, 0), which gmPanelUI (the panel frame) and gmToolbarUI (its
        /// button's light) both hear. Closing a panel that stood in for another (gmPanelUI's m_previous, +0x14) opens that
        /// one again, so it's closed too. False when none was open, or the call fails.
        /// </summary>
        public static unsafe bool CloseGamePanel() {
            if (broken) {
                return false;
            }

            try {
                var frame = (byte*)FindElement(PanelFrame);

                // gmFloatyPanelUI, or plain gmPanelUI: UIElement::DynamicCast (vtable +0x94) to gmPanelUI's type
                if (frame == null || !IsVisible(frame)
                    || ((delegate* unmanaged[Thiscall]<void*, uint, void*>)(*(void***)frame)[0x94 / 4])(frame,
                        PanelUiType) != frame) {
                    return false;
                }

                var closed = false;

                for (var tries = 0; tries < 3; tries++) {
                    var id = OpenPanelId(frame + PanelNoticeOffset);

                    if (id == 0) {
                        break;
                    }

                    ((delegate* unmanaged[Cdecl]<uint, byte, byte>)0x47A4A0)(id, 0);
                    closed = true;
                }

                return closed;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's panels can't be reached, so the plugin's lookups are off until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>
        /// The id of the panel open in gmPanelUI (its notice handler, as gmPanelUI::RecvNotice_SetPanelVisibility reads
        /// it: the panels as (element, id) pairs, a SmartArray at +0x4 with its count at +0xC, and the open one at +0x10),
        /// or 0.
        /// </summary>
        private static unsafe uint OpenPanelId(byte* notices) {
            var open = *(void**)(notices + 0x10);
            var panels = *(byte**)(notices + 0x4);
            var count = *(uint*)(notices + 0xC);

            if (open == null || panels == null) {
                return 0;
            }

            for (var i = 0; i < count && i < 64; i++) {
                if (*(void**)(panels + i * 8) == open) {
                    return *(uint*)(panels + i * 8 + 4);
                }
            }

            return 0;
        }

        /// <summary>
        /// Where the element is in its parent (UIRegion's m_x0 and m_y0, +0x7C and +0x80, which UIElement::MoveTo takes),
        /// its size, and its parent's height. False as TryGetBox.
        /// </summary>
        public static unsafe bool TryGetPlace(uint id, out int x, out int y, out int width, out int height,
            out int parentHeight) {
            x = y = width = height = parentHeight = 0;

            if (broken) {
                return false;
            }

            try {
                var element = (byte*)FindElement(id);
                var parent = element == null ? null : *(void**)(element + ParentOffset);

                if (element == null || parent == null) {
                    return false;
                }

                x = *(int*)(element + 0x7C);
                y = *(int*)(element + 0x80);
                width = GetWidth(element);
                height = GetHeight(element);
                parentHeight = GetHeight(parent);
                return width > 0 && height > 0;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be found, so the plugin's lookups are off until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Moves and sizes an element with UIElement::MoveTo and UIElement::ResizeTo, which lay its children out again.
        /// Called as themselves, not through the element's vtable: the floaty windows' own (gmFloatyToolbarUI's, say) save
        /// the new place in the player's UI preferences, and the plugin's changes are never meant to outlast it.
        /// ResizeTo holds the size inside the element's own limits (attributes 0x3C to 0x3F, SetIntAttribute). False when
        /// there's no such element or the calls fail.
        /// </summary>
        public static unsafe bool Place(uint id, int x, int y, int width, int height) {
            if (broken) {
                return false;
            }

            try {
                var element = FindElement(id);

                if (element == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, int, int, void>)0x463D60)(element, width, height);
                ((delegate* unmanaged[Thiscall]<void*, int, int, void>)0x4634C0)(element, x, y);
                return true;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be moved, so the plugin's lookups are off until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>An element's whole-number attribute (UIElement::GetAttribute_Int), or null when it has none.</summary>
        public static unsafe int? GetIntAttribute(uint id, uint attribute) {
            if (broken) {
                return null;
            }

            try {
                var element = FindElement(id);
                var value = 0;

                if (element == null
                    || ((delegate* unmanaged[Thiscall]<void*, uint, int*, byte>)0x460B30)(element, attribute, &value) == 0) {
                    return null;
                }

                return value;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be found, so the plugin's lookups are off until it next starts: " + ex);
                return null;
            }
        }

        /// <summary>
        /// Sets an element's whole-number attribute (UIElement::SetAttribute_Int), in memory only: the next login reads
        /// the layout's own again. False when there's no such element or the game didn't take it.
        /// </summary>
        public static unsafe bool SetIntAttribute(uint id, uint attribute, int value) {
            if (broken) {
                return false;
            }

            try {
                var element = FindElement(id);
                return element != null
                    && ((delegate* unmanaged[Thiscall]<void*, uint, int, byte>)0x4606B0)(element, attribute, value) != 0;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be changed, so the plugin's lookups are off until it next starts: " + ex);
                return false;
            }
        }

        /// <summary>Whether the element is there and shown; null when that can't be told.</summary>
        public static unsafe bool? IsShown(uint id) {
            if (broken) {
                return null;
            }

            try {
                var element = FindElement(id);
                return element == null ? (bool?)null : IsVisible(element);
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log("The game's UI can't be found, so the plugin's lookups are off until it next starts: " + ex);
                return null;
            }
        }

        /// <summary>Whether a spot in the game window is on the element, while it's shown.</summary>
        public static bool IsOver(uint id, Vector2 at) {
            return TryGetScreenBox(id, id, out var min, out var max)
                && at.X >= min.X && at.X < max.X && at.Y >= min.Y && at.Y < max.Y;
        }

        /// <summary>An element anywhere in the UI by its id, or null (before the UI's made, too). May throw.</summary>
        internal static unsafe void* FindElement(uint id) {
            var manager = *(void**)ManagerAddress;
            return manager == null ? null : GetElement(manager, id);
        }

        // An element starts with its region, so the same pointer serves both.

        /// <summary>UIElementManager::GetElement: an element anywhere in the UI by its id, or null.</summary>
        private static unsafe void* GetElement(void* manager, uint id) {
            return ((delegate* unmanaged[Thiscall]<void*, uint, void*>)0x459A00)(manager, id);
        }

        /// <summary>UIElement::IsVisible.</summary>
        private static unsafe bool IsVisible(void* element) {
            return ((delegate* unmanaged[Thiscall]<void*, byte>)0x4603A0)(element) != 0;
        }

        /// <summary>UIRegion::GetScreenX0 and the rest: where the region is on screen, and its size.</summary>
        private static unsafe int GetScreenX0(void* region) {
            return ((delegate* unmanaged[Thiscall]<void*, int>)0x69FE00)(region);
        }

        private static unsafe int GetScreenY0(void* region) {
            return ((delegate* unmanaged[Thiscall]<void*, int>)0x69FE30)(region);
        }

        private static unsafe int GetWidth(void* region) {
            return ((delegate* unmanaged[Thiscall]<void*, int>)0x69FE60)(region);
        }

        private static unsafe int GetHeight(void* region) {
            return ((delegate* unmanaged[Thiscall]<void*, int>)0x69FE70)(region);
        }
    }
}
