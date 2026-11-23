// Glow (Ui/DepthShading.Glow.cs), its first pass: what glows, a quarter the screen's size. A glowing thing in Asheron's
// Call (a fire, a lantern, the lifestone's crystal, a portal) is bright and strongly coloured; sunlit armour and clouds
// are bright and nearly white, and don't glow. So a pixel glows by how far its brightest channel is past the threshold,
// times how saturated it is. Four bilinear samples a full-size texel apart cover the 4 by 4 pixels this one stands for.
// Compiled by Shaders/build.cmd.
sampler frame : register(s1);
// the frame before the lamp pass and right after it, inside the game's frame (Ui/DepthShading.LampCopies.cs)
sampler beforeLamps : register(s2);
sampler afterLamps : register(s3);

float4 settings : register(c0);  // threshold (0-1), 1 / full width, 1 / full height, the lamp pass's copies there (1)

float3 Glowing(float2 uv) {
    float3 c = tex2D(frame, uv).rgb;
    // what the plugin's lamp light made bright doesn't glow: a house wall a lamp lit bright orange bloomed whole (the
    // user's screenshot, November 2026). Where nothing was drawn over a pixel since the lamp pass, it glows as it looked
    // before the lamp light; where something was (a see-through crystal, a lifestone, a halo), as it ends up
    float3 before = tex2D(beforeLamps, uv).rgb;
    float3 after = tex2D(afterLamps, uv).rgb;
    float drawnOver = saturate(length(c - after) * 8);
    c = lerp(c, lerp(before, c, drawnOver), settings.w);
    float top = max(c.r, max(c.g, c.b));
    float bottom = min(c.r, min(c.g, c.b));
    float saturation = top > 0.001 ? (top - bottom) / top : 0;
    float bright = saturate((top - settings.x) / (1 - settings.x));
    return c * bright * saturate(saturation * 2 - 0.3);
}

float4 main(float2 uv : TEXCOORD0) : COLOR {
    float2 step = settings.yz;
    float3 sum = Glowing(uv + float2(-1, -1) * step) + Glowing(uv + float2(1, -1) * step)
        + Glowing(uv + float2(-1, 1) * step) + Glowing(uv + float2(1, 1) * step);
    return float4(sum * 0.25, 1);
}
