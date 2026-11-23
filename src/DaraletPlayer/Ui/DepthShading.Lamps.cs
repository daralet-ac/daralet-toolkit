using System;
using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Light from the game's own lights (LampLightOptions; the user's call, November 2026: lighting and shadows from
    /// other than the sun). The game lights only what it chooses with them (objects and indoor cells, per vertex, with
    /// Direct3D lights set by Render::config_hardware_light, 0x59BD40: diffuse the colour times the intensity, often
    /// 100, attenuation 1 over the distance, so all but saturated in reach, and a range of the falloff times the float
    /// at 0x821CD4), never the ground outdoors, and nothing blocks them. So the eight nearest point and spot lights
    /// (GameLights) brighten what's round them per pixel (Shaders/lamps.ps.hlsl): fading smoothly to nothing at the
    /// game's own reach, only on surfaces facing the light, blocked by what's between (a march through the depth
    /// buffer), the picture multiplied by one plus the light so surfaces keep their colours. A light's strength is its
    /// colour, times its intensity over ten up to one (the white light the game puts round you is 2.25: a quarter).
    /// Drawn with the other depth passes, in the game's frame before its see-through things.
    /// <para>
    /// **Every lamp within the Distance setting** (LampLightOptions.Distance, 200 m to start) is lit, up to MostLit,
    /// each easing in as it comes into range and out as it leaves (EaseLampShare) and fading over the last fifth of the
    /// distance; with shadows on, the game's own light of each is dimmed by the plugin's share (GameLampSwitch), so the
    /// two cross-fade. The first way lit only the eight nearest, and left the rest to the game, which lights only its
    /// own eight nearest with Direct3D's fixed lights: a lamp leaving those took its light off a whole tree or building
    /// at once, and back as you stepped back (the user, November 2026: "so much flickering"). Lamps without a shadow map
    /// light unshadowed, as the game's always did. Untested in the game when written.
    /// </para>
    /// </summary>
    internal sealed partial class DepthShading {
        /// <summary>How many lamps are lit at once, and drawn with those still fading out (the shader's MOST_LIT).</summary>
        private const int MostLit = 32;

        /// <summary>The sampler the lamp shader reads the frame's copy from (lamps.ps.hlsl's `frame`, s13).</summary>
        private const int FrameSampler = 13;
        private const int MostDrawn = 40;

        /// <summary>How much nearer an unlit lamp must be than a lit one to take its place, in meters.</summary>
        private const float LitStickiness = 15;

        /// <summary>How much further a lamp whose light doesn't reach into view counts, in meters (DrawLamps).</summary>
        private const float OutOfView = 1000;

        // for the lamp report (DepthShading.LampShadows.cs): lamps within the Distance setting, and those lighting
        // something in view
        private int traceInRange;
        private int traceInView;

        /// <summary>
        /// Whether a lamp's reach (a sphere round it, in view space) touches the view: tested against the four side
        /// planes and the camera's own, with room to spare (a fifth more and 2 m), so a lamp turned into view is lit a
        /// little before its light shows at the edge, and its half-second ease-in isn't seen.
        /// </summary>
        private static bool InView(Vector3 at, float reach, Matrix projection) {
            var r = reach * 1.2f + 2;

            if (at.Z < -r) {
                return false;
            }

            var sideX = (float)Math.Sqrt(projection.M11 * projection.M11 + 1);
            var sideY = (float)Math.Sqrt(projection.M22 * projection.M22 + 1);
            return projection.M11 * Math.Abs(at.X) - at.Z <= r * sideX
                && projection.M22 * Math.Abs(at.Y) - at.Z <= r * sideY;
        }

        /// <summary>
        /// Where each of the shader's inputs starts, in floats (Shaders/lamps.ps.hlsl's registers times four): places
        /// c2, colours c42, viewToWorld c82, night c85, the maps' lamps c86 and their shares c98.
        /// </summary>
        private const int PlacesAt = 2 * 4;
        private const int ColoursAt = 42 * 4;
        private const int ViewToWorldAt = 82 * 4;
        private const int NightAt = 85 * 4;
        private const int MapLampsAt = 86 * 4;
        private const int MapSharesAt = 98 * 4;
        private const int ConstantFloats = 101 * 4;

        /// <summary>How much your own character's light counts, against a lamp of the same strength.</summary>
        public static float OwnLightShare = 0.3f;

        /// <summary>How much deeper a lamp's colour is made (1 as the game has it).</summary>
        private const double Warmth = 1.6;

        /// <summary>The game's own lights reach their falloff times this (read from the client, as it reads it).</summary>
        private const int ReachFactorAt = 0x821CD4;

        private IntPtr lampShader;
        private bool loggedLamps;

        private LampLightOptions LampOptions => settings.Data.LampLight;

        private unsafe void DrawLamps(Device device, Matrix view, Matrix projection, Surface back, int width, int height) {
            var home = RenderOrigin.Landblock;
            var reachFactor = *(float*)ReachFactorAt;
            lampCopiesReady = false;
            seeThroughReady = false;

            if (reachFactor <= 0 || reachFactor > 100) {
                reachFactor = 1;
            }

            // nearest to the camera first: the game's own distance (+0xD8) was from when the list was made, a hundred
            // meters for lights twenty away (seen in the game)
            var camera = Vector3.TransformCoordinate(new Vector3(0, 0, 0), Matrix.Invert(view));
            var all = GameLights.Read(home);

            if (LampOptions.FireLight) {
                FireLights.AddTo(all);
                PlacedLights.AddTo(all, unchecked((uint)CoreManager.Current.Actions.Landcell) >> 16);
            }

            // every lamp within the Distance setting, nearest first: one holding a shadow map always (its shadow may
            // still be fading), then those lit already (LitStickiness meters nearer, so two about as near don't take
            // turns at the edge of MostLit), then the rest; past MostLit, only those still fading out. Lamps whose
            // light reaches into view go before the rest (OutOfView meters further): Hebian-to has more lamps in range
            // than MostLit, and by distance alone lamps behind you and inside houses took the places, while a lamp
            // across the square in plain view stayed dark (the user's screenshot, November 2026)
            var limit = (float)LampOptions.ClampedDistance;
            var reachSetting = LampOptions.ClampedReach / 100f;
            var inRange = all
                .Where(l => (l.Type == 0 || l.Type == 2) && l.Falloff > 0)
                .Select(l => (Light: l, Place: new Vector3(l.East, l.Height, l.North)))
                .Select(c => (c.Light, c.Place, Distance: (c.Place - camera).Length()))
                .Where(c => c.Distance < limit)
                .Select(c => (c.Light, c.Place, c.Distance, Seen: InView(Vector3.TransformCoordinate(c.Place, view),
                    c.Light.Falloff * reachFactor * reachSetting, projection)))
                .ToList();
            var candidates = inRange
                .OrderBy(c => c.Distance + (c.Seen ? 0 : OutOfView)
                    - (HoldsMap(c.Place) ? 10000 : LampShare(c.Place) > 0 ? LitStickiness : 0))
                .Take(MostDrawn)
                .ToList();
            traceInRange = inRange.Count;
            traceInView = inRange.Count(c => c.Seen);

            var now = Environment.TickCount;
            var seconds = Math.Max(0, Math.Min(0.25f, unchecked(now - lampsEasedAt) / 1000f));
            lampsEasedAt = now;

            // the light the game hangs round your own character casts no shadow: everything would be in yours
            var you = RenderOrigin.Player();
            var chosen = new List<(GameLight Light, Vector3 Place, float Share, bool Yours)>();
            var seen = new HashSet<(int, int, int)>();

            for (var i = 0; i < candidates.Count; i++) {
                var c = candidates[i];
                seen.Add(ShareKey(c.Place));
                // each lamp eases in as it comes into range and out as it leaves (EaseLampShare), and fades over the last
                // fifth of the distance: so none ever appears or vanishes at once
                var presence = EaseLampShare(c.Place, i < MostLit, seconds);
                var fade = 1 - SmoothStep(limit * 0.8f, limit, c.Distance);
                var share = presence * fade;
                // the game hangs it a few meters ahead of you and above (3.7 m off, seen at the tavern door, where taken
                // for a lamp it lit everything outside and leaked under the walls, November 2026): faint and near
                var yours = c.Light.Dynamic && c.Light.Intensity < 10 && (c.Place - you).Length() < 6;

                if (share > 0.001f || HoldsMap(c.Place)) {
                    chosen.Add((c.Light, c.Place, share, yours));
                }
            }

            // lamps out of range are forgotten, and start from nothing when they come back
            foreach (var key in lampShares.Keys.Where(k => !seen.Contains(k)).ToList()) {
                lampShares.Remove(key);
            }

            traceLamps = chosen.Count;
            traceStatic = *(int*)0x8683B4;
            traceDynamic = *(int*)0x86B838;

            if (!loggedLamps && chosen.Count > 0) {
                loggedLamps = true;
                PluginCore.LogFile($"[lamps] {chosen.Count} lights near, reach {reachFactor:0.###} times the falloff, "
                    + $"lit to {limit:0} m, up to {MostLit}");
            }

            var (night, glow) = Night();
            var replacing = LampOptions.Shadows;

            if (chosen.Count == 0 && night <= 0) {
                GameLampSwitch.Places = new (Vector3, float)[0];
                return;
            }

            // a card that takes none of the lamp shaders keeps the game's own lamps, and the rest of the shading goes on
            if (!EnsureLampShader(device)) {
                GameLampSwitch.Places = new (Vector3, float)[0];
                return;
            }

            var constants = new float[ConstantFloats];
            constants[NightAt] = night;
            constants[NightAt + 1] = glow;
            // the sky darkens a little less than the land: the night's own sky, not a black one
            constants[NightAt + 2] = 0.85f;
            // with shadows the plugin lights the lamps in the game's place (GameLampSwitch)
            constants[NightAt + 3] = replacing ? 1 : 0;
            constants[0] = projection.M33;
            constants[1] = projection.M43;
            constants[2] = projection.M11;
            constants[3] = projection.M22;
            constants[4] = LampOptions.ClampedStrength / 100f;
            constants[5] = LampOptions.Shadows ? 1 : 0;
            constants[6] = chosen.Count;

            var forShadows = new List<(Vector3 Place, float Reach, bool Casts)>();
            var places = new List<(Vector3, float)>();

            for (var i = 0; i < chosen.Count; i++) {
                var (l, place, share, yours) = chosen[i];

                if (DebugView == 5 && !loggedDebugLamps) {
                    PluginCore.LogFile($"[lamps] debug light {i}: {(l.Dynamic ? "dynamic" : "static")} cell {l.Cell:X8} at "
                        + $"{l.East:0.0} {l.North:0.0} {l.Height:0.0}, falloff {l.Falloff:0.0}, share {share:0.##}");
                }

                var at = Vector3.TransformCoordinate(place, view);
                // the light round you counts for OwnLightShare of a lamp: it lit whatever you walked up to, so a room
                // seemed to light up near its walls and go dark in the open (the user, November 2026)
                var strength = Math.Min(1, l.Intensity / 10) * (yours ? OwnLightShare : 1) * share;
                // further than the game's own by the Reach setting (not the light round you)
                var reach = l.Falloff * reachFactor * (yours ? 1 : LampOptions.ClampedReach / 100f);
                var o = PlacesAt + i * 4;
                constants[o] = at.X;
                constants[o + 1] = at.Y;
                constants[o + 2] = at.Z;
                constants[o + 3] = reach;
                // the colour deepened a little (each channel to the power Warmth): the game lit with a lamp's colour
                // straight onto the texture, warm through and through, where this adds light over the ambient, which
                // washed the warmth out of a lit character (the user, November 2026)
                var c = ColoursAt + i * 4;
                constants[c] = (float)Math.Pow(l.Red, Warmth) * strength;
                constants[c + 1] = (float)Math.Pow(l.Green, Warmth) * strength;
                constants[c + 2] = (float)Math.Pow(l.Blue, Warmth) * strength;
                constants[c + 3] = -1;
                forShadows.Add((place, reach, !yours));

                // the game's own light of it dimmed by the plugin's share, so the two cross-fade; the light round you
                // stays the game's
                if (!yours) {
                    places.Add((place, 1 - share));
                }
            }

            GameLampSwitch.Places = replacing ? places.ToArray() : new (Vector3, float)[0];
            loggedDebugLamps = DebugView == 5;

            // the shadowed lamps' cube maps (DepthShading.LampShadows.cs), which change the target, shaders and states:
            // all put back after by a state block of their own
            var which = new int[MostShadowSlots];

            for (var k = 0; k < which.Length; k++) {
                which[k] = -1;
            }

            if (LampOptions.Shadows) {
                using (var saved = new StateBlock(device, StateBlockType.All)) {
                    saved.Capture();

                    try {
                        which = DrawLampShadows(device, forShadows, you,
                            Math.Min(LampOptions.ClampedShadowedLamps, lampShaderMaps), seconds);
                    }
                    finally {
                        saved.Apply();
                    }
                }
            }

            // the frame as drawn so far, for the night to spare what's bright in it (lamps.ps.hlsl): the lamps' glass, the
            // portals, the moon, which the game draws self-lit; darkened with the rest, a lamp's glass went from white to
            // half as bright (Hebian-to's square, November 2026). By day there's nothing to spare, but Glow wants it too
            // (DepthShading.LampCopies.cs)
            var glowing = settings.Data.Image.ClampedGlow > 0 && DebugView == 0;
            device.SetTexture(FrameSampler, null);

            if (night > 0 || glowing) {
                CopyInto(device, back, width, height, ref beforeLamps, ref beforeLampsSurface);
            }

            device.SetTexture(FrameSampler, night > 0 ? beforeLamps : null);
            device.SamplerState[FrameSampler].MinFilter = TextureFilter.Point;
            device.SamplerState[FrameSampler].MagFilter = TextureFilter.Point;
            device.SamplerState[FrameSampler].MipFilter = TextureFilter.None;
            device.SamplerState[FrameSampler].AddressU = TextureAddress.Clamp;
            device.SamplerState[FrameSampler].AddressV = TextureAddress.Clamp;

            // a way in view space to the device's world, for the cube maps, which are laid out in the world
            var inverse = Matrix.Invert(view);
            constants[ViewToWorldAt] = inverse.M11;
            constants[ViewToWorldAt + 1] = inverse.M12;
            constants[ViewToWorldAt + 2] = inverse.M13;
            constants[ViewToWorldAt + 4] = inverse.M21;
            constants[ViewToWorldAt + 5] = inverse.M22;
            constants[ViewToWorldAt + 6] = inverse.M23;
            constants[ViewToWorldAt + 8] = inverse.M31;
            constants[ViewToWorldAt + 9] = inverse.M32;
            constants[ViewToWorldAt + 10] = inverse.M33;

            // each map's lamp (place in view space and reach) and how much of its shadow shows, looked up once in the
            // shader before its lights' loop; and each mapped light's map
            for (var k = 0; k < MostShadowSlots; k++) {
                var lamp = which[k];

                for (var c = 0; c < 4; c++) {
                    constants[MapLampsAt + k * 4 + c] = lamp >= 0 ? constants[PlacesAt + lamp * 4 + c] : 0;
                }

                constants[MapSharesAt + k] = lamp >= 0 ? lampShadowShares[k] : 0;

                if (lamp >= 0) {
                    constants[ColoursAt + lamp * 4 + 3] = k;
                }

                device.SetTexture(1 + k, lamp >= 0 ? lampCubes[k] : null);
                device.SamplerState[1 + k].MinFilter = TextureFilter.Point;
                device.SamplerState[1 + k].MagFilter = TextureFilter.Point;
                device.SamplerState[1 + k].MipFilter = TextureFilter.None;
                device.SamplerState[1 + k].AddressU = TextureAddress.Clamp;
                device.SamplerState[1 + k].AddressV = TextureAddress.Clamp;
                device.SamplerState[1 + k].AddressW = TextureAddress.Clamp;
            }

            var state = device.RenderState;
            state.AlphaBlendEnable = true;
            DeviceCalls.UseShaders(device, vertexShader, lampShader);

            // a test run's `depthview 4`: what the first shadowed lamp sees, in grey, over everything
            if (DebugView == 4 || DebugView == 5) {
                constants[7] = DebugView == 5 ? 3 : 2;
                state.AlphaBlendEnable = false;
                DeviceCalls.PixelConstants(device, 0, constants);
                device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 2, Quad);
                return;
            }

            // then the light: the picture times twice it, so it can darken (the night) as well as brighten (the lamps)
            constants[7] = 0;
            state.SourceBlend = Blend.DestinationColor;
            state.DestinationBlend = Blend.SourceColor;
            DeviceCalls.PixelConstants(device, 0, constants);
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 2, Quad);

            // and the frame as the lamp light left it, for Glow and, at night, for what's seen through the game's
            // see-through things (DepthShading.LampCopies.cs, DepthShading.SeeThrough.cs)
            if ((glowing || night > 0) && beforeLamps != null) {
                CopyInto(device, back, width, height, ref afterLamps, ref afterLampsSurface);
                lampCopiesReady = glowing;
                seeThroughReady = night > 0;
            }
        }

        /// <summary>
        /// The lamp shader with the most shadow maps the card takes, tried from twelve down (Shaders/build.cmd makes 12,
        /// 10, 8 and 6): a card takes a pixel shader of only so many instructions, and the twelve-map one, about 5,000,
        /// was refused (the user's, November 2026, where the old six-map one of 2,600 ran), which stopped all the
        /// shading for the session. Whether any was taken; none, and lamp light stays off for the session.
        /// </summary>
        private bool EnsureLampShader(Device device) {
            if (lampShader != IntPtr.Zero) {
                return true;
            }

            if (lampShaderFailed) {
                return false;
            }

            var slots = "?";

            try {
                slots = device.DeviceCaps.MaxPixelShader30InstructionSlots.ToString();
            }
            catch (Exception) {
                // only for the log line
            }

            foreach (var (maps, name) in new[] {
                (12, "lamps.ps.cso"), (10, "lamps10.ps.cso"), (8, "lamps8.ps.cso"), (6, "lamps6.ps.cso"),
            }) {
                try {
                    lampShader = CreateShader(device, CreatePixelShaderSlot, name);
                    lampShaderMaps = maps;
                    LampShadowMapsTaken = maps;
                    PluginCore.LogFile($"[lamps] the lamp shader with {maps} shadow maps (the card takes up to {slots} "
                        + "instruction slots a pixel shader)");
                    return true;
                }
                catch (InvalidOperationException) {
                    // too big for this card: the next smaller
                }
            }

            lampShaderFailed = true;
            PluginCore.LogFile($"[lamps] the card took none of the lamp shaders ({slots} instruction slots): lamp light "
                + "off for the session, the rest of the shading goes on");
            return false;
        }

        /// <summary>The most shadowed lamps the card's lamp shader has, for the settings page (0 before it's made).</summary>
        public static int LampShadowMapsTaken;

        private int lampShaderMaps = 12;
        private bool lampShaderFailed;

        private static float SmoothStep(float from, float to, float x) {
            var t = Math.Max(0, Math.Min(1, (x - from) / (to - from)));
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// How much darker it is now (0 by day, up to the Night setting) and the lamps' strength then. Night is the
        /// game's own: its day is a list of periods (Darktide, Dawnsong...), each marked night or not, in the client's
        /// GameTime (the pointer at 0x8EE9C8: the periods' pointers at +0x14, the current one's index at +0x74; a period's
        /// start at +4, its night flag at +8, as TimeOfDay::UnPack, 0x5A7EE0, reads them). The sun's height was tried
        /// first and did nothing: at midnight the game's light vector is still well up (the moon), and the character
        /// even cast a sun shadow (seen in the game, November 2026). Eased in and out over NightEaseSeconds, so the
        /// change of period at dusk and dawn doesn't snap. Outdoors only: a dungeon or a building keeps the game's own
        /// light.
        /// </summary>
        private unsafe (float Night, float Glow) Night() {
            var now = Environment.TickCount;
            var seconds = Math.Max(0, Math.Min(1, unchecked(now - nightEasedAt) / 1000f));
            nightEasedAt = now;
            var isNight = GameIsNight();

            if (isNight != loggedNight) {
                loggedNight = isNight;
                PluginCore.LogFile($"[lamps] the game's period is now {(isNight ? "a night" : "a day")} one");
            }

            var target = isNight ? 1f : 0f;
            var step = seconds / NightEaseSeconds;
            nightLevel = target > nightLevel ? Math.Min(target, nightLevel + step) : Math.Max(target, nightLevel - step);

            var darkness = LampOptions.ClampedNightDarkness / 100f;
            var cell = unchecked((uint)CoreManager.Current.Actions.Landcell);

            if (darkness <= 0 || (cell & 0xFFFF) >= 0x100) {
                return (0, 1);
            }

            var nightness = nightLevel * nightLevel * (3 - 2 * nightLevel);
            return (darkness * nightness, 1 + (LampOptions.ClampedNightGlow / 100f - 1) * nightness);
        }

        private const float NightEaseSeconds = 20;
        private float nightLevel;
        private bool loggedDebugLamps;
        private bool? loggedNight;
        private int nightEasedAt = Environment.TickCount;
        private int lampsEasedAt = Environment.TickCount;

        /// <summary>Whether the game's current period of the day is a night one.</summary>
        private static unsafe bool GameIsNight() {
            var time = *(byte**)0x8EE9C8;

            if (time == null) {
                return false;
            }

            var periods = *(byte***)(time + 0x14);
            var index = *(int*)(time + 0x74);

            if (periods == null || index < 0 || index > 64 || periods[index] == null) {
                return false;
            }

            return *(int*)(periods[index] + 8) != 0;
        }

        private void ReleaseLampShader() {
            Release(lampShader);
            lampShader = IntPtr.Zero;
        }
    }
}
