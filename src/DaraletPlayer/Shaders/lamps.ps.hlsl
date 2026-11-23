// Light from the game's own lights (Ui/DepthShading.Lamps.cs): up to forty point lights near the camera, read from the
// client's own lists, brightening what's round them per pixel. The light pass gives how much brighter a pixel gets (0
// none), blended as destination times (1 + source), so a surface keeps its own colour and is lit by the light's; the
// shadow pass before it, how much of the game's own light is left where something blocks a lamp (a character on the
// floor under it), multiplied in. Each light reaches as far as the game lets it, fading smoothly to nothing there; a
// surface lit from behind gets none. The six lamps nearest you have shadows, from cube shadow maps of the game's own
// world drawn from each lamp (Ui/DepthShading.LampShadows.cs): the first build marched through the depth buffer
// instead, which only knows what's on screen, and walls and furniture threw shadows that made no sense (seen in the
// game). Places come back from the depth as in shading.ps.hlsl. Compiled by Shaders/build.cmd.
sampler depthBuffer : register(s0);
samplerCUBE lampMap0 : register(s1);
samplerCUBE lampMap1 : register(s2);
samplerCUBE lampMap2 : register(s3);
samplerCUBE lampMap3 : register(s4);
samplerCUBE lampMap4 : register(s5);
samplerCUBE lampMap5 : register(s6);
samplerCUBE lampMap6 : register(s7);
samplerCUBE lampMap7 : register(s8);
samplerCUBE lampMap8 : register(s9);
samplerCUBE lampMap9 : register(s10);
samplerCUBE lampMap10 : register(s11);
samplerCUBE lampMap11 : register(s12);
// the frame as drawn before this pass, at night only (the brightest parts are spared the night's darkening)
sampler frame : register(s13);

// how many shadow maps this build of the shader looks up (build.cmd makes 12, 8 and 6): a card takes a pixel shader of only
// so many instructions, and with all twelve, about 5,000, one refused it and the lamps gave no light (the user,
// November 2026); DepthShading.Lamps.cs uses the biggest the card takes
#ifndef MAPS
#define MAPS 12
#endif

// the C# side (DepthShading.Lamps.cs) writes these in this order
#define MOST_LIT 40
float4 projection : register(c0);  // M33, M43, M11, M22
float4 settings : register(c1);    // strength, shadows (1 or 0), light count, mode (0 light, 1 shadow)
// per light: view-space place (xyz) and reach (m); colour (rgb, eased in and faded by distance already) and its cube
// map (0 to 11), -1 for none. Two arrays, each indexed by the loop's counter alone: read as lamps[i * 2], the compiler
// copied the whole array into temporary registers, which 16 entries fitted and 80 didn't
float4 lampPlaces[MOST_LIT] : register(c2);
float4 lampColours[MOST_LIT] : register(c42);
float4 viewToWorld[3] : register(c82); // rows: a direction in view space to the device's world
float4 night : register(c85);      // how much darker (0 day, up to 0.8), lamps times this, the sky's share, replacing (w)
float4 mapLamps[12] : register(c86); // each map's lamp: view-space place (xyz) and reach (m, 0 for none)
float4 mapShares[3] : register(c98); // how much of each map's shadow shows, eased in and out (0 to 1)

float ViewZ(float d) {
    return projection.y / (d - projection.x);
}

float3 ViewPlace(float2 uv, float z) {
    return float3((uv.x * 2 - 1) * z / projection.z, (1 - uv.y * 2) * z / projection.w, z);
}

