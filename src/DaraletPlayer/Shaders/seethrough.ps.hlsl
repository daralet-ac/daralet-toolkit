// What's seen through the game's see-through things given back its look from before the night (Ui/DepthShading.
// SeeThrough.cs). The lamp pass darkens the frame for night before the game draws its see-through things, so a crystal,
// a lifestone or a halo was drawn over ground already darkened, and showed it: Hebian-to's crystals went faint and
// see-through, the dark behind them showing (the user's screenshots, November 2026; the night off, they were solid).
// Where the finished frame differs from the copy right after the lamp pass, something was drawn over the pixel since,
// and the night's change to what's behind it (before less after) is put back, as much as it differs. Compiled by
// Shaders/build.cmd.
sampler frame : register(s1);        // the finished frame
sampler beforeLamps : register(s2);  // the frame just before the lamp pass
sampler afterLamps : register(s3);   // and just after it

float4 settings : register(c0);  // how quickly a difference counts as drawn over

float4 main(float2 uv : TEXCOORD0) : COLOR {
    float3 finished = tex2Dlod(frame, float4(uv, 0, 0)).rgb;
    float3 before = tex2Dlod(beforeLamps, float4(uv, 0, 0)).rgb;
    float3 after = tex2Dlod(afterLamps, float4(uv, 0, 0)).rgb;
    float drawnOver = saturate(length(finished - after) * settings.x);
    return float4(saturate(finished + (before - after) * drawnOver), 1);
}
