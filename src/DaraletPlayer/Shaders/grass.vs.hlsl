// Grass and reeds (Ui/Grass.cs): each clump's crossed pictures. A chunk's vertices are in its landblock's meters (x
// east, height, y north), moved into the device's world by the chunk's offset. Toward the edge of the grass's radius a
// clump sinks into the ground and thins (its alpha, against the pixel shader's cut-out), so none pop in or out as you
// walk; past Thin's distance only a share of the clumps are drawn (Ui/Grass.cs draws a chunk's lowest ranks), and a
// clump whose rank is near the share's edge sinks the same way, so none pop as the share changes. With wind on, a
// blade's top sways and its base stays put (the second texture coordinate: how far up this vertex is, in meters, and
// the clump's phase). Lit as the ground is: the game's ambient and the sun on ground facing up (Ui/Grass.cs scales
// both). The colour's alpha is the clump's rank, 0 to 1. Compiled by Shaders/build.cmd.
float4 viewProjection[4] : register(c0);  // rows
float4 eye : register(c4);                // your place (xyz), the radius (m)
float4 wind : register(c5);               // seconds, strength (0 none), direction x, direction z
float4 ambient : register(c6);            // the game's ambient (rgb)
float4 sun : register(c7);                // the sun's share (rgb)
float4 offset : register(c8);             // the chunk's landblock in the device's world (x, 0, z)
float4 thin : register(c9);               // full to (m), how quickly the share falls past it, the edge's width

struct Input {
    float3 position : POSITION;
    float4 colour : COLOR0;
    float2 uv : TEXCOORD0;
    float2 sway : TEXCOORD1;
};

struct Output {
    float4 position : POSITION;
    float4 colour : COLOR0;
    float2 uv : TEXCOORD0;
};

Output main(Input input) {
    float3 p = input.position + offset.xyz;
    float distance = length(p.xz - eye.xz);
    float fade = saturate((eye.w - distance) / (eye.w * 0.3));

    // the share of clumps drawn this far off, and this one's place against it
    float share = saturate(pow(thin.x / max(distance, thin.x), thin.y));
    fade *= saturate((share + thin.z - input.colour.a) / thin.z);

    // into the ground toward the edge
    p.y -= input.sway.x * (1 - fade);

    // two waves of wind, so it never looks like one swing
    float bend = sin(wind.x * 1.7 + input.sway.y + p.x * 0.35 + p.z * 0.27) * 0.6
        + sin(wind.x * 2.9 + input.sway.y * 1.3) * 0.25;
    p.xz += wind.zw * bend * wind.y * input.sway.x * 0.25;

    Output o;
    o.position = p.x * viewProjection[0] + p.y * viewProjection[1] + p.z * viewProjection[2] + viewProjection[3];
    o.colour = float4(input.colour.rgb * min(ambient.rgb + sun.rgb, 2), fade);
    o.uv = input.uv;
    return o;
}