// How much of a place a lamp sees (0 to 1): its distance from the lamp against the nearest the cube map saw that way,
// nine samples spread across the way. The spread grows with how far the place is past what blocks it, as a real
// lamp's shadow softens away from the thing casting it: the hard shadow of the wall above the tavern's door ended the
// beam outside in a sharp line (the user, November 2026)
float Seen(samplerCUBE map, float3 fromLamp, float distance, float reach, float facing) {
    float3 way = fromLamp / distance;
    float3 side = normalize(abs(way.y) < 0.9 ? cross(way, float3(0, 1, 0)) : cross(way, float3(1, 0, 0)));
    float3 other = cross(way, side);
    // kept under a wall's thickness far off, or light leaked round the walls' feet
    float bias = 0.03 + distance * 0.004;
    // the samples turned by a hash of the place, so the map's texels, stretched across the ground, don't show as steps
    // (your shadow looked blocky, the user, November 2026)
    // A hash with no sine or cosine in it (each costs eight of the card's instruction slots, and this runs twice a
    // map): with them the eight-map shader grew past what the user's card takes (November 2026). Half a turn is
    // enough, the samples coming in opposite pairs, so its cosine is the hash and its sine follows
    float3 mixed = frac(fromLamp * 97.13);
    mixed += dot(mixed, mixed.zyx + 31.32);
    float turnCos = frac((mixed.x + mixed.y) * mixed.z) * 2 - 1;
    float turnSin = sqrt(1 - turnCos * turnCos);
    float3 side2 = side * turnCos + other * turnSin;
    other = other * turnCos - side * turnSin;
    side = side2;
    // what blocks the lamp round about, averaged over a wide search: from the ray alone, a place just outside a shadow
    // found no blocker, got the narrowest edge, and the shadow of the wall over the door stayed hard (the user)
    float centre = texCUBElod(map, float4(way, 0)).r * reach;
    float found = 0;
    float search = 0.06;
    float d0 = centre;
    float d1 = texCUBElod(map, float4(way + side * search, 0)).r * reach;
    float d2 = texCUBElod(map, float4(way - side * search, 0)).r * reach;
    float d3 = texCUBElod(map, float4(way + other * search, 0)).r * reach;
    float d4 = texCUBElod(map, float4(way - other * search, 0)).r * reach;
    float h0 = step(d0, distance - bias);
    float h1 = step(d1, distance - bias);
    float h2 = step(d2, distance - bias);
    float h3 = step(d3, distance - bias);
    float h4 = step(d4, distance - bias);
    found = h0 + h1 + h2 + h3 + h4;

    if (found < 0.5) {
        return 1;
    }

    // the blocker nearest the place decides: averaged, the door frame found round your shadow blurred it (the user
    // wanted it harder, November 2026)
    float blocker = max(max(h0 * d0, h1 * d1), max(max(h2 * d2, h3 * d3), h4 * d4));
    // the lamp taken as a light 40 cm across (1.5 m washed the beam's shape out): the soft edge widens with how far
    // past the blocker the place is, as
    // seen from the lamp (a door frame's shadow was hard)
    float spread = min(0.003 + 0.4 * max(distance - blocker, 0) / (max(blocker, 0.5) * distance), 0.08);
    float3 a = side * spread;
    float3 b = other * spread;
    float3 c = (side + other) * (spread * 0.35);
    float3 e = (side - other) * (spread * 0.35);
    float seen = step(distance - bias, centre);
    seen += step(distance - bias, texCUBElod(map, float4(way + a, 0)).r * reach);
    seen += step(distance - bias, texCUBElod(map, float4(way - a, 0)).r * reach);
    seen += step(distance - bias, texCUBElod(map, float4(way + b, 0)).r * reach);
    seen += step(distance - bias, texCUBElod(map, float4(way - b, 0)).r * reach);
    seen += step(distance - bias, texCUBElod(map, float4(way + c, 0)).r * reach);
    seen += step(distance - bias, texCUBElod(map, float4(way - c, 0)).r * reach);
    seen += step(distance - bias, texCUBElod(map, float4(way + e, 0)).r * reach);
    seen += step(distance - bias, texCUBElod(map, float4(way - e, 0)).r * reach);
    return seen / 9;
}

// How much of a place a map's lamp sees, 1 outside its reach. Each map is looked up once, before the loop over the
// lights: samplers can't be picked by a light's number, and six lookups in the loop ran out of registers
float3 FacingCamera(float3 n, float3 p) {
    return dot(n, p) > 0 ? -n : n;
}

float3 PlaceAt(float2 uv) {
    return ViewPlace(uv, ViewZ(min(tex2Dlod(depthBuffer, float4(uv, 0, 0)).r, 0.9999)));
}

// How much a place off to one side counts as this surface (0 to 1): by how far it stands off the pixel's own facet
// against how far to the side it was looked for (span, meters). A curve's next facet leaves it a little off; a crease
// (a wall's foot), or something in front of the surface or behind it, a lot. Judged against its own distance instead,
// something in front (a sword over the ground, your arm) passed, being off mostly along the line of sight, and the
// ground's way tilted round it: a dark outline round the character and the weapon (the user's screenshots, November
// 2026)
float SameSurface(float3 q, float3 p, float3 n, float span) {
    return 1 - smoothstep(0.3, 0.6, abs(dot(q - p, n)) / span);
}

