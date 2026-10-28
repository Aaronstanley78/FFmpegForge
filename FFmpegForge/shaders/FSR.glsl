// AMD FidelityFX Super Resolution 1.0 (EASU + RCAS) for libplacebo / mpv.
// Port for FFmpeg Forge of AMD's ffx_fsr1.h (Copyright (c) 2021 Advanced Micro Devices, Inc. — MIT License).
// Template values replaced by FFmpeg Forge: {{RCAS_STOPS}} (0 = maximum sharpness).

//!DESC FSR 1.0 EASU (edge adaptive spatial upsampling)
//!HOOK MAIN
//!BIND HOOKED
//!WIDTH OUTPUT.w
//!HEIGHT OUTPUT.h
//!WHEN OUTPUT.w HOOKED.w 1.0 * > OUTPUT.h HOOKED.h 1.0 * > *

vec3 easuTap(vec2 p) { return HOOKED_tex((p + vec2(0.5)) * HOOKED_pt).rgb; }
float easuLuma(vec3 c) { return c.b * 0.5 + (c.r * 0.5 + c.g); }

void easuSet(inout vec2 dir, inout float len, float w, float lA, float lB, float lC, float lD, float lE)
{
    float dc = lD - lC;
    float cb = lC - lB;
    float lenX = max(abs(dc), abs(cb));
    lenX = 1.0 / max(lenX, 1.0e-8);
    float dirX = lD - lB;
    dir.x += dirX * w;
    lenX = clamp(abs(dirX) * lenX, 0.0, 1.0);
    lenX *= lenX;
    len += lenX * w;
    float ec = lE - lC;
    float ca = lC - lA;
    float lenY = max(abs(ec), abs(ca));
    lenY = 1.0 / max(lenY, 1.0e-8);
    float dirY = lE - lA;
    dir.y += dirY * w;
    lenY = clamp(abs(dirY) * lenY, 0.0, 1.0);
    lenY *= lenY;
    len += lenY * w;
}

void easuTapAcc(inout vec3 aC, inout float aW, vec2 off, vec2 dir, vec2 len, float lob, float clp, vec3 c)
{
    vec2 v;
    v.x = (off.x * dir.x) + (off.y * dir.y);
    v.y = (off.x * (-dir.y)) + (off.y * dir.x);
    v *= len;
    float d2 = v.x * v.x + v.y * v.y;
    d2 = min(d2, clp);
    float wB = (2.0 / 5.0) * d2 - 1.0;
    float wA = lob * d2 - 1.0;
    wB *= wB;
    wA *= wA;
    wB = (25.0 / 16.0) * wB - (25.0 / 16.0 - 1.0);
    float w = wB * wA;
    aC += c * w;
    aW += w;
}

