// Water reflections (Ui/DepthShading.Water.cs): screen-space reflections on the game's water, blended over it by alpha.
// Compiled by Shaders/build.cmd into Resources/shaders/water.ps.cso.
//
// Positions come back from the depth as in shading.ps.hlsl. The device's world is (x east, height, y north) in meters
// from your landblock's south-west corner; a pixel is water where the water map (Feed/WaterMap.cs, a value a terrain
// vertex, filtered) says so and its surface faces up. The reflected ray is marched through the depth buffer in view
// space; where it hits, the frame's own colour there is the reflection, and where it leaves the screen, the sky colour.
sampler depthBuffer : register(s0);
sampler frameColour : register(s1);
sampler waterMap : register(s2);

float4 projection : register(c0);  // M33, M43, M11, M22
float4 inverseView[4] : register(c1); // rows: view space to the device's world
float4 viewRows[3] : register(c5);    // rows of the view's rotation: world directions to view space
float4 mapShape : register(c8);    // vertices a side, the home block's first vertex, 1 / side, seconds
float4 look : register(c9);        // strength (0-1), ripple scale (1 usual), ripples on, fresnel on
float4 extras : register(c10);     // glints on, blur on, soft shore on, furthest reflected (m)
float4 sun : register(c11);        // toward the sun in the world (xyz), its strength (0 under the horizon)
float4 sky : register(c12);        // the sky's colour where a reflection leaves the screen (rgb), sun colour scale
float4 screen : register(c13);     // width, height, 1/width, 1/height

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

float3 ToWorld(float3 p) {
    return p.x * inverseView[0].xyz + p.y * inverseView[1].xyz + p.z * inverseView[2].xyz + inverseView[3].xyz;
}

float3 DirectionToWorld(float3 v) {
    return v.x * inverseView[0].xyz + v.y * inverseView[1].xyz + v.z * inverseView[2].xyz;
}

float3 DirectionToView(float3 v) {
    return v.x * viewRows[0].xyz + v.y * viewRows[1].xyz + v.z * viewRows[2].xyz;
}

// The slope of a few waves crossing at odd angles, so no stripes line up: east and north, per meter. A wave is let go
// of where it's too small on screen to draw (metersPerPixel), or far off it made rings of moire.
float2 RippleSlope(float2 at, float seconds, float scale, float metersPerPixel) {
    float2 slope = 0;
    static const float3 Waves[4] = {
        float3(0.80, 0.60, 6.3), float3(-0.45, 0.89, 3.9), float3(0.21, -0.98, 2.6), float3(-0.93, -0.37, 1.7),
    };

    for (int i = 0; i < 4; i++) {
        float2 direction = Waves[i].xy;
        float length = Waves[i].z * scale;
        float frequency = 6.2831853 / length;
        float phase = dot(direction, at) * frequency + seconds * (1.2 / sqrt(length));
        // each wave as steep as the others; shown from 12 pixels a wave, wholly from 24
        float shown = saturate((length / metersPerPixel - 12) / 12);
        slope += direction * cos(phase) * 0.022 * shown;
    }

    return slope;
}

