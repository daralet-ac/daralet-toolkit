using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>What happens to one kind of combat line (the Combat tab's Combat Text).</summary>
    [DataContract]
    public class CombatKindOptions {
        /// <summary>Shown as floating text.</summary>
        [DataMember(Name = "floating")] public bool Floating = true;
        /// <summary>Listed in the Combat log window.</summary>
        [DataMember(Name = "log")] public bool Log = true;
        /// <summary>Kept out of the game's chat.</summary>
        [DataMember(Name = "hideChat")] public bool HideChat;
        /// <summary>Written to the day's file, while saving is on.</summary>
        [DataMember(Name = "save")] public bool Save = true;
        /// <summary>Packed as ImGui has it (PackedColor).</summary>
        [DataMember(Name = "color")] public uint Color;

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            Floating = true;
            Log = true;
            Save = true;
        }
    }
}
