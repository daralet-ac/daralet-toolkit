namespace DaraletPlayer.Feed {
    /// <summary>
    /// Materials by MaterialType number (Decal's LongValueKey.Material, daralet-ACE's MaterialType enum), named as the
    /// game names them: the server's RecipeManager.GetMaterialName reads the portal dat's mapper 0x27000000, which
    /// spells each as the enum does, with underscores for spaces.
    /// </summary>
    internal static class MaterialNames {
        private static readonly string[] Names = {
            null, "Ceramic", "Porcelain", "Cloth", "Linen", "Satin", "Silk", "Velvet", "Wool", "Gem",
            "Agate", "Amber", "Amethyst", "Aquamarine", "Azurite", "Black Garnet", "Black Opal", "Bloodstone", "Carnelian",
            "Citrine", "Diamond", "Emerald", "Fire Opal", "Green Garnet", "Green Jade", "Hematite", "Imperial Topaz", "Jet",
            "Lapis Lazuli", "Lavender Jade", "Malachite", "Moonstone", "Onyx", "Opal", "Peridot", "Red Garnet", "Red Jade",
            "Rose Quartz", "Ruby", "Sapphire", "Smokey Quartz", "Sunstone", "Tiger Eye", "Tourmaline", "Turquoise",
            "White Jade", "White Quartz", "White Sapphire", "Yellow Garnet", "Yellow Topaz", "Zircon", "Ivory", "Leather",
            "Armoredillo Hide", "Gromnie Hide", "Reed Shark Hide", "Metal", "Brass", "Bronze", "Copper", "Gold", "Iron",
            "Pyreal", "Silver", "Steel", "Stone", "Alabaster", "Granite", "Marble", "Obsidian", "Sandstone", "Serpentine",
            "Wood", "Ebony", "Mahogany", "Oak", "Pine", "Teak",
        };

        /// <summary>The material's name, or null for none (0) or one this table doesn't know.</summary>
        public static string Of(int materialType) {
            return materialType > 0 && materialType < Names.Length ? Names[materialType] : null;
        }

        /// <summary>A name with its material in front, as the game writes it (unless the name has it already).</summary>
        public static string NameWith(string name, int materialType) {
            var material = Of(materialType);
            return material == null || name == null ? name : ItemLines.WithMaterial(name, material);
        }
    }
}
