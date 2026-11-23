// The shading's grey (occlusion and sun shadows, drawn by shading.ps.hlsl into a texture of its own) averaged over each
// 4 by 4 block of pixels and multiplied onto the picture (Ui/DepthShading.ShadeBlur.cs). The shading turns its samples
// one of 16 ways by a pixel's place in such a block, so the average has every turn and the noise goes; a neighbour much
// nearer or further than the pixel (another surface: a character against the wall behind) counts for nothing, so no
// shading bleeds across an edge. Compiled by Shaders/build.cmd.
sampler depthBuffer : register(s0);
sampler shade : register(s4);

float4 projection : register(c0);  // M33, M43, M11, M22
float4 screen : register(c3);      // width, height, 1/width, 1/height

float ViewZ(float d) {
    return projection.y / (min(d, 0.9999) - projection.x);
}

float4 main(float2 uv : TEXCOORD0) : COLOR {
    float d = tex2Dlod(depthBuffer, float4(uv, 0, 0)).r;

    if (d >= 0.99999) {
        return float4(1, 1, 1, 1);
    }

    float z = ViewZ(d);
    // as near as a surface's own neighbours are: 5 cm, and more with distance, where a pixel spans more
    float allowance = 0.05 + z * 0.03;
    float sum = 0;
    float weight = 0;

    [unroll]
    for (int y = -2; y < 2; y++) {
        [unroll]
        for (int x = -2; x < 2; x++) {
            float2 at = uv + float2(x, y) * screen.zw;
            float near = ViewZ(tex2Dlod(depthBuffer, float4(at, 0, 0)).r);
            float w = saturate(1 - abs(near - z) / allowance);
            sum += tex2Dlod(shade, float4(at, 0, 0)).r * w;
            weight += w;
        }
    }

    float light = sum / max(weight, 0.0001);
    return float4(light, light, light, 1);
}