vec4 hook()
{
    vec2 pp = HOOKED_pos * HOOKED_size - vec2(0.5);
    vec2 fp = floor(pp);
    pp -= fp;
    //    b c
    //  e f g h
    //  i j k l
    //    n o
    vec3 b = easuTap(fp + vec2(0.0, -1.0));
    vec3 c = easuTap(fp + vec2(1.0, -1.0));
    vec3 e = easuTap(fp + vec2(-1.0, 0.0));
    vec3 f = easuTap(fp);
    vec3 g = easuTap(fp + vec2(1.0, 0.0));
    vec3 h = easuTap(fp + vec2(2.0, 0.0));
    vec3 i = easuTap(fp + vec2(-1.0, 1.0));
    vec3 j = easuTap(fp + vec2(0.0, 1.0));
    vec3 k = easuTap(fp + vec2(1.0, 1.0));
    vec3 l = easuTap(fp + vec2(2.0, 1.0));
    vec3 n = easuTap(fp + vec2(0.0, 2.0));
    vec3 o = easuTap(fp + vec2(1.0, 2.0));
    float bL = easuLuma(b), cL = easuLuma(c), eL = easuLuma(e), fL = easuLuma(f), gL = easuLuma(g), hL = easuLuma(h);
    float iL = easuLuma(i), jL = easuLuma(j), kL = easuLuma(k), lL = easuLuma(l), nL = easuLuma(n), oL = easuLuma(o);

    vec2 dir = vec2(0.0);
    float len = 0.0;
    easuSet(dir, len, (1.0 - pp.x) * (1.0 - pp.y), bL, eL, fL, gL, jL);
    easuSet(dir, len, pp.x * (1.0 - pp.y), cL, fL, gL, hL, kL);
    easuSet(dir, len, (1.0 - pp.x) * pp.y, fL, iL, jL, kL, nL);
    easuSet(dir, len, pp.x * pp.y, gL, jL, kL, lL, oL);

    vec2 dir2 = dir * dir;
    float dirR = dir2.x + dir2.y;
    bool zro = dirR < (1.0 / 32768.0);
    dirR = inversesqrt(max(dirR, 1.0e-12));
    dirR = zro ? 1.0 : dirR;
    dir.x = zro ? 1.0 : dir.x;
    dir *= vec2(dirR);
    len = len * 0.5;
    len *= len;
    float stretch = (dir.x * dir.x + dir.y * dir.y) / max(abs(dir.x), abs(dir.y));
    vec2 len2 = vec2(1.0 + (stretch - 1.0) * len, 1.0 - 0.5 * len);
    float lob = 0.5 + ((1.0 / 4.0 - 0.04) - 0.5) * len;
    float clp = 1.0 / lob;

    vec3 min4 = min(min(f, g), min(j, k));
    vec3 max4 = max(max(f, g), max(j, k));
    vec3 aC = vec3(0.0);
    float aW = 0.0;
    easuTapAcc(aC, aW, vec2(0.0, -1.0) - pp, dir, len2, lob, clp, b);
    easuTapAcc(aC, aW, vec2(1.0, -1.0) - pp, dir, len2, lob, clp, c);
    easuTapAcc(aC, aW, vec2(-1.0, 1.0) - pp, dir, len2, lob, clp, i);
    easuTapAcc(aC, aW, vec2(0.0, 1.0) - pp, dir, len2, lob, clp, j);
    easuTapAcc(aC, aW, vec2(0.0, 0.0) - pp, dir, len2, lob, clp, f);
    easuTapAcc(aC, aW, vec2(-1.0, 0.0) - pp, dir, len2, lob, clp, e);
    easuTapAcc(aC, aW, vec2(1.0, 1.0) - pp, dir, len2, lob, clp, k);
    easuTapAcc(aC, aW, vec2(2.0, 1.0) - pp, dir, len2, lob, clp, l);
    easuTapAcc(aC, aW, vec2(2.0, 0.0) - pp, dir, len2, lob, clp, h);
    easuTapAcc(aC, aW, vec2(1.0, 0.0) - pp, dir, len2, lob, clp, g);
    easuTapAcc(aC, aW, vec2(1.0, 2.0) - pp, dir, len2, lob, clp, o);
    easuTapAcc(aC, aW, vec2(0.0, 2.0) - pp, dir, len2, lob, clp, n);
    vec3 pix = min(max4, max(min4, aC / aW));
    return vec4(pix, HOOKED_tex(HOOKED_pos).a);
}

//!DESC FSR 1.0 RCAS (robust contrast adaptive sharpening)
//!HOOK MAIN
//!BIND HOOKED

#define RCAS_STOPS {{RCAS_STOPS}}
#define FSR_RCAS_LIMIT (0.25 - (1.0 / 16.0))

vec4 hook()
{
    vec3 b = HOOKED_texOff(vec2(0.0, -1.0)).rgb;
    vec3 d = HOOKED_texOff(vec2(-1.0, 0.0)).rgb;
    vec4 ee = HOOKED_texOff(vec2(0.0, 0.0));
    vec3 e = ee.rgb;
    vec3 f = HOOKED_texOff(vec2(1.0, 0.0)).rgb;
    vec3 h = HOOKED_texOff(vec2(0.0, 1.0)).rgb;
    // FSR_RCAS_DENOISE is off, as in AMD's default and in Lossless Scaling
    vec3 mn4 = min(min(b, d), min(f, h));
    vec3 mx4 = max(max(b, d), max(f, h));
    vec2 peakC = vec2(1.0, -4.0);
    vec3 hitMin = min(mn4, e) / (4.0 * mx4 + 1.0e-8);
    vec3 hitMax = (peakC.x - max(mx4, e)) / (4.0 * mn4 + peakC.y);
    vec3 lobeRGB = max(-hitMin, hitMax);
    float sharp = exp2(-float(RCAS_STOPS));
    float lobe = max(-FSR_RCAS_LIMIT, min(max(max(lobeRGB.r, lobeRGB.g), lobeRGB.b), 0.0)) * sharp;
    float rcpL = 1.0 / (4.0 * lobe + 1.0);
    vec3 pix = (lobe * b + lobe * d + lobe * h + lobe * f + e) * rcpL;
    return vec4(clamp(pix, 0.0, 1.0), ee.a);
}
