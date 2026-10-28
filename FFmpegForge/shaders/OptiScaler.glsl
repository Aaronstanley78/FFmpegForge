// FFmpeg Forge — OptiScaler output stage (mpv / libplacebo user shader)
//
// GLSL port of OptiScaler's open-source post-processing:
//   * RCAS with OptiScaler's HDR-safe limiter   (OptiScaler/shaders/rcas/precompile/rcas.hlsl,
//                                               itself based on RdenBlaauwen/RCAS-for-ReShade, from AMD FidelityFX RCAS)
//   * Output Scaling downscalers                (OptiScaler/shaders/output_scaling/OS_Common.h):
//       Bicubic, Catmull-Rom, Lanczos 2, Lanczos 3, Kaiser 2, Kaiser 3, Magic Kernel
// OptiScaler — https://github.com/optiscaler/OptiScaler — GNU GPL v3.0. This file is GPL v3.0 too.
//
// The upscaler before this stage renders the picture at (output × multiplier); RCAS sharpens it at that
// supersampled size and the downscaler brings it to the output size, as OptiScaler's Output Scaling does.

//!DESC OptiScaler RCAS
//!HOOK MAIN
//!BIND HOOKED
//!WHEN {{OPTI_SHARP_WHEN}}

#define OPTI_SHARPNESS {{OPTI_SHARPNESS}}
#define OPTI_CONTRAST  {{OPTI_CONTRAST}}

vec4 hook()
{
    vec4 ee = HOOKED_texOff(vec2(0.0, 0.0));
    vec3 e = ee.rgb;
    vec3 b = HOOKED_texOff(vec2(0.0, -1.0)).rgb;
    vec3 d = HOOKED_texOff(vec2(-1.0, 0.0)).rgb;
    vec3 f = HOOKED_texOff(vec2(1.0, 0.0)).rgb;
    vec3 h = HOOKED_texOff(vec2(0.0, 1.0)).rgb;

    // only normalize HDR neighbourhoods down
    float localScale = max(max(e.r, max(e.g, e.b)),
                       max(max(b.r, max(b.g, b.b)),
                       max(max(d.r, max(d.g, d.b)),
                       max(max(f.r, max(f.g, f.b)), max(h.r, max(h.g, h.b))))));
    localScale = max(localScale, 1.0);
    vec3 en = max(e / localScale, 0.0), bn = max(b / localScale, 0.0), dn = max(d / localScale, 0.0);
    vec3 fn = max(f / localScale, 0.0), hn = max(h / localScale, 0.0);

    vec3 minRGB = min(min(bn, dn), min(fn, hn));
    vec3 maxRGB = max(max(bn, dn), max(fn, hn));
    vec2 peakC = vec2(1.0, -4.0);
    vec3 hitMin = minRGB / max(4.0 * maxRGB, vec3(1e-5));
    vec3 hitMax = (peakC.xxx - maxRGB) / max(4.0 * minRGB + peakC.yyy, vec3(-1e-5));
    vec3 lobeRGB = max(-hitMin, hitMax);
    float lobe = max(-0.1875, min(max(lobeRGB.r, max(lobeRGB.g, lobeRGB.b)), 0.0)) * float(OPTI_SHARPNESS);

    if (float(OPTI_CONTRAST) != 0.0) {
        vec3 amp = clamp(min(minRGB, 2.0 - maxRGB) / max(maxRGB, vec3(1e-5)), 0.0, 1.0);
        amp = inversesqrt(max(amp, vec3(1e-5)));
        float peak = -3.0 * float(OPTI_CONTRAST) + 8.0;
        float contrastFactor = 1.0 / max(amp.g * peak, 1.0);
        lobe *= mix(1.0, contrastFactor, clamp(float(OPTI_CONTRAST), 0.0, 1.0));
    }

    float rcpL = 1.0 / (4.0 * lobe + 1.0);
    vec3 outc = (((bn + dn + fn + hn) * lobe + en) * rcpL) * localScale;
    return vec4(outc, ee.a);
}

//!DESC OptiScaler Output Scaling ({{OPTI_KERNEL_NAME}})
//!HOOK MAIN
//!BIND HOOKED
//!WIDTH OUTPUT.w
//!HEIGHT OUTPUT.h
//!WHEN OUTPUT.w HOOKED.w < OUTPUT.h HOOKED.h < +

// 1 Bicubic (Keys A=-0.6)  2 Catmull-Rom (A=-0.45)  3 Lanczos2  4 Lanczos3  5 Kaiser2  6 Kaiser3  7 Magic Kernel
#define KERNEL {{OPTI_KERNEL}}
#define PI 3.1415926535

vec3 px(ivec2 p)
{
    ivec2 sz = ivec2(HOOKED_size);
    p = clamp(p, ivec2(0), sz - 1);
    return HOOKED_tex((vec2(p) + 0.5) * HOOKED_pt).rgb;
}

