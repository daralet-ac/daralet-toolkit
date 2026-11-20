// Far land (Ui/FarLand.cs): the ground's picture from the atlas (FarLand.Textures.cs), or its vertex colour while the atlas
// isn't ready, times the light, then the game's fog. Compiled by Shaders/build.cmd.
sampler atlas : register(s0);

float4 fogColour : register(c0); // rgb, textured (1 or 0)
float4 debug : register(c1);     // tinted red (1 or 0), for the test step farlanddebug: where the plugin's land is

float4 main(float4 light : COLOR0, float4 colour : COLOR1, float2 uv : TEXCOORD0, float fogged : TEXCOORD1) : COLOR {
    float3 ground = fogColour.w > 0.5 ? tex2D(atlas, uv).rgb : colour.rgb;
    float3 lit = lerp(fogColour.rgb, ground * light.rgb * 2, fogged);
    return float4(debug.x > 0.5 ? lerp(lit, float3(1, 0, 0), 0.5) : lit, 1);
}
