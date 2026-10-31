using System;
using System.Collections.Generic;
using System.Globalization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The pictures a game icon is drawn from, bottom to top, as portal.dat ids (0 for none): the tile behind it for its kind
    /// of item, the item, and a mark on top (a gem's material, say).
    /// </summary>
    public class IconLayers {
        /// <summary>
        /// The tiles the client draws behind an item, by the kind the Market API names (MarketListing.IconUnderlay), copied
        /// from the website's MKT_ICON_UNDERLAYS (market.js). The API and the website call every weapon "Weapon", which is a
        /// missile weapon's tile: portal.dat's table (0x25000008, by ItemType's bit number plus one) gives melee its own, and
        /// casters the plain one, which WeaponKind picks from the weapon's class.
        /// </summary>
        public static readonly Dictionary<string, uint> Underlays = new Dictionary<string, uint> {
            ["Weapon"] = 0x060011D2,
            ["MeleeWeapon"] = 0x060011CB,
            ["Caster"] = 0x060011D4,
            ["Armor"] = 0x060011CF,
            ["Clothing"] = 0x060011F3,
            ["Jewelry"] = 0x060011D5,
            ["SpellComponent"] = 0x060011CD,
            ["Misc"] = 0x060011D4,
            ["SigilBlue"] = 0x06008700,
            ["SigilYellow"] = 0x06008701,
            ["SigilRed"] = 0x06008702,
        };

        /// <summary>
        /// The 32x32 picture an icon's outline is cut from, by its effect, from portal.dat's table 0x25000009 (the client's
        /// IconData::RenderIcons, 0x58DFB0): every pure white pixel takes the colour at the same spot in the picture
        /// (0x441750), which is what makes the outline a gradient running down the icon. Undef is the table's Default, black,
        /// which is also what an effect with no picture of its own (Nether) gets. The names are the Market API's, then the
        /// client's.
        /// </summary>
        public static readonly Dictionary<string, uint> EffectImages = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) {
            ["Magical"] = 0x060011CA,
            ["Poisoned"] = 0x060011C6,
            ["BoostHealth"] = 0x06001B05,
            ["BoostMana"] = 0x060011CA,
            ["BoostStamina"] = 0x06001B06,
            ["Fire"] = 0x06001B2E,
            ["Electric"] = 0x06001B2D,
            ["Cold"] = 0x06001B2F,
            ["Acid"] = 0x06001B2C,
            ["Bludgeoning"] = 0x060033C3,
            ["Slashing"] = 0x060033C2,
            ["Piercing"] = 0x060033C4,
            ["Undef"] = 0x060011C5,
        };

        /// <summary>
        /// The one colour a sigil trinket's outline is, copied from the website's MKT_ICON_OUTLINE_COLORS (market.js). The
        /// website's own idea, by the trinket's colour rather than a UiEffect, so the client has no picture for these.
        /// </summary>
        public static readonly Dictionary<string, (byte R, byte G, byte B)> SigilOutlines = new Dictionary<string, (byte, byte, byte)>(StringComparer.OrdinalIgnoreCase) {
            ["SigilBlue"] = (30, 110, 255),
            ["SigilYellow"] = (255, 209, 0),
            ["SigilRed"] = (220, 20, 20),
        };

        /// <summary>The only pixel the client treats as outline, as Color.ToArgb gives it: opaque, pure white.</summary>
        public const int OutlinePixel = -1;

        /// <summary>UiEffects by bit number, as EffectOf reads them. Nether (bit 12) has no picture, so it's left out.</summary>
        private static readonly string[] EffectBits = {
            "Magical", "Poisoned", "BoostHealth", "BoostMana", "BoostStamina", "Fire", "Electric", "Cold", "Acid",
            "Bludgeoning", "Slashing", "Piercing",
        };

        public uint Underlay;
        public uint Icon;
        public uint Overlay;

        /// <summary>A key of EffectImages or SigilOutlines: what the outline is drawn with. Anything else is Undef's.</summary>
        public string Effect;

        public IconLayers(uint underlay, uint icon, uint overlay, string effect = "Undef") {
            Underlay = underlay;
            Icon = icon;
            Overlay = overlay;
            Effect = effect ?? "Undef";
        }

        /// <summary>An item with no icon of its own draws nothing, not a bare tile.</summary>
        public bool IsEmpty => Icon == 0;

        /// <summary>The picture for an effect, Undef's for one it doesn't know. A sigil's outline is a colour instead.</summary>
        public static uint EffectImage(string effect) {
            return EffectImages.TryGetValue(effect ?? "Undef", out var image) ? image : EffectImages["Undef"];
        }

        /// <summary>A market listing's icon, as market search shows it.</summary>
        public static IconLayers Of(MarketListing listing) {
            return listing == null ? null : FromHex(listing.IconHex, listing.IconOverlayHex, WeaponKind(listing), listing.IconEffect);
        }

        /// <summary>The kind of tile behind a listing, with "Weapon" made melee or caster where its class says so.</summary>
        private static string WeaponKind(MarketListing listing) {
            if (listing.IconUnderlay != "Weapon" || listing.WeaponClass == "Thrown") {
                return listing.IconUnderlay;
            }

            switch (MarketSearch.SubtypeOf(listing.WeaponClass)) {
                case MarketSearch.Melee:
                    return "MeleeWeapon";
                case MarketSearch.Caster:
                    return "Caster";
                default:
                    return listing.IconUnderlay;
            }
        }

        /// <summary>One of your listings, sales or expired items, from the icon @playerui market sends with it.</summary>
        public static IconLayers Of(ListingIconInfo icon) {
            return icon == null ? null : FromHex(icon.Icon, icon.Overlay, icon.Underlay, icon.Effect);
        }

        /// <summary>Hex ids ("06001234") and an underlay kind ("Weapon"), as the Market API writes them.</summary>
        public static IconLayers FromHex(string icon, string overlay, string underlayKind, string effect = null) {
            var underlay = Underlays.TryGetValue(underlayKind ?? "Misc", out var tile) ? tile : Underlays["Misc"];
            return new IconLayers(underlay, Hex(icon), Hex(overlay), effect);
        }

        /// <summary>
        /// The effect from the game's UiEffects flags (what Decal calls IconOutline): the lowest bit set, as the client picks
        /// it (0x685460), so magic comes before an element. The Market API has no flags to read and picks its own way.
        /// </summary>
        public static string EffectOf(uint uiEffects) {
            for (var bit = 0; bit < 32; bit++) {
                if ((uiEffects & (1u << bit)) != 0) {
                    return bit < EffectBits.Length ? EffectBits[bit] : "Undef";
                }
            }

            return "Undef";
        }

        private static uint Hex(string text) {
            return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }
    }
}
