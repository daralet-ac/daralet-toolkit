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
    /// Drawn with the other depth passes, in the game's frame before its see-through things. Untested in the game when
    /// written.
    /// </summary>
    internal sealed partial class DepthShading {
        private const int MostLamps = 8;

        /// <summary>How much your own character's light counts, against a lamp of the same strength.</summary>
        public static float OwnLightShare = 0.3f;

        /// <summary>How much deeper a lamp's colour is made (1 as the game has it).</summary>
        private const double Warmth = 1.6;

        /// <summary>The game's own lights reach their falloff times this (read from the client, as it reads it).</summary>
        private const int ReachFactorAt = 0x821CD4;

        private IntPtr lampShader;
        private bool loggedLamps;

        private LampLightOptions LampOptions => settings.Data.LampLight;

        private unsafe void DrawLamps(Device device, Matrix view, Matrix projection) {
            var home = RenderOrigin.Landblock;
            var reachFactor = *(float*)ReachFactorAt;

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

            // a lamp holding a shadow map always among them (it may still be fading out of the plugin's hands), then the
            // nearest
            var chosen = all
                .Where(l => (l.Type == 0 || l.Type == 2) && l.Falloff > 0)
                .Select(l => (Light: l, Distance: (new Vector3(l.East, l.Height, l.North) - camera).Length()))
                .Where(c => c.Distance < 150)
                .OrderBy(c => c.Distance - (HoldsMap(new Vector3(c.Light.East, c.Light.Height, c.Light.North)) ? 1000 : 0))
                .Select(c => c.Light)
                .Take(MostLamps)
                .ToList();

            traceLamps = chosen.Count;
            traceStatic = *(int*)0x8683B4;
            traceDynamic = *(int*)0x86B838;

            if (!loggedLamps && chosen.Count > 0) {
                loggedLamps = true;
                PluginCore.LogFile($"[lamps] {chosen.Count} lights near, reach {reachFactor:0.###} times the falloff");
            }

            var (night, glow) = Night();

            if (chosen.Count == 0 && night <= 0) {
                GameLampSwitch.Places = new (Vector3, float)[0];
                return;
            }

            var constants = new float[4 + 4 + MostLamps * 8 + 16 + 4 + 4 + 6 * 4];
            var nightAt = 4 + 4 + MostLamps * 8 + 16;
            constants[nightAt] = night;
            constants[nightAt + 1] = glow;
            // the sky darkens a little less than the land: the night's own sky, not a black one
            constants[nightAt + 2] = 0.85f;
            // with shadows the plugin lights the lamps in the game's place: only what a lamp's map says it sees
            // (GameLampSwitch). It was a switch of its own, "In the game's place", until November 2026
            var replacing = LampOptions.Shadows;
            constants[nightAt + 3] = replacing ? 1 : 0;
            constants[0] = projection.M33;
            constants[1] = projection.M43;
            constants[2] = projection.M11;
            constants[3] = projection.M22;
            constants[4] = LampOptions.ClampedStrength / 100f;
            constants[5] = LampOptions.Shadows ? 1 : 0;
            constants[6] = chosen.Count;

            // the light the game hangs round your own character casts no shadow: everything would be in yours
            var you = RenderOrigin.Player();

            var forShadows = new List<(Vector3 Place, float Reach, bool Casts)>();

            for (var i = 0; i < chosen.Count; i++) {
                var l = chosen[i];

                if (DebugView == 5 && !loggedDebugLamps) {
                    PluginCore.LogFile($"[lamps] debug light {i}: {(l.Dynamic ? "dynamic" : "static")} cell {l.Cell:X8} at "
                        + $"{l.East:0.0} {l.North:0.0} {l.Height:0.0}, falloff {l.Falloff:0.0}");
                }
                var at = Vector3.TransformCoordinate(new Vector3(l.East, l.Height, l.North), view);
                var place = new Vector3(l.East, l.Height, l.North);
                // the game hangs it a few meters ahead of you and above (3.7 m off, seen at the tavern door, where taken
                // for a lamp it lit everything outside and leaked under the walls, November 2026): faint and near
                var yours = l.Dynamic && l.Intensity < 10 && (place - you).Length() < 6;
                // the light the game hangs round your own character lit whatever you walked up to, so a room seemed to light
                // up near its walls and go dark in the open (the user, November 2026): it counts for OwnLightShare of a lamp
                var strength = Math.Min(1, l.Intensity / 10) * (yours ? OwnLightShare : 1);
                // further than the game's own by the Reach setting (not the light round you)
                var reach = l.Falloff * reachFactor * (yours ? 1 : LampOptions.ClampedReach / 100f);
                var o = 8 + i * 8;
                constants[o] = at.X;
                constants[o + 1] = at.Y;
                constants[o + 2] = at.Z;
                constants[o + 3] = reach;
                // the colour deepened a little (each channel to the power Warmth): the game lit with a lamp's colour
                // straight onto the texture, warm through and through, where this adds light over the ambient, which
                // washed the warmth out of a lit character (the user, November 2026)
                constants[o + 4] = (float)Math.Pow(l.Red, Warmth) * strength;
                constants[o + 5] = (float)Math.Pow(l.Green, Warmth) * strength;
                constants[o + 6] = (float)Math.Pow(l.Blue, Warmth) * strength;
                constants[o + 7] = yours ? 0 : 1;
                forShadows.Add((place, reach, constants[o + 7] > 0.5f));
            }

            loggedDebugLamps = DebugView == 5;

            if (lampShader == IntPtr.Zero) {
                lampShader = CreateShader(device, CreatePixelShaderSlot, "lamps.ps.cso");
            }

            // the two nearest lamps' cube shadow maps (DepthShading.LampShadows.cs), which change the target, shaders and
            // states: all put back after by a state block of their own
            var which = new[] { -1f, -1f, -1f, -1f, -1f, -1f };

            if (LampOptions.Shadows) {
                using (var saved = new StateBlock(device, StateBlockType.All)) {
                    saved.Capture();

                    try {
                        which = DrawLampShadows(device, forShadows, you);
                    }
                    finally {
                        saved.Apply();
                    }
                }
            }

            // each mapped lamp's light eased between the game and the plugin (EaseLampShare): the plugin's colour times its
            // share here, the game's own dimmed by the rest (GameLampSwitch); the lamps without a map are the game's
            var now = Environment.TickCount;
            var seconds = Math.Max(0, Math.Min(0.25f, unchecked(now - lampsEasedAt) / 1000f));
            lampsEasedAt = now;
            var places = new List<(Vector3, float)>();

            foreach (var w in which) {
                if (w < 0) {
                    continue;
                }

                var i = (int)w;
                var share = EaseLampShare(forShadows[i].Place, wantedLamps.Contains(i), seconds);
                var o = 8 + i * 8;
                constants[o + 4] *= share;
                constants[o + 5] *= share;
                constants[o + 6] *= share;
                places.Add((forShadows[i].Place, 1 - share));
            }

            GameLampSwitch.Places = places.ToArray();

            // a way in view space to the device's world, for the cube maps, which are laid out in the world
            var inverse = Matrix.Invert(view);
            var rest = 8 + MostLamps * 8;
            constants[rest] = inverse.M11;
            constants[rest + 1] = inverse.M12;
            constants[rest + 2] = inverse.M13;
            constants[rest + 4] = inverse.M21;
            constants[rest + 5] = inverse.M22;
            constants[rest + 6] = inverse.M23;
            constants[rest + 8] = inverse.M31;
            constants[rest + 9] = inverse.M32;
            constants[rest + 10] = inverse.M33;

            for (var k = 0; k < 4; k++) {
                constants[rest + 12 + k] = which[k];
            }

            constants[nightAt + 4] = which[4];
            constants[nightAt + 5] = which[5];

            // each map's lamp (place in view space and reach), looked up once in the shader before its lights' loop
            for (var k = 0; k < which.Length; k++) {
                var lamp = (int)which[k];

                for (var c = 0; c < 4; c++) {
                    constants[nightAt + 8 + k * 4 + c] = lamp >= 0 ? constants[8 + lamp * 8 + c] : 0;
                }
            }

            for (var k = 0; k < which.Length; k++) {
                device.SetTexture(1 + k, which[k] >= 0 ? lampCubes[k] : null);
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
