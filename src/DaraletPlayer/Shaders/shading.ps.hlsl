// Shading from the game's depth buffer (Ui/DepthShading.cs): ambient occlusion and contact shadows toward the sun,
// as a grey multiplied onto the picture (the blend is destination times source); with mode 1 the depth itself as grey,
// to see that it's read, and with mode 2 the shading alone as grey. Compiled by Shaders/build.cmd into
// Resources/shaders/shading.ps.cso.
//
// The game's camera is Direct3D's left-handed perspective: a depth d is M33 + M43 / z for a view distance z, so
// z = M43 / (d - M33); a pixel's place in view space is x = (2u - 1) z / M11, y = (1 - 2v) z / M22.
sampler depthBuffer : register(s0);
sampler shadowMap : register(s1);
sampler farMap : register(s2);
sampler closeMap : register(s3);

float4 projection : register(c0);  // M33, M43, M11, M22
float4 occlusion : register(c1);   // radius (m), strength (0-1), furthest (m), bias (m)
float4 sun : register(c2);         // toward the sun in view space (xyz), shadow strength (0-1)
float4 screen : register(c3);      // width, height, 1/width, 1/height
float4 shadowRay : register(c4);   // length (m), furthest (m), thickness (m), mode (0 shading, 1 depth view)
float4 toShadow[4] : register(c5);  // rows: view space to the sun's shadow map (x, y in -1..1, z its depth 0..1)
float4 sunShadow : register(c9);  // strength (0-1, 0 for none), 1 / the map's depth range (m), a texel (m), 1 / size
float4 sunLight : register(c10);  // the game's ambient, its sun on ground facing it (grey levels), far map on, a far texel (m)
float4 farShadow[4] : register(c11); // rows: view space to the far shadow map
float4 closeShadow[4] : register(c15); // rows: view space to the close shadow map, the few meters round you
float4 closeTexel : register(c19);    // the close map's texel (m), the edges' softness (m), your character's (m)
float4 upView : register(c20);       // which way is up, in view space

float ViewZ(float d) {
    return projection.y / (d - projection.x);
}

float3 ViewPlace(float2 uv, float z) {
    return float3((uv.x * 2 - 1) * z / projection.z, (1 - uv.y * 2) * z / projection.w, z);
}

float2 ScreenOf(float3 p) {
    return float2(p.x * projection.z / p.z * 0.5 + 0.5, 0.5 - p.y * projection.w / p.z * 0.5);
}

float SceneZ(float2 uv) {
    return ViewZ(tex2Dlod(depthBuffer, float4(uv, 0, 0)).r);
}

// Sixteen places in a disc, spread evenly (a Poisson disc), for soft edges.
static const float2 Disc[16] = {
    float2(-0.942, -0.399), float2(0.946, -0.769), float2(-0.094, -0.929), float2(0.345, 0.294),
    float2(-0.916, 0.458), float2(-0.815, -0.879), float2(-0.383, 0.277), float2(0.975, 0.756),
    float2(0.443, -0.975), float2(0.537, -0.474), float2(-0.265, -0.419), float2(0.792, 0.191),
    float2(-0.242, 0.997), float2(-0.814, 0.914), float2(0.200, 0.786), float2(0.144, -0.141),
};

// How much of a place a shadow map sees is behind something (0 to 1). With a softness wider than a few texels (the
// same width in meters in every map, so the near and far maps' edges match), sixteen samples in a disc of it, turned
// by the pixel's spin so no pattern shows (the user's call: the first edges were "slightly too hard"); otherwise
// a 3 by 3 texel filter blended by where the place falls between texels (16 samples), a smooth ramp (hard steps showed
// each 6 cm texel as a block at the camera's feet). A place sits behind what the map saw by some allowance and still
// counts as lit: more for a slanting surface, whose texel (and disc) covers a longer stretch of it.
float Shadowed(sampler map, float4 s, float facing, float texelMeters, float spin, float softness) {
    float2 at = float2(s.x * 0.5 + 0.5, 0.5 - s.y * 0.5);
    float bias = (0.02 + texelMeters * (1.5 + 3 * (1 - facing)) + softness * 0.5 * (1 - facing)) * sunShadow.y;
    float radius = softness * sunShadow.w / texelMeters;

    if (radius > sunShadow.w * 1.5) {
        float c = cos(spin);
        float n = sin(spin);
        float hidden = 0;

        for (int i = 0; i < 16; i++) {
            float2 d = Disc[i];
            float2 offset = float2(d.x * c - d.y * n, d.x * n + d.y * c) * radius;
            hidden += step(tex2Dlod(map, float4(at + offset, 0, 0)).r, s.z - bias);
        }

        return hidden / 16;
    }
    float2 texels = at / sunShadow.w - 0.5;
    float2 within = frac(texels);
    float2 first = (floor(texels) + 0.5) * sunShadow.w;
    float shadowed = 0;

    for (int u = -1; u <= 2; u++) {
        float wu = u == -1 ? 1 - within.x : u == 2 ? within.x : 1;

        for (int v = -1; v <= 2; v++) {
            float wv = v == -1 ? 1 - within.y : v == 2 ? within.y : 1;
            float seen = tex2Dlod(map, float4(first + float2(u, v) * sunShadow.w, 0, 0)).r;
            shadowed += wu * wv * step(seen, s.z - bias);
        }
    }

    return shadowed / 9;
}

