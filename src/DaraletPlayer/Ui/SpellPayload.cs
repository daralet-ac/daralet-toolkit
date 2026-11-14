using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A spell dragged from one of the plugin's windows (the Spellbook) to another (the Spell bar, a hotbar): an ImGui drag
    /// with this payload type, the spell kept here, since ImGui's payload is only bytes.
    /// </summary>
    internal static class SpellPayload {
        public const string Type = "DAP_SPELL";

        /// <summary>The spell being dragged, or 0.</summary>
        public static int Spell { get; private set; }

        /// <summary>Makes the item just drawn a drag source for this spell, its icon following the mouse.</summary>
        public static void Source(int spell, ItemIcons icons, float size) {
            if (!ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip)) {
                return;
            }

            Spell = spell;
            ImGui.SetDragDropPayload(Type, IntPtr.Zero, 0);
            icons?.DrawAt(ImGui.GetForegroundDrawList(), Spells.Icon(spell), ImGui.GetMousePos() - new Vector2(size / 2, size / 2),
                size);
            ImGui.EndDragDropSource();
        }

        /// <summary>The spell let go over the item just drawn this frame, or 0.</summary>
        public static unsafe int Accept() {
            if (!ImGui.BeginDragDropTarget()) {
                return 0;
            }

            var payload = ImGui.AcceptDragDropPayload(Type);
            var spell = payload.NativePtr != null ? Spell : 0;
            ImGui.EndDragDropTarget();
            return spell;
        }
    }
}