// The way a surface faces, smoothed: from places about 6 cm off each way, not the next pixel's. The depth only gives
// each triangle's own way, and the game shades a character smoothly between its corners' normals, so lit by a lamp a
// face and armour showed every facet as a hard-edged patch (the user's screenshot by a bonfire, November 2026). Six
// centimetres spans a facet or two near you; past a few meters it's less than a pixel, and this is the narrow way again.
// A side that isn't this surface (a crease, another object's edge) is left out, the other side's taken alone
float3 SmoothNormal(float2 uv, float3 p, float3 n, float3 alongX, float3 alongY, float2 pixel) {
    float2 wide = clamp(0.06 * projection.zw / (2 * p.z), pixel, pixel * 16);
    float3 qR = PlaceAt(uv + float2(wide.x, 0));
    float3 qL = PlaceAt(uv - float2(wide.x, 0));
    float3 qD = PlaceAt(uv + float2(0, wide.y));
    float3 qU = PlaceAt(uv - float2(0, wide.y));
    // how far to the side each way was looked, in meters (6 cm, unless held to a pixel or sixteen)
    float2 span = wide * 2 * p.z / projection.zw;
    float wR = SameSurface(qR, p, n, span.x);
    float wL = SameSurface(qL, p, n, span.x);
    float wD = SameSurface(qD, p, n, span.y);
    float wU = SameSurface(qU, p, n, span.y);
    float3 ax = (qR - p) * wR + (p - qL) * wL;
    float3 ay = (qD - p) * wD + (p - qU) * wU;
    ax = lerp(normalize(alongX), ax / max(length(ax), 1e-6), saturate(wR + wL));
    ay = lerp(normalize(alongY), ay / max(length(ay), 1e-6), saturate(wD + wU));
    return FacingCamera(normalize(cross(ax, ay)), p);
}

float MapSeen(samplerCUBE map, float4 lamp, float3 p, float3 n, float toward) {
    float3 toLight = lamp.xyz - p;
    float distance = length(toLight);
    float seen = 1;

    [branch]
    if (distance < lamp.w && distance > 0.01) {
        float facing = saturate(dot(n, toLight / distance));
        // the way from the lamp to the place, in the device's world, as the cube map is laid out
        // looked up a little off the surface, along its normal (more where the light grazes it), not with a bigger
        // allowance: one big enough for grazing light let the light past a wall's foot onto the ground outside (seen
        // in the game, November 2026, round the Holtburg tavern)
        // and a little toward the camera: a wall that's a single sheet puts its outside at the same distance from a lamp
        // inside as itself, so its bottom row (a ground-like normal on the crease) was 'seen', a line of light along
        // the tavern's walls' feet (seen in the game, November 2026); on the camera's side, the wall is between
        float3 v = p + n * (0.05 + 0.15 * (1 - facing)) - normalize(p) * toward - lamp.xyz;
        distance = length(v);
        float3 fromLamp = v.x * viewToWorld[0].xyz + v.y * viewToWorld[1].xyz + v.z * viewToWorld[2].xyz;
        seen = Seen(map, fromLamp, distance, lamp.w, facing);

        // the ground outdoors isn't in the maps, so a ray from a lamp passing just under a wall's lower edge (between
        // the map's texels) found nothing and lit a faint line along the wall's foot (the tavern's angled wall, the
        // user, November 2026): a point 30 cm above must be seen too where something is within 0.6 m of it (the
        // wall), softly as the place itself; not further (your body cast a second, stepped shadow)
        float3 lifted = fromLamp + float3(0, 0.3, 0);
        float liftedDistance = length(lifted);
        float blocker = texCUBElod(map, float4(lifted / liftedDistance, 0)).r * lamp.w;
        float nearWall = smoothstep(liftedDistance - 1.0, liftedDistance - 0.6, blocker);
        seen = min(seen, lerp(1, Seen(map, lifted, liftedDistance, lamp.w, facing), nearWall));
    }

    return seen;
}

// a map's lookup, or 1 (nothing blocked) past the maps this build has
#define LOOKUP(k) (k < MAPS ? MapSeen(lampMap##k, mapLamps[k], p, n, toward) : 1.0)