// a different turn of the sample kernel for each pixel in a 4 by 4 block, so the noise is fine and even
float Hash(float2 pixel) {
    return frac(sin(dot(floor(pixel), float2(12.9898, 78.233))) * 43758.5453);
}

static const float3 Kernel[12] = {
    float3(0.53, 0.18, 0.21), float3(-0.37, 0.42, 0.31), float3(0.12, -0.55, 0.45),
    float3(-0.62, -0.21, 0.12), float3(0.25, 0.71, 0.33), float3(-0.08, -0.17, 0.81),
    float3(0.81, -0.35, 0.27), float3(-0.45, 0.66, 0.52), float3(0.33, 0.09, 0.95),
    float3(-0.86, 0.12, 0.40), float3(0.05, -0.90, 0.25), float3(0.61, 0.55, 0.47),
};

float4 main(float2 uv : TEXCOORD0, float2 pixel : VPOS) : COLOR {
    float d = tex2Dlod(depthBuffer, float4(uv, 0, 0)).r;

    if (d >= 0.99999) {
        return float4(1, 1, 1, 1);
    }

    float z = ViewZ(d);

    if (shadowRay.w > 0.5 && shadowRay.w < 1.5) {
        // the depth view: near white, far black, on a log scale to 5 km
        float g = 1 - saturate(log2(max(z, 1)) / log2(5000));
        return float4(g, g, g, 1);
    }

    float3 p = ViewPlace(uv, z);
    float3 n = normalize(cross(ddx(p), ddy(p)));

    // facing the camera: against the line of sight to this point, not the view's axis (ground seen at a low angle is
    // nearly along the axis, and flipped at random by it: the first build darkened the ground in front of you)
    if (dot(n, p) > 0) {
        n = -n;
    }

    if (shadowRay.w > 2.5) {
        // mode 3: the normals worked out from the depth, as colours (x red, y green, toward the camera blue)
        return float4(n.x * 0.5 + 0.5, n.y * 0.5 + 0.5, -n.z, 1);
    }

    float light = 1;

    // ambient occlusion: how much of the hemisphere over the surface is closed in, within the radius
    float aoFade = saturate(1 - z / occlusion.z);

    if (occlusion.y > 0 && aoFade > 0) {
        float angle = Hash(pixel) * 6.2831853;
        float s = sin(angle);
        float c = cos(angle);
        float closed = 0;

        // the depth buffer's steps grow with distance, so the allowance for them does too
        float bias = occlusion.w + z * 0.004;
        float counted = 0;

        for (int i = 0; i < 12; i++) {
            float3 k = Kernel[i];
            k = float3(k.x * c - k.y * s, k.x * s + k.y * c, k.z);

            if (dot(k, n) < 0) {
                k = -k;
            }

            // lifted off the surface a little, or a sample lying along flat ground finds the ground itself in front
            float3 q = p + (k + n * 0.25) * occlusion.x;
            float2 at = ScreenOf(q);

            // off the screen there's nothing to know: such a sample doesn't count either way
            if (q.z > 0.1 && at.x > 0 && at.x < 1 && at.y > 0 && at.y < 1) {
                float sceneZ = SceneZ(at);
                float inFront = step(sceneZ, q.z - bias);
                // something much nearer than the surface (you, in front of the ground behind you) counts for little,
                // or it leaves a dark halo round itself
                float near = saturate(occlusion.x / abs(z - sceneZ));
                closed += inFront * near * near;
                counted += 1;
            }
        }

        light *= 1 - closed / max(counted, 1) * occlusion.y * aoFade;
    }

    // contact shadows: a short march toward the sun; something in the depth buffer in front of the ray, not by more
    // than the thickness, is in the way
    float shadowFade = saturate(1 - z / shadowRay.y);

    if (sun.w > 0 && shadowFade > 0 && dot(n, sun.xyz) > 0) {
        float hit = 0;
        float3 start = p + n * 0.05 * (1 + z / 50);

        for (int j = 1; j <= 16; j++) {
            float3 q = start + sun.xyz * (shadowRay.x * j / 16);

            if (q.z <= 0.1) {
                break;
            }

            float gap = q.z - SceneZ(ScreenOf(q));

            if (gap > 0.02 * (1 + z / 30) && gap < shadowRay.z) {
                hit = 1;
                break;
            }
        }

        light *= 1 - hit * sun.w * shadowFade;
    }

    // the sun's shadows, from shadow maps of the game's own world drawn from the sun (Ui/DepthShading.Shadows.cs): the
    // near map where it covers the pixel, blending into the far one toward its edge, and the far one fading out at its own
    if (sunShadow.x > 0) {
        float facing = saturate(dot(n, sun.xyz));
        // each pixel's turn of the soft edges' disc
        float spin = Hash(pixel + 17) * 6.2831853;

        // The ground is lit by how it faces the sun, so ground turned away is lit by the ambient alone already. But the
        // game lights characters and things evenly all round (a share of the sun whichever way they face), so a
        // character's back in a tree's shade went unshaded (seen in the game). Told apart by the surface's direction:
        // facing up is ground; facing sideways or down, a thing, which loses at least that share in shadow.
        float up = dot(n, upView.xyz);
        float thing = 1 - saturate((up - 0.5) / 0.25);
        float lightFacing = max(facing, thing * 0.4);
        float biasFacing = max(facing, 0.25);

        if (lightFacing > 0) {
            // each map looked up a little way out from the surface, along its normal, by two of that map's texels (more
            // where the sun grazes it): coarse texels otherwise shadow the very ground they cover, and with a low sun the
            // far map darkened everything past the near map's edge, a lit square round you (seen in the game)
            float lift = 2 + 3 * (1 - facing);
            float3 ps = p + n * (sunShadow.z * lift);
            float4 s = ps.x * toShadow[0] + ps.y * toShadow[1] + ps.z * toShadow[2] + toShadow[3];
            float nearEdge = 1 - max(abs(s.x), abs(s.y));
            float shadowed = 0;
            float covered = 0;

            if (sunLight.z > 0.5) {
                float3 pf = p + n * (sunLight.w * lift);
                float4 f = pf.x * farShadow[0] + pf.y * farShadow[1] + pf.z * farShadow[2] + farShadow[3];
                float farEdge = 1 - max(abs(f.x), abs(f.y));

                if (farEdge > 0) {
                    shadowed = Shadowed(farMap, f, biasFacing, sunLight.w, spin, closeTexel.y);
                    covered = saturate(farEdge * 6);
                }
            }

            if (nearEdge > 0) {
                // all near inside its last tenth, then the far map takes over
                float nearShare = saturate(nearEdge * 10);
                shadowed = lerp(shadowed, Shadowed(shadowMap, s, biasFacing, sunShadow.z, spin, closeTexel.y), nearShare);
                covered = lerp(covered, sunLight.z > 0.5 ? 1 : saturate(nearEdge * 6), nearShare);
            }

            float hidden = shadowed * covered;

            // the close map round you, 8 mm a texel, where it covers the pixel: your character alone (its arms and legs),
            // with its own softness; everything else is in the near and far maps, so the darker of the two is the shade
            float3 pc = p + n * (closeTexel.x * lift);
            float4 c = pc.x * closeShadow[0] + pc.y * closeShadow[1] + pc.z * closeShadow[2] + closeShadow[3];
            float closeEdge = 1 - max(abs(c.x), abs(c.y));

            if (closeEdge > 0) {
                float closeShare = saturate(closeEdge * 10);
                hidden = max(hidden, Shadowed(closeMap, c, biasFacing, closeTexel.x, spin, closeTexel.z) * closeShare);
            }

            // in shadow a surface keeps the game's ambient and loses the sun: the game's own light, ambient plus the sun
            // on it, as the ground is lit (the grass matched it in the game)
            float lit = sunLight.x + sunLight.y * lightFacing;
            float dark = sunLight.x / max(lit, 0.001);
            light *= lerp(1, dark, hidden * sunShadow.x);
        }
    }

    return float4(light, light, light, 1);
}