float4 main(float2 uv : TEXCOORD0) : COLOR {
    float d = tex2Dlod(depthBuffer, float4(uv, 0, 0)).r;

    if (d >= 0.99999) {
        return float4(0, 0, 0, 0);
    }

    float z = ViewZ(d);
    float3 p = ViewPlace(uv, z);
    float3 world = ToWorld(p);

    // where on the water map: a vertex every 24 meters from the map's first, filtered between them
    float2 vertex = world.xz / 24 + mapShape.y;
    float2 mapUv = (vertex + 0.5) * mapShape.z;

    if (mapUv.x < 0 || mapUv.y < 0 || mapUv.x > 1 || mapUv.y > 1) {
        return float4(0, 0, 0, 0);
    }

    float wet = tex2Dlod(waterMap, float4(mapUv, 0, 0)).r;

    // where the water starts: the map is a value a 24 m vertex, so near you the reflection begins well inside the
    // water and fades in (soft shores); far off that left the last 12 m before every shore the game's darker raw water,
    // a few pixels wide, a hard dark line along the far bank (seen in the game, November 2026), so further away it
    // reaches the shoreline and fades over more of the map
    float farOff = saturate((z - 60) / 240);
    float shoreFrom = lerp(0.45, 0.2, farOff);
    float shoreWidth = lerp(0.5, 0.3, farOff);

    if (wet < shoreFrom) {
        return float4(0, 0, 0, 0);
    }

    // only what faces up: not a tree or a person standing in the water
    float3 n = normalize(cross(ddx(p), ddy(p)));

    if (dot(n, p) > 0) {
        n = -n;
    }

    if (DirectionToWorld(n).y < 0.85) {
        return float4(0, 0, 0, 0);
    }

    float3 eye = inverseView[3].xyz;
    float3 toEye = normalize(eye - world);
    float3 normal = float3(0, 1, 0);

    if (look.z > 0.5) {
        // a pixel's size on the water in meters, stretched by how slanting the view is
        float metersPerPixel = z * 2 / (projection.z * screen.x) / max(0.15, toEye.y);
        float2 slope = RippleSlope(world.xz, mapShape.w, look.y, metersPerPixel);
        normal = normalize(float3(-slope.x, 1, -slope.y));
    }

    float3 reflected = reflect(-toEye, normal);
    float3 colour = sky.rgb;
    float found = 0;

    // the march, in view space, with steps growing as it goes
    float3 ray = DirectionToView(reflected);
    float3 q = p;
    float stepLength = 0.25 + z * 0.01;
    float travelled = 0;

    for (int i = 0; i < 40; i++) {
        q += ray * stepLength;
        travelled += stepLength;
        stepLength *= 1.12;

        if (q.z <= 0.1 || travelled > extras.w) {
            break;
        }

        float2 at = ScreenOf(q);

        if (at.x < 0 || at.y < 0 || at.x > 1 || at.y > 1) {
            break;
        }

        float sceneZ = SceneZ(at);

        // what it hits must stand above the water: skimming the water far off, the march took the water surface
        // further out for a hit and reflected the game's dark raw water, a hard dark band along the far bank (seen in
        // the game, November 2026)
        if (q.z > sceneZ && q.z - sceneZ < max(1.5, stepLength * 2)
            && ToWorld(ViewPlace(at, sceneZ)).y > world.y + 0.4) {
            // narrowed down between the last step and this one
            float3 back = q - ray * stepLength;

            for (int j = 0; j < 5; j++) {
                float3 middle = (back + q) * 0.5;

                if (middle.z > SceneZ(ScreenOf(middle))) {
                    q = middle;
                }
                else {
                    back = middle;
                }
            }

            float2 hit = ScreenOf(q);
            // fading near the screen's edges, where the next frame may have nothing to show
            float2 edge = min(hit, 1 - hit);
            found = saturate(min(edge.x, edge.y) * 10);

            if (extras.y > 0.5) {
                // further from the water, softer
                float spread = saturate(travelled / 60) * 0.006;
                colour = (tex2Dlod(frameColour, float4(hit + float2(spread, 0), 0, 0)).rgb
                    + tex2Dlod(frameColour, float4(hit - float2(spread, 0), 0, 0)).rgb
                    + tex2Dlod(frameColour, float4(hit + float2(0, spread), 0, 0)).rgb
                    + tex2Dlod(frameColour, float4(hit - float2(0, spread), 0, 0)).rgb) * 0.25;
            }
            else {
                colour = tex2Dlod(frameColour, float4(hit, 0, 0)).rgb;
            }

            colour = lerp(sky.rgb, colour, found);
            break;
        }
    }

    // the water's own colour carried half way into what it reflects, so it stays the game's water and not a mirror:
    // the hue of what's there, the brightness the reflection's
    float3 water = tex2Dlod(frameColour, float4(uv, 0, 0)).rgb;
    float3 hue = water / max(0.05, dot(water, float3(0.333, 0.333, 0.334)));
    colour *= lerp(float3(1, 1, 1), saturate(hue), 0.5);

    float amount = look.x;

    if (look.w > 0.5) {
        // Schlick's fresnel, water's 2% straight on
        float facing = saturate(dot(toEye, normal));
        amount *= 0.02 + 0.98 * pow(1 - facing, 5);
        // a little more than physics, or water seen from a hill shows nothing
        amount = saturate(amount * 1.3);
    }

    if (extras.z > 0.5) {
        amount *= saturate((wet - shoreFrom) / shoreWidth);
    }

    if (extras.x > 0.5 && sun.w > 0) {
        float glint = pow(saturate(dot(reflected, sun.xyz)), 600) * 6 * sun.w;
        colour += glint * sky.w;
        amount = max(amount, saturate(glint));
    }

    // faded out before the water map's edge (4 landblocks round the camera's, so at least 768 m off): stopping there
    // left a step in the water's tone far off (seen in the game, November 2026)
    float away = length(world.xz - eye.xz);
    amount *= saturate((740 - away) / 290);

    return float4(colour, saturate(amount));
}