float sinc1(float x) { x *= PI; return abs(x) < 1e-5 ? 1.0 : sin(x) / x; }

float I0(float x)
{
    float ax = abs(x);
    if (ax < 3.75) {
        float t = x / 3.75; float t2 = t * t;
        return 1.0 + t2 * (3.5156229 + t2 * (3.0899424 + t2 * (1.2067492 + t2 * (0.2659732 + t2 * (0.0360768 + t2 * 0.0045813)))));
    }
    float t = 3.75 / ax;
    return (exp(ax) / sqrt(ax)) * (0.39894228 + t * (0.01328592 + t * (0.00225319 + t * (-0.00157565 + t * (0.00916281
           + t * (-0.02057706 + t * (0.02635537 + t * (-0.01647633 + t * 0.00392377))))))));
}

float kaiser(float x, float a, float beta)
{
    float ax = abs(x);
    if (ax >= a) return 0.0;
    float r = ax / a;
    return sinc1(x) * I0(beta * sqrt(clamp(1.0 - r * r, 0.0, 1.0))) / I0(beta);
}

float keys(float x, float A)
{
    x = abs(x); float x2 = x * x, x3 = x2 * x;
    if (x < 1.0) return (A + 2.0) * x3 - (A + 3.0) * x2 + 1.0;
    if (x < 2.0) return A * x3 - 5.0 * A * x2 + 8.0 * A * x - 4.0 * A;
    return 0.0;
}

float wfun(float x)
{
#if KERNEL == 1
    return keys(x, -0.6);
#elif KERNEL == 2
    return keys(x, -0.45);
#elif KERNEL == 3
    float ax = abs(x); return ax >= 2.0 ? 0.0 : sinc1(x) * sinc1(x / 2.0);
#elif KERNEL == 4
    float ax = abs(x); return ax >= 3.0 ? 0.0 : sinc1(x) * sinc1(x / 3.0);
#elif KERNEL == 5
    return kaiser(x, 2.0, 5.0);
#else
    return kaiser(x, 3.0, 6.0);
#endif
}

float magic(float x)
{
    float ax = abs(x);
    if (ax >= 1.5) return 0.0;
    if (x <= -0.5) { float t = x + 1.5; return 0.5 * t * t; }
    if (x < 0.5) return 0.75 - x * x;
    float t = x - 1.5; return 0.5 * t * t;
}

vec4 hook()
{
    vec2 srcSize = HOOKED_size;
    vec2 dstSize = target_size;
    vec2 dst = HOOKED_pos * dstSize;            // output pixel centre
    float alpha = HOOKED_tex(HOOKED_pos).a;
#if KERNEL == 7
    // Magic Kernel, area-correct: taps where |k*(x+0.5) - o| < 1.5, k = dst/src
    vec2 k = dstSize / srcSize;
    ivec2 x0 = ivec2(ceil((dst - 1.5) / k - 0.5));
    ivec2 x1 = ivec2(floor((dst + 1.5) / k - 0.5));
    ivec2 n = clamp(x1 - x0 + 1, ivec2(1), ivec2(12));
    vec3 acc = vec3(0.0); float sw = 0.0;
    for (int j = 0; j < 12; j++) {
        if (j >= n.y) break;
        float wy = magic(k.y * (float(x0.y + j) + 0.5) - dst.y);
        for (int i = 0; i < 12; i++) {
            if (i >= n.x) break;
            float w = magic(k.x * (float(x0.x + i) + 0.5) - dst.x) * wy;
            acc += px(ivec2(x0.x + i, x0.y + j)) * w;
            sw += w;
        }
    }
    return vec4(acc / max(sw, 1e-6), alpha);
#else
  #if KERNEL == 4 || KERNEL == 6
    const int R = 3;
  #else
    const int R = 2;
  #endif
    vec2 srcPos = dst * (srcSize / dstSize) - 0.5;
    vec2 ip = floor(srcPos);
    vec2 fr = srcPos - ip;
    ivec2 base = ivec2(ip) - (R - 1);
    float wx[6]; float wy[6]; float sx = 0.0, sy = 0.0;
    for (int i = 0; i < 2 * R; i++) {
        wx[i] = wfun(float(i - (R - 1)) - fr.x); sx += wx[i];
        wy[i] = wfun(float(i - (R - 1)) - fr.y); sy += wy[i];
    }
    vec3 acc = vec3(0.0), mn = vec3(1e30), mx = vec3(-1e30);
    for (int j = 0; j < 2 * R; j++) {
        for (int i = 0; i < 2 * R; i++) {
            vec3 s = px(base + ivec2(i, j));
            mn = min(mn, s); mx = max(mx, s);
            acc += s * (wx[i] / sx) * (wy[j] / sy);
        }
    }
    return vec4(clamp(acc, mn, mx), alpha);
#endif
}
