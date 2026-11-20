using System;
using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Far trees (DrawDistanceOptions.FarTrees, the user's call, November 2026): the trees in the first rings past the
    /// real scenery drawn by the plugin as the game's own 3D models, where the billboards would be. The game draws each
    /// real tree's parts one call at a time, about a microsecond each: real trees out to 3 landblocks were about 5,100
    /// more calls a frame than out to 2, 94 fps to 64 at the Holtburg overlook (seen in the game). These are drawn with
    /// hardware instancing: each model's geometry once (FarTreeModels), every tree of that model a 32-byte instance
    /// (FarTreeInstance: place, height, size, turn) in one buffer, and one DrawIndexedPrimitive a model and texture.
    /// <para>
    /// Same objects as the billboards (SceneryLayout's, at the density in use, stood on the ground the game draws, as
    /// BuildBillboards stands them), lit the same (the game's ambient and its share of the sun, by strength), drawn in
    /// the game's frame before its see-through things (WorldPass) with the device's camera. Shaders (fartree.vs.hlsl,
    /// fartree.ps.hlsl) do the placing, the light, the cut out leaves and the game's linear fog, which the device gives
    /// shaders none of: its start, end and colour are read from the device as the game left them. Stream 0 is the
    /// model (0x40000000 | copies), stream 1 the instances (0x80000000 | 1), both put back after. `[far-trees]` lines in
    /// log.txt. Untested in the game when written.
    /// </para>
    /// </summary>
    internal sealed partial class DrawDistance {
        private const int InstanceSize = 32;

        private readonly FarTreeModels farTreeModels = new FarTreeModels();
        /// <summary>Runs of the instance buffer by model; Far for those on the far land, drawn in its pass.</summary>
        private readonly List<(FarTreeModel Model, int First, int Count, bool Far)> farTreeBatches =
            new List<(FarTreeModel, int, int, bool)>();

        private VertexBuffer farTreeInstances;
        private VertexDeclaration farTreeDeclaration;
        private IntPtr farTreeVertexShader;
        private IntPtr farTreePixelShader;
        private int farTreeCount;

        /// <summary>Whether a block's trees are far trees: within the rings set past the real scenery.</summary>
        private bool IsFarTreeRing(int ring) {
            var rings = Options.ClampedFarTrees;
            return rings > 0 && ring <= TreeRing + rings;
        }

        /// <summary>The far trees' instances, one buffer, in runs by model (from BuildBillboards).</summary>
        private void BuildFarTrees(Device device, Dictionary<FarTreeModel, List<FarTreeInstance>> byModel,
            Dictionary<FarTreeModel, List<FarTreeInstance>> onFarLand) {
            ClearFarTrees();
            var all = new List<FarTreeInstance>();

            foreach (var kv in byModel) {
                farTreeBatches.Add((kv.Key, all.Count, kv.Value.Count, false));
                all.AddRange(kv.Value);
            }

            foreach (var kv in onFarLand) {
                farTreeBatches.Add((kv.Key, all.Count, kv.Value.Count, true));
                all.AddRange(kv.Value);
            }

            if (all.Count == 0) {
                return;
            }

            farTreeInstances = new VertexBuffer(typeof(FarTreeInstance), all.Count, device, Usage.WriteOnly,
                VertexFormats.None, ScenePool.Buffers);
            farTreeInstances.SetData(all.ToArray(), 0, LockFlags.None);
            farTreeCount = all.Count;
            var triangles = farTreeBatches.Sum(b => (long)b.Count * b.Model.Parts.Sum(p => p.Triangles));
            PluginCore.LogFile($"[far-trees] {all.Count} trees of {farTreeBatches.Count} models in rings "
                + $"{TreeRing + 1} to {TreeRing + Options.ClampedFarTrees}, {triangles} triangles, "
                + $"{FreeMegabytes()} MB free");
        }

        /// <summary>One far tree from a placed object, in the device's world (east, height, north).</summary>
        private static FarTreeInstance FarTree(float east, float north, float height, float yaw, float scale) {
            return new FarTreeInstance {
                East = east, Height = height, North = north, Scale = scale,
                Cos = (float)Math.Cos(yaw), Sin = (float)Math.Sin(yaw),
            };
        }

        /// <summary>
        /// The far trees (and far buildings) with the device's camera, or those on the far land with its camera
        /// (projection given, from FarLand's pass).
        /// </summary>
        private unsafe void DrawFarTrees(Device device, Matrix shift, Matrix? projection = null) {
            var onFarLand = projection.HasValue;

            if (farTreeInstances == null || !farTreeBatches.Any(b => b.Far == onFarLand)) {
                return;
            }

            if (farTreeVertexShader == IntPtr.Zero) {
                farTreeVertexShader = DeviceCalls.VertexShader(device, "fartree.vs.cso");
                farTreePixelShader = DeviceCalls.PixelShader(device, "fartree.ps.cso");
                farTreeDeclaration = new VertexDeclaration(device, new[] {
                    new VertexElement(0, 0, DeclarationType.Float3, DeclarationMethod.Default,
                        DeclarationUsage.Position, 0),
                    new VertexElement(0, 12, DeclarationType.Float2, DeclarationMethod.Default,
                        DeclarationUsage.TextureCoordinate, 0),
                    new VertexElement(1, 0, DeclarationType.Float4, DeclarationMethod.Default,
                        DeclarationUsage.TextureCoordinate, 1),
                    new VertexElement(1, 16, DeclarationType.Float4, DeclarationMethod.Default,
                        DeclarationUsage.TextureCoordinate, 2),
                    // D3DDECL_END, written out: VertexElement.VertexDeclarationEnd needs Microsoft.VisualC to compile
                    new VertexElement(0xFF, 0, DeclarationType.Unused, DeclarationMethod.Default,
                        DeclarationUsage.Position, 0),
                });
            }

            // the game's fog as it left the device, before anything here changes it
            var state = device.RenderState;
            var fogOn = state.FogEnable;
            var fogStart = state.FogStart;
            var fogEnd = state.FogEnd;
            var fogColour = state.FogColor;

            // the game's light for outdoor objects, as the billboards have it (DrawDistance.Lighting.cs)
            float F(uint at) => *(float*)at;
            var sx = F(0x8682C8);
            var sy = F(0x8682CC);
            var sz = F(0x8682D0);
            var share = SunShare * (float)Math.Sqrt(sx * sx + sy * sy + sz * sz);
            var light = new[] {
                F(0x8682B0) + F(0x8682BC) * share, F(0x8682B4) + F(0x8682C0) * share,
                F(0x8682B8) + F(0x8682C4) * share, 0,
                fogColour.R / 255f, fogColour.G / 255f, fogColour.B / 255f, 0,
            };
            var m = shift * device.Transform.View * (projection ?? device.Transform.Projection);

            // made and let go each frame: a state block alive at a device reset hangs the game
            using (var saved = new StateBlock(device, StateBlockType.All)) {
                saved.Capture();

                try {
                    state.ZBufferEnable = true;
                    state.ZBufferWriteEnable = true;
                    state.AlphaBlendEnable = false;
                    state.AlphaTestEnable = false;
                    state.CullMode = Cull.None;
                    state.Lighting = false;
                    state.FogEnable = false;
                    device.SamplerState[0].MinFilter = TextureFilter.Linear;
                    device.SamplerState[0].MagFilter = TextureFilter.Linear;
                    device.SamplerState[0].MipFilter = TextureFilter.Linear;
                    device.SamplerState[0].AddressU = TextureAddress.Wrap;
                    device.SamplerState[0].AddressV = TextureAddress.Wrap;
                    device.VertexDeclaration = farTreeDeclaration;
                    DeviceCalls.UseShaders(device, farTreeVertexShader, farTreePixelShader);
                    DeviceCalls.VertexConstants(device, 0, new[] {
                        m.M11, m.M12, m.M13, m.M14,
                        m.M21, m.M22, m.M23, m.M24,
                        m.M31, m.M32, m.M33, m.M34,
                        m.M41, m.M42, m.M43, m.M44,
                        fogStart, fogEnd, fogOn ? 1 : 0, 0,
                    });

                    foreach (var batch in farTreeBatches) {
                        if (batch.Far != onFarLand) {
                            continue;
                        }

                        device.SetStreamSource(0, batch.Model.Vertices, 0, 20);
                        device.SetStreamSource(1, farTreeInstances, batch.First * InstanceSize, InstanceSize);
                        DeviceCalls.StreamFrequency(device, 0, 0x40000000 | batch.Count);
                        DeviceCalls.StreamFrequency(device, 1, unchecked((int)0x80000000) | 1);
                        device.Indices = batch.Model.Indices;

                        foreach (var part in batch.Model.Parts) {
                            light[3] = part.CutOut ? 1 : 0;
                            DeviceCalls.PixelConstants(device, 0, light);
                            device.SetTexture(0, part.Texture);
                            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.Model.VertexCount,
                                part.StartIndex, part.Triangles);
                        }
                    }
                }
                finally {
                    DeviceCalls.StreamFrequency(device, 0, 1);
                    DeviceCalls.StreamFrequency(device, 1, 1);
                    device.SetStreamSource(1, null, 0, 0);
                    DeviceCalls.UseShaders(device, IntPtr.Zero, IntPtr.Zero);
                    device.SetTexture(0, null);
                    saved.Apply();
                }
            }
        }

        private void ClearFarTrees() {
            farTreeInstances?.Dispose();
            farTreeInstances = null;
            farTreeBatches.Clear();
            farTreeCount = 0;
        }

        private void DisposeFarTrees() {
            ClearFarTrees();
            farTreeModels.Dispose();
            farTreeDeclaration?.Dispose();
            farTreeDeclaration = null;
            DeviceCalls.Release(farTreeVertexShader);
            DeviceCalls.Release(farTreePixelShader);
            farTreeVertexShader = IntPtr.Zero;
            farTreePixelShader = IntPtr.Zero;
        }
    }
}
