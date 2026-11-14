using System;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>The Attributes window's own choices (Ui/AttributesPanel.cs), in settings.json as "attributes".</summary>
    [DataContract]
    public class AttributesOptions {
        public const string AttributesTab = "attributes";
        public const string SkillsTab = "skills";
        public const string TitlesTab = "titles";

        /// <summary>Which page shows: attributes and vitals, skills, or titles.</summary>
        [DataMember(Name = "tab")] public string Tab;
        /// <summary>Untrained skills you can still use, under the trained ones.</summary>
        [DataMember(Name = "showUntrained")] public bool ShowUntrained;
        /// <summary>Skills you can't use until they're trained.</summary>
        [DataMember(Name = "showUnusable")] public bool ShowUnusable;

        public AttributesOptions() {
            SetDefaults();
        }

        /// <summary>"1,234,567": XP as the game writes it.</summary>
        public static string Xp(long xp) {
            return xp.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Tab = AttributesTab;
            ShowUntrained = true;
        }
    }
}
