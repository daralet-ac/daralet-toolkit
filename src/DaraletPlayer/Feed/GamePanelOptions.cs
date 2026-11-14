using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// One of the game's panels that a plugin window can take the place of (Ui/GamePanel.cs), in settings.json under
    /// "gamePanels" by the panel's key: whether the game's is hidden for the plugin's, and which look the plugin's has.
    /// </summary>
    [DataContract]
    public class GamePanelOptions {
        /// <summary>The client's own art, taken from its LayoutDescs.</summary>
        public const string Retail = "retail";

        /// <summary>Plain ImGui, in the plugin's theme.</summary>
        public const string Modern = "modern";

        /// <summary>The game's panel is hidden while the plugin's window takes its place.</summary>
        [DataMember(Name = "replace")] public bool Replace;

        /// <summary>Retail or Modern; anything else (null, from before there was a choice) is Retail.</summary>
        [DataMember(Name = "look")] public string Look;

        public bool IsRetail => Look != Modern;
    }
}
