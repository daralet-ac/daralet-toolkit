using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Water reflections (the user's call, November 2026: screen-space reflections, each piece its own switch, Feed/
    /// WaterOptions.cs): Shaders/water.ps.hlsl, drawn before the shading pass, blended over the game's water by alpha.
    /// <para>
    /// It needs the frame's colours to reflect: a render-target texture the screen's size, copied from the back buffer
    /// each frame (StretchRect) before the pass. It's in the default pool, so it goes with the depth texture before a
    /// device reset (TearDownWater, from TearDown). Which pixels are water: Feed/WaterMap.cs, a value a terrain vertex
    /// over the blocks within WaterRadius of yours, read from the cell dat when you cross into another landblock and
    /// kept in a small L8 texture (managed pool), filtered between vertices. Indoors there's none.
    /// </para>
    /// Untested in the game when written.
    /// </summary>
    internal sealed partial class DepthShading {
        /// <summary>Landblocks round yours that reflections reach: 4, 768 meters, past which water is small anyway.</summary>
        private const int WaterRadius = 4;

        private IntPtr waterShader;
        private Texture frameCopy;
        private Surface frameCopySurface;
        private Texture waterMapTexture;
        private uint waterMapHome = uint.MaxValue;
        private int waterVertices;

        private WaterOptions Water => settings.Data.Water;

        private unsafe void DrawWater(Device device, Surface back, Matrix view, Matrix projection, int width, int height) {
            // outdoors, or in a building's room that sees out
            if (!OutdoorView.Now) {
                return;
            }

            if (!EnsureWaterMap(device, RenderOrigin.Landblock) || waterVertices == 0) {
                return;
            }

            CopyFrame(device, back, width, height);

            var options = Water;
            var inverse = Matrix.Invert(view);
            var side = WaterMap.Side(WaterRadius);
            var seconds = (Environment.TickCount & 0x3FFFFFFF) % 3600000 / 1000f;

            // toward the sun in the device's world (x, height, y), from the client's own light, and how far it's up
            var toSun = new Vector3(*(float*)0x8682C8, *(float*)0x8682D0, *(float*)0x8682CC);
            var sunUp = toSun.LengthSq() > 1e-6f ? toSun.Y / toSun.Length() : 0;

            if (toSun.LengthSq() > 1e-6f) {
                toSun.Normalize();
            }

            var sunColour = (*(float*)0x8682BC + *(float*)0x8682C0 + *(float*)0x8682C4) / 3;
            var fog = device.RenderState.FogColor;

            var state = device.RenderState;
            state.AlphaBlendEnable = true;
            state.SourceBlend = Blend.SourceAlpha;
            state.DestinationBlend = Blend.InvSourceAlpha;

            device.SetTexture(1, frameCopy);
            device.SamplerState[1].MinFilter = TextureFilter.Linear;
            device.SamplerState[1].MagFilter = TextureFilter.Linear;
            device.SamplerState[1].MipFilter = TextureFilter.None;
            device.SamplerState[1].AddressU = TextureAddress.Clamp;
            device.SamplerState[1].AddressV = TextureAddress.Clamp;
            device.SetTexture(2, waterMapTexture);
            device.SamplerState[2].MinFilter = TextureFilter.Linear;
            device.SamplerState[2].MagFilter = TextureFilter.Linear;
            device.SamplerState[2].MipFilter = TextureFilter.None;
            device.SamplerState[2].AddressU = TextureAddress.Clamp;
            device.SamplerState[2].AddressV = TextureAddress.Clamp;

            var com = Com(device);
            Slot<SetShaderCall>(device, SetPixelShaderSlot)(com, waterShader);
            Slot<SetConstantsCall>(device, SetPixelShaderConstantSlot)(com, 0, new[] {
                projection.M33, projection.M43, projection.M11, projection.M22,
                inverse.M11, inverse.M12, inverse.M13, inverse.M14,
                inverse.M21, inverse.M22, inverse.M23, inverse.M24,
                inverse.M31, inverse.M32, inverse.M33, inverse.M34,
                inverse.M41, inverse.M42, inverse.M43, inverse.M44,
                view.M11, view.M12, view.M13, 0,
                view.M21, view.M22, view.M23, 0,
                view.M31, view.M32, view.M33, 0,
                side, WaterRadius * WaterMap.VerticesPerBlock, 1f / side, seconds,
                options.ClampedStrength / 100f, options.ClampedRippleSize / 100f, Flag(options.Ripples),
                Flag(options.Fresnel),
                Flag(options.Glints), Flag(options.Blur), Flag(options.SoftShore), 150,
                toSun.X, toSun.Y, toSun.Z, sunUp > 0.02f ? Math.Min(1, sunUp * 4) : 0,
                fog.R / 255f, fog.G / 255f, fog.B / 255f, sunColour,
                width, height, 1f / width, 1f / height,
            }, 14);
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 2, Quad);
        }

        private static float Flag(bool on) {
            return on ? 1 : 0;
        }

        /// <summary>The water map round home, made again when you've crossed into another landblock.</summary>
        private bool EnsureWaterMap(Device device, uint home) {
            if (home == waterMapHome && waterMapTexture != null) {
                return true;
            }

            // not while the game loads the place (DrawDistance.QuietNow): nothing of the plugin's is read or made then
            if (DrawDistance.QuietNow) {
                return false;
            }

            var cells = UBService.CellDat;

            if (cells == null) {
                return false;
            }

            var map = WaterMap.Build(home, WaterRadius, block => {
                var land = cells.ReadFromDat<ACE.DatLoader.FileTypes.CellLandblock>(block << 16 | 0xFFFF);
                return land?.Terrain;
            });

            var side = WaterMap.Side(WaterRadius);
            waterMapTexture?.Dispose();
            waterMapTexture = new Texture(device, side, side, 1, Usage.None, Format.L8, Pool.Managed);
            var stream = waterMapTexture.LockRectangle(0, LockFlags.None, out var pitch);

            try {
                for (var row = 0; row < side; row++) {
                    Marshal.Copy(map, row * side, stream.InternalData + row * pitch, side);
                }
            }
            finally {
                waterMapTexture.UnlockRectangle(0);
            }

            waterVertices = 0;

            foreach (var value in map) {
                if (value != 0) {
                    waterVertices++;
                }
            }

            waterMapHome = home;
            PluginCore.LogFile($"[water] map round {home:X4}: {waterVertices} water vertices of {side * side}");
            return true;
        }

        /// <summary>The frame copy let go (a reset, or off), and the water map with it.</summary>
        private void TearDownWater() {
            frameCopySurface?.Dispose();
            frameCopySurface = null;
            frameCopy?.Dispose();
            frameCopy = null;
            waterMapTexture?.Dispose();
            waterMapTexture = null;
            waterMapHome = uint.MaxValue;
        }
    }
}
