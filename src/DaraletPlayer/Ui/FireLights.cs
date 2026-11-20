using System;
using System.Collections.Generic;
using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Light from fires the game gives none (LampLightOptions.FireLight; the user's call, November 2026: torches and
    /// campfires more dramatic at night). The game lights only what its dats give a light: Hebian-to's bonfire, "Bliz's
    /// Eternal Bonfire", glows but is in neither of its light lists (seen in the game). So each object in the world
    /// named like a fire (Words) gets a light of the plugin's: warm, a meter above it, reaching FireFalloff as the game's
    /// lamps reach theirs, flickering a little; it joins the game's lights in the lamp pass (DepthShading.Lamps.cs),
    /// shadow map and all. Creatures, players and anything carried are left out ("Fire Elemental"). Found from Decal's
    /// GetLandscape once a second (LoginStatus 3 only: its reads crash the game before login), as values, never kept
    /// WorldObjects. Untested in the game when written.
    /// </summary>
    internal static class FireLights {
        private static readonly string[] Words = { "bonfire", "campfire", "fire", "torch", "brazier" };

        /// <summary>How far a fire reaches before the Reach setting, as a game lamp's falloff.</summary>
        private const float FireFalloff = 6;

        private static readonly List<(int Id, uint Cell, float X, float Y, float Z)> found =
            new List<(int, uint, float, float, float)>();

        private static int nextLook;
        private static bool logged;

        /// <summary>The fires found near, each as a light, added to the game's.</summary>
        public static void AddTo(List<GameLight> lights) {
            var now = Environment.TickCount;

            if (unchecked(now - nextLook) >= 0) {
                nextLook = now + 1000;
                Look();
            }

            var seconds = (now & 0x3FFFFFFF) / 1000f;

            foreach (var f in found) {
                var (east, north) = RenderOrigin.InFrame(f.Cell >> 16, f.X, f.Y);
                // a flame's flicker: three slow waves at odd rates, a different phase each fire, never below 80%
                var phase = (f.Id & 0xFFFF) * 0.37f;
                var flicker = 0.9f + 0.05f * (float)Math.Sin(seconds * 7.3 + phase)
                    + 0.03f * (float)Math.Sin(seconds * 13.1 + phase * 2) + 0.02f * (float)Math.Sin(seconds * 23.7 + phase);
                lights.Add(new GameLight {
                    Type = 0, Cell = f.Cell, East = east, North = north, Height = f.Z + 1,
                    Red = 1f * flicker, Green = 0.55f * flicker, Blue = 0.25f * flicker, Intensity = 100,
                    Falloff = FireFalloff,
                });
            }
        }

        private static void Look() {
            found.Clear();

            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                return;
            }

            foreach (var wo in CoreManager.Current.WorldFilter.GetLandscape()) {
                var name = wo.Name ?? "";

                if (!IsFire(name) || wo.ObjectClass == ObjectClass.Monster || wo.ObjectClass == ObjectClass.Player
                    || wo.ObjectClass == ObjectClass.Npc || wo.ObjectClass == ObjectClass.Vendor
                    || wo.Values(LongValueKey.Container, 0) != 0) {
                    continue;
                }

                var raw = wo.RawCoordinates();
                var cell = unchecked((uint)wo.Values(LongValueKey.Landblock, 0));
                found.Add((wo.Id, cell, (float)raw.X, (float)raw.Y, (float)raw.Z));

                if (!logged) {
                    logged = true;
                    PluginCore.LogFile($"[lamps] a fire given light: {name} at {cell:X8} "
                        + $"[{raw.X:0.0} {raw.Y:0.0} {raw.Z:0.0}]");
                }
            }
        }

        private static bool IsFire(string name) {
            foreach (var word in Words) {
                if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Let go at shutdown (a hot reload keeps every build's statics).</summary>
        public static void Clear() {
            found.Clear();
        }
    }
}
