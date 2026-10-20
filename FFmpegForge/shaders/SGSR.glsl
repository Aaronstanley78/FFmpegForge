// Snapdragon Game Super Resolution 1 (SGSR) for libplacebo / mpv — FFmpeg Forge port.
// Based on SnapdragonStudios/snapdragon-gsr (Copyright (c) 2023-2025 Qualcomm Innovation Center, Inc. — BSD-3-Clause).
// Template value: {{EDGE_SHARPNESS}} (default 2.0).

//!DESC Snapdragon Game Super Resolution (SGSR 1)
//!HOOK MAIN
//!BIND HOOKED
//!WIDTH OUTPUT.w
//!HEIGHT OUTPUT.h
//!WHEN OUTPUT.w HOOKED.w 1.0 * > OUTPUT.h HOOKED.h 1.0 * > *

#define EdgeThreshold (8.0 / 255.0)
#define EdgeSharpness {{EDGE_SHARPNESS}}

float texG(vec2 t) { return HOOKED_tex((t + vec2(0.5)) * HOOKED_pt).g; }

// Same texel order as GLSL textureGather(): x=(i0,j1) y=(i1,j1) z=(i1,j0) w=(i0,j0)
vec4 gatherG(vec2 coord)
{
    vec2 base = floor(coord * HOOKED_size - vec2(0.5));
    return vec4(texG(base + vec2(0.0, 1.0)), texG(base + vec2(1.0, 1.0)), texG(base + vec2(1.0, 0.0)), texG(base));
}

float fastLanczos2(float x)
{
    float wA = x - 4.0;
    float wB = x * wA - wA;
    wA *= wA;
    return wB * wA;
}

vec2 weightY(float dx, float dy, float c, float stdv)
{
    float x = ((dx * dx) + (dy * dy)) * 0.55 + clamp(abs(c) * stdv, 0.0, 1.0);
    float w = fastLanczos2(x);
    return vec2(w, w * c);
}

vec4 hook()
{
    vec4 color = HOOKED_tex(HOOKED_pos);
    float alpha = color.a;
    vec2 imgCoord = HOOKED_pos * HOOKED_size + vec2(-0.5, 0.5);
    vec2 imgCoordPixel = floor(imgCoord);
    vec2 coord = imgCoordPixel * HOOKED_pt;
    vec2 pl = imgCoord - imgCoordPixel;
    vec4 left = gatherG(coord);
    float edgeVote = abs(left.z - left.y) + abs(color.g - left.y) + abs(color.g - left.z);
    if (edgeVote > EdgeThreshold)
    {
        coord.x += HOOKED_pt.x;
        vec4 right = gatherG(coord + vec2(HOOKED_pt.x, 0.0));
        vec4 upDown;
        upDown.xy = gatherG(coord + vec2(0.0, -HOOKED_pt.y)).wz;
        upDown.zw = gatherG(coord + vec2(0.0, HOOKED_pt.y)).yx;
        float mean = (left.y + left.z + right.x + right.w) * 0.25;
        left -= vec4(mean);
        right -= vec4(mean);
        upDown -= vec4(mean);
        float cw = color.g - mean;
        float sum = abs(left.x) + abs(left.y) + abs(left.z) + abs(left.w) +
                    abs(right.x) + abs(right.y) + abs(right.z) + abs(right.w) +
                    abs(upDown.x) + abs(upDown.y) + abs(upDown.z) + abs(upDown.w);
        float stdv = 2.181818 / max(sum, 1.0e-6);
        vec2 aWY = weightY(pl.x, pl.y + 1.0, upDown.x, stdv);
        aWY += weightY(pl.x - 1.0, pl.y + 1.0, upDown.y, stdv);
        aWY += weightY(pl.x - 1.0, pl.y - 2.0, upDown.z, stdv);
        aWY += weightY(pl.x, pl.y - 2.0, upDown.w, stdv);
        aWY += weightY(pl.x + 1.0, pl.y - 1.0, left.x, stdv);
        aWY += weightY(pl.x, pl.y - 1.0, left.y, stdv);
        aWY += weightY(pl.x, pl.y, left.z, stdv);
        aWY += weightY(pl.x + 1.0, pl.y, left.w, stdv);
        aWY += weightY(pl.x - 1.0, pl.y - 1.0, right.x, stdv);
        aWY += weightY(pl.x - 2.0, pl.y - 1.0, right.y, stdv);
        aWY += weightY(pl.x - 2.0, pl.y, right.z, stdv);
        aWY += weightY(pl.x - 1.0, pl.y, right.w, stdv);
        float finalY = aWY.y / aWY.x;
        float maxY = max(max(left.y, left.z), max(right.x, right.w));
        float minY = min(min(left.y, left.z), min(right.x, right.w));
        finalY = clamp(EdgeSharpness * finalY, minY, maxY);
        float deltaY = clamp(finalY - cw, -23.0 / 255.0, 23.0 / 255.0);
        color.rgb = clamp(color.rgb + vec3(deltaY), 0.0, 1.0);
    }
    return vec4(color.rgb, alpha);
}