float4 main(float2 uv : TEXCOORD0) : COLOR {
    float d = tex2Dlod(depthBuffer, float4(uv, 0, 0)).r;

    float z = ViewZ(min(d, 0.9999));
    float3 p = ViewPlace(uv, z);
    // the normal from the neighbouring pixels' places, each way the one on the same surface (the nearer in depth):
    // from the 2x2 block's derivatives, a pixel on the crease where a wall meets the ground mixed the two and pointed
    // wrong, and a line of light showed along the foot of every wall (seen in the game, November 2026)
    float2 stepX = float2(abs(ddx(uv.x)), 0);
    float2 stepY = float2(0, abs(ddy(uv.y)));
    float3 pR = ViewPlace(uv + stepX, ViewZ(min(tex2Dlod(depthBuffer, float4(uv + stepX, 0, 0)).r, 0.9999)));
    float3 pL = ViewPlace(uv - stepX, ViewZ(min(tex2Dlod(depthBuffer, float4(uv - stepX, 0, 0)).r, 0.9999)));
    float3 pD = ViewPlace(uv + stepY, ViewZ(min(tex2Dlod(depthBuffer, float4(uv + stepY, 0, 0)).r, 0.9999)));
    float3 pU = ViewPlace(uv - stepY, ViewZ(min(tex2Dlod(depthBuffer, float4(uv - stepY, 0, 0)).r, 0.9999)));
    float3 alongX = abs(pR.z - p.z) < abs(p.z - pL.z) ? pR - p : p - pL;
    float3 alongY = abs(pD.z - p.z) < abs(p.z - pU.z) ? pD - p : p - pU;
    float toward = 0.3;
    float3 n = normalize(cross(alongX, alongY));
    // every way the neighbours give a surface (left or right, up or down), facing the camera: a pixel on a crease (a
    // wall's foot) has the wall's among them, and the light it takes is the least any gives, so a single-sheet wall
    // lit from behind stays dark to its last row (moving the check toward the camera left a faint line along an angled
    // wall, the user, November 2026); on a flat surface all four agree
    float3 n1 = FacingCamera(normalize(cross(pR - p, pD - p)), p);
    float3 n2 = FacingCamera(normalize(cross(p - pL, p - pU)), p);
    float3 n3 = FacingCamera(normalize(cross(pR - p, p - pU)), p);
    float3 n4 = FacingCamera(normalize(cross(p - pL, pD - p)), p);
    // the light pass is blended as the picture times twice this (0.5 leaves it be): the night darkens it, the lamps lift it
    // the night spares what's bright already: at night nearly all of that is a light source (a crystal's flame, a
    // lamp's glass, a fire, the moon), which the game draws self-lit, and darkened with the rest they lost their glow
    // (the user, November 2026: Hebian-to's crystals 40% dimmer at 80% night); everything else darkens as before
    float3 picture = tex2Dlod(frame, float4(uv, 0, 0)).rgb;
    float spare = night.x > 0 ? smoothstep(0.55, 0.9, max(max(picture.r, picture.g), picture.b)) : 0;
    float darkness = night.x * (1 - spare);
    float sky = (1 - darkness * night.z) * 0.5;
    float nothing = settings.w > 0.5 ? 1 : sky;

    if (d >= 0.99999) {
        return float4(nothing, nothing, nothing, 1);
    }

    if (dot(n, p) > 0) {
        n = -n;
    }

    // where the four narrow ways disagree by more than about 75 degrees, the pixel is on a crease (a wall's foot), and
    // takes the least light any of them gives (below); on a character's facets they agree near enough, and the smoothed
    // way alone decides, so no facet's edge shows as a line
    float agree = min(min(dot(n1, n2), dot(n3, n4)), min(dot(n1, n3), dot(n2, n4)));
    float crease = 1 - smoothstep(0.1, 0.35, agree);
    // the smoothed way only for how much light a surface turned toward a lamp takes; the shadow maps are looked up off
    // the triangle's own way (n), as before it: off the smoothed way, a lookup on a low-poly face or armour plate could
    // stay too near its own triangle, half blocked by the character's own body, and the samples' turn (a hash of the
    // place, fixed in the world) showed as dots the character moved through (the user's screenshot, November 2026)
    float3 smooth = SmoothNormal(uv, p, n, alongX, alongY, float2(stepX.x, stepY.y));

    float3 added = 0;
    float dark = 0;
    float4 seenA = 1;
    float4 seenB = 1;
    float4 seenC = 1;

    // each map's shadow as much as it shows (mapShares, eased in when a lamp gains a map and out before it gives one
    // up, so a lamp's shadows never appear or vanish at once), looked up once each; a map with no lamp (reach 0) is 1
    [branch]
    if (settings.y > 0.5) {
        seenA = lerp(1, float4(LOOKUP(0), LOOKUP(1), LOOKUP(2), LOOKUP(3)), mapShares[0]);
        seenB = lerp(1, float4(LOOKUP(4), LOOKUP(5), LOOKUP(6), LOOKUP(7)), mapShares[1]);
        seenC = lerp(1, float4(LOOKUP(8), LOOKUP(9), LOOKUP(10), LOOKUP(11)), mapShares[2]);
    }

    float firstSeen = seenA.x;
    // a test run's `depthview 5`: which light gives a pixel the most, by colour
    float most = 0;
    float mostAt = -1;

    [loop]
    for (int i = 0; i < MOST_LIT; i++) {
        if (i >= settings.z) {
            break;
        }

        float4 place = lampPlaces[i];
        float3 colour = lampColours[i].rgb;
        float map = lampColours[i].w;
        float3 toLight = place.xyz - p;
        float distance = length(toLight);

        if (distance < place.w && distance > 0.01) {
            toLight /= distance;
            float least = min(min(dot(n1, toLight), dot(n2, toLight)), min(dot(n3, toLight), dot(n4, toLight)));
            float faces = lerp(dot(smooth, toLight), least, crease);
            float facing = saturate(faces);
            // how a face turns counts for little: the game lights a character smoothly between its corners' normals, but
            // the depth only gives each triangle's own, and its armour showed every facet (the user, November 2026). A
            // face turned away still gets none, faded in so there's no line; the maps shade a body's far side
            float turned = smoothstep(-0.3, 0.1, faces) * (0.35 + 0.5 * facing);
            // (1 - x squared) squared: to nothing at the reach with no edge (the plain square showed where it ended)
            float fade = saturate(1 - (distance / place.w) * (distance / place.w));
            float amount = turned * fade * fade;
            float seen = 1;

            // a lamp with a map: how much of it this place sees. One without lights unshadowed, as the game's own lamps
            // always did: lighting nothing (the first way, when only the six nearest were the plugin's) left a lamp's
            // light to the game, which lights only its eight nearest, and a lamp leaving those took its light off a
            // whole building at once (the user, November 2026)
            if (map > -0.5) {
                float4 inA = map == float4(0, 1, 2, 3);
                float4 inB = map == float4(4, 5, 6, 7);
                float4 inC = map == float4(8, 9, 10, 11);
                seen = dot(inA, seenA) + dot(inB, seenB) + dot(inC, seenC);

                // a shadow takes away some of the game's own light from it, which it gives everything in reach in
                // full: so by how near the light is, not the fade added light has
                dark = max(dark, (1 - seen) * facing * saturate(1.5 - 1.5 * distance / place.w));
            }

            added += colour * amount * seen;

            if (amount * seen > most) {
                most = amount * seen;
                mostAt = i;
            }
        }
    }

    // a test run's `depthview 4`: what the first shadowed lamp sees, white, and what's hidden from it, black
    if (settings.w > 2.5) {
        float3 hues[8] = { float3(1, 0, 0), float3(0, 1, 0), float3(0, 0, 1), float3(1, 1, 0), float3(1, 0, 1),
            float3(0, 1, 1), float3(1, 0.5, 0), float3(1, 1, 1) };
        float3 shown = 0;
        float hue = mostAt - floor(mostAt / 8) * 8;

        for (int k = 0; k < 8; k++) {
            shown += mostAt >= 0 && k == hue ? hues[k] : 0;
        }

        return float4(shown * saturate(most * 4), 1);
    }

    if (settings.w > 1.5) {
        return float4(firstSeen, firstSeen, firstSeen, 1);
    }

    // the shadow pass: what's left of the light, multiplied in; the light pass: how much brighter
    if (settings.w > 0.5) {
        float left = 1 - dark * 0.55 * saturate(settings.x);
        return float4(left, left, left, 1);
    }

    // the light pass: darker by the night, brighter by the lamps (stronger at night), as the picture times twice it
    float3 factor = (1 - darkness) + added * settings.x * night.y;
    return float4(saturate(factor * 0.5), 1);
}
