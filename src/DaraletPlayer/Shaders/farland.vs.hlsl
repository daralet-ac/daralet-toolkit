// Far land (Ui/FarLand.cs): the ground past the game's landscape, its vertices in the device's world (x east, height,
// y north) from the far land's own landblock, moved into the camera's by the matrix. Lit as the game's ground (its ambient
// plus the sun on the ground's normal, times the grass's GroundLight), and fogged with the game's linear fog. Its camera
// is the game's with the far plane pushed out, so the land past 3.3 km isn't clipped. Hands on the light, the vertex's
// terrain colour (for while the atlas isn't ready) and its place in the atlas (FarLand.Textures.cs).
// Compiled by Shaders/build.cmd.
float4 viewProjection[4] : register(c0);  // rows
float4 fog : register(c4);                // start, end, on (1 or 0)
float4 ambient : register(c5);            // rgb
float4 sunColour : register(c6);          // rgb
float4 sun : register(c7);                // toward the sun in the device's axes, its length the sun's strength

struct Input {
    float3 position : POSITION;
    float3 normal : NORMAL;
    float4 colour : COLOR0;
    float2 uv : TEXCOORD0;
};

struct Output {
    float4 position : POSITION;
    float4 light : COLOR0;
    float4 colour : COLOR1;
    float2 uv : TEXCOORD0;
    float fogged : TEXCOORD1;
};

Output main(Input input) {
    float3 p = input.position;

    Output o;
    o.position = p.x * viewProjection[0] + p.y * viewProjection[1] + p.z * viewProjection[2] + viewProjection[3];
    // COLOR outputs are held to 0..1, so the light goes at half and is doubled in the pixel shader
    float3 light = ambient.rgb + sunColour.rgb * max(0, dot(normalize(input.normal), sun.xyz));
    o.light = float4(saturate(light * 0.5), 1);
    o.colour = input.colour;
    o.uv = input.uv;
    o.fogged = fog.z > 0.5 ? saturate((fog.y - o.position.w) / max(fog.y - fog.x, 1)) : 1;
    return o;
}
