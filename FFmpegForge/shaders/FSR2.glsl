// FFmpeg Forge — temporal upscaler following the design of AMD FidelityFX Super Resolution 2
// (mpv / libplacebo user shader, Vulkan compute).
//
// AMD's FSR 2 SDK needs the motion vectors and depth buffer of a game engine. A video file has
// neither, so this implementation estimates the motion from the video itself and then follows the
// FSR 2 pipeline: reprojection of an output-resolution history, rectification of that history
// against the current frame's neighbourhood (YCoCg variance clipping), accumulation, and RCAS
// sharpening (the RCAS pass is AMD's, see FSR.glsl). Experimental: wrong motion estimates can leave
// short trails on fast or transparent motion.
//
// Template values set by FFmpeg Forge: {{IN_W}} {{IN_H}} {{BW}} {{BH}} {{OUT_W}} {{OUT_H}}
// History state lives in STORAGE textures, which libplacebo keeps from one frame to the next.

//!BUFFER FSR2_STATE
//!VAR uint fsr2_frames
//!STORAGE
00000000

//!TEXTURE FSR2_PREVY
//!SIZE {{IN_W}} {{IN_H}}
//!FORMAT r16f
//!STORAGE

//!TEXTURE FSR2_MV
//!SIZE {{BW}} {{BH}}
//!FORMAT rgba16f
//!STORAGE

//!TEXTURE FSR2_MV2
//!SIZE {{BW}} {{BH}}
//!FORMAT rgba16f
//!STORAGE

//!TEXTURE FSR2_HA
//!SIZE {{OUT_W}} {{OUT_H}}
//!FORMAT rgba16f
//!STORAGE

//!TEXTURE FSR2_HB
//!SIZE {{OUT_W}} {{OUT_H}}
//!FORMAT rgba16f
//!STORAGE

//!DESC FSR 2 (FFmpeg Forge) motion estimation
//!HOOK MAIN
//!BIND HOOKED
//!BIND FSR2_PREVY
//!BIND FSR2_MV
//!BIND FSR2_STATE
//!SAVE FSR2_ME
//!WIDTH {{BW}}
//!HEIGHT {{BH}}
//!COMPONENTS 1
//!COMPUTE 8 8

const ivec2 INSZ = ivec2({{IN_W}}, {{IN_H}});

float curY(ivec2 p)
{
    p = clamp(p, ivec2(0), min(ivec2(HOOKED_size), INSZ) - 1);
    vec3 c = HOOKED_mul * texelFetch(HOOKED_raw, p, 0).rgb;
    return dot(c, vec3(0.299, 0.587, 0.114));
}

float prevY(ivec2 p)
{
    p = clamp(p, ivec2(0), INSZ - 1);
    return imageLoad(FSR2_PREVY, p).x;
}

float cur[64];      // this invocation's 8x8 block (private)

// block SAD for a candidate vector v (current pixel p came from p - v in the previous frame)
float prevYb(vec2 q)
{
    ivec2 i = ivec2(floor(q)); vec2 f = q - vec2(i);   // q in pixel-index space
    return mix(mix(prevY(i), prevY(i + ivec2(1, 0)), f.x), mix(prevY(i + ivec2(0, 1)), prevY(i + ivec2(1, 1)), f.x), f.y);
}

float sad(ivec2 o, ivec2 v, int stepPx)
{
    float s = 0.0;
    for (int y = 0; y < 8; y += stepPx)
        for (int x = 0; x < 8; x += stepPx)
            s += abs(cur[y * 8 + x] - prevY(o + ivec2(x, y) - v));
    return s * float(stepPx * stepPx);
}

void hook()
{
    ivec2 b = ivec2(gl_GlobalInvocationID.xy);
    if (b.x >= {{BW}} || b.y >= {{BH}}) return;
    ivec2 o = b * 8;
    float mean = 0.0;
    for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++) { cur[y * 8 + x] = curY(o + ivec2(x, y)); mean += cur[y * 8 + x]; }
    mean /= 64.0;
    float tex = 0.0;                                  // block texture: flat blocks keep the zero vector
    for (int i = 0; i < 64; i++) tex += abs(cur[i] - mean);

    vec2 vOut = vec2(0.0);
    float conf = 0.0;
    if (fsr2_frames > 0u) {
        const float lambda = 0.004;                   // small bias towards smooth / zero motion
        ivec2 best = ivec2(0);
        float bestS = sad(o, ivec2(0), 1);
        // temporal predictor: this block's previous vector
        ivec2 pred = ivec2(round(imageLoad(FSR2_MV, b).xy));
        float sp = sad(o, pred, 1) + lambda * 64.0 * length(vec2(pred));
        if (sp < bestS) { bestS = sp; best = pred; }
        // coarse search ±24 px, step 4 (subsampled block)
        ivec2 cbest = best; float cS = 1e9;
        for (int y = -24; y <= 24; y += 4)
            for (int x = -24; x <= 24; x += 4) {
                ivec2 v = ivec2(x, y);
                float s = sad(o, v, 2) + lambda * 64.0 * length(vec2(v));
                if (s < cS) { cS = s; cbest = v; }
            }
        float cf = sad(o, cbest, 1) + lambda * 64.0 * length(vec2(cbest));
        if (cf < bestS) { bestS = cf; best = cbest; }
        // refinement ±3 px around the best vector
        ivec2 c0 = best;
        for (int y = -3; y <= 3; y++)
            for (int x = -3; x <= 3; x++) {
                ivec2 v = c0 + ivec2(x, y);
                float s = sad(o, v, 1) + lambda * 64.0 * length(vec2(v));
                if (s < bestS) { bestS = s; best = v; }
            }
        // sub-pixel refinement: Lucas-Kanade (2 Gauss-Newton steps on the bilinear previous frame)
        vec2 v = vec2(best);
        for (int it = 0; it < 2; it++) {
            mat2 G = mat2(0.0); vec2 bv = vec2(0.0);
            for (int y = -2; y < 10; y++)
                for (int x = -2; x < 10; x++) {
                    vec2 q = vec2(o + ivec2(x, y)) - v;
                    float pc = prevYb(q);
                    vec2 g = vec2(prevYb(q + vec2(0.5, 0.0)) - prevYb(q - vec2(0.5, 0.0)),
                                  prevYb(q + vec2(0.0, 0.5)) - prevYb(q - vec2(0.0, 0.5)));
                    float r = curY(o + ivec2(x, y)) - pc;
                    G += outerProduct(g, g); bv += g * r;
                }
            float det = G[0][0] * G[1][1] - G[0][1] * G[1][0];
            if (abs(det) < 1e-6) break;
            vec2 dv = -(inverse(G) * bv);
            v += clamp(dv, -0.75, 0.75);
        }
        float s0 = sad(o, best, 1);
        vOut = clamp(v, vec2(best) - 1.0, vec2(best) + 1.0);
        // confidence: low residual relative to the block's own texture
        conf = clamp(1.0 - s0 / max(0.25 * tex + 0.6, 1e-3), 0.0, 1.0);
        if (tex < 0.4) { vOut = vec2(0.0); conf = -1.0; }  // flat block: filled from its neighbours below
    }
    imageStore(FSR2_MV, b, vec4(vOut, conf, 0.0));
    imageStore(out_image, b, vec4(conf));
}

//!DESC FSR 2 (FFmpeg Forge) motion field fill
//!HOOK MAIN
//!BIND HOOKED
//!BIND FSR2_ME
//!BIND FSR2_MV
//!BIND FSR2_MV2
//!SAVE FSR2_MF
//!WIDTH {{BW}}
//!HEIGHT {{BH}}
//!COMPONENTS 1
//!COMPUTE 8 8

void hook()
{
    ivec2 b = ivec2(gl_GlobalInvocationID.xy);
    if (b.x >= {{BW}} || b.y >= {{BH}}) return;
    vec4 m = imageLoad(FSR2_MV, b) + 0.0 * FSR2_ME_tex(vec2(0.5));
    if (m.z < 0.0) {
        // flat block: average of the textured blocks around it (motion of the surrounding content)
        vec3 acc = vec3(0.0); float ws = 0.0;
        for (int y = -3; y <= 3; y++)
            for (int x = -3; x <= 3; x++) {
                ivec2 q = b + ivec2(x, y);
                if (any(lessThan(q, ivec2(0))) || q.x >= {{BW}} || q.y >= {{BH}}) continue;
                vec4 n = imageLoad(FSR2_MV, q);
                if (n.z < 0.0) continue;
                float w = (0.2 + n.z) / (1.0 + float(x * x + y * y));
                acc += vec3(n.xy, n.z) * w; ws += w;
            }
        m = ws > 0.0 ? vec4(acc / ws, 0.0) : vec4(0.0, 0.0, 1.0, 0.0);
        m.z = ws > 0.0 ? acc.z / ws : 1.0;
    }
    imageStore(FSR2_MV2, b, m);
    imageStore(out_image, b, vec4(m.z));
}

//!DESC FSR 2 (FFmpeg Forge) keep luma for the next frame
//!HOOK MAIN
//!BIND HOOKED
//!BIND FSR2_MF
//!BIND FSR2_PREVY
//!BIND FSR2_STATE
//!SAVE FSR2_Y
//!COMPONENTS 1
//!COMPUTE 16 16

void hook()
{
    ivec2 p = ivec2(gl_GlobalInvocationID.xy);
    if (p.x >= {{IN_W}} || p.y >= {{IN_H}} || p.x >= int(HOOKED_size.x) || p.y >= int(HOOKED_size.y)) return;
    vec3 c = HOOKED_mul * texelFetch(HOOKED_raw, p, 0).rgb;
    float y = dot(c, vec3(0.299, 0.587, 0.114)) + 0.0 * FSR2_MF_tex(vec2(0.5)).x;   // runs after the motion passes
    imageStore(FSR2_PREVY, p, vec4(y));
    imageStore(out_image, p, vec4(y));
    if (p == ivec2(0)) fsr2_frames = fsr2_frames + 1u;    // one frame done (read by the next passes)
}

//!DESC FSR 2 (FFmpeg Forge) reproject, rectify and accumulate
//!HOOK MAIN
//!BIND HOOKED
//!BIND FSR2_Y
//!BIND FSR2_MV2
//!BIND FSR2_HA
//!BIND FSR2_HB
//!BIND FSR2_STATE
//!WHEN HOOKED.w {{IN_W}} >
//!COMPUTE 16 16

// Runs on the FSR 1 EASU output (output resolution): the spatial upscale of the current frame is
// the new sample, the reprojected history adds temporal stability and detail.
const ivec2 OUTSZ = ivec2({{OUT_W}}, {{OUT_H}});
const ivec2 BSZ = ivec2({{BW}}, {{BH}});
const vec2 INSZ = vec2({{IN_W}}, {{IN_H}});
const float MAX_ACC = {{MAX_ACC}};

vec3 rgb2ycocg(vec3 c) { return vec3(0.25 * c.r + 0.5 * c.g + 0.25 * c.b, 0.5 * c.r - 0.5 * c.b, -0.25 * c.r + 0.5 * c.g - 0.25 * c.b); }
vec3 ycocg2rgb(vec3 c) { return vec3(c.x + c.y - c.z, c.x + c.z, c.x - c.y - c.z); }

vec3 curRGB(ivec2 p)
{
    p = clamp(p, ivec2(0), ivec2(HOOKED_size) - 1);
    return HOOKED_mul * texelFetch(HOOKED_raw, p, 0).rgb + 0.0 * FSR2_Y_tex(vec2(0.5)).x;
}

vec4 histLoad(ivec2 p, bool readA)
{
    p = clamp(p, ivec2(0), OUTSZ - 1);
    return readA ? imageLoad(FSR2_HA, p) : imageLoad(FSR2_HB, p);
}

// Catmull-Rom history sample (16 taps), weight channel bilinear
vec4 histSample(vec2 pos, bool readA)
{
    vec2 sp = pos - 0.5;
    vec2 f = fract(sp);
    ivec2 i0 = ivec2(floor(sp)) - 1;
    vec2 w0 = f * (-0.5 + f * (1.0 - 0.5 * f));
    vec2 w1 = 1.0 + f * f * (-2.5 + 1.5 * f);
    vec2 w2 = f * (0.5 + f * (2.0 - 1.5 * f));
    vec2 w3 = f * f * (-0.5 + 0.5 * f);
    float wx[4] = float[4](w0.x, w1.x, w2.x, w3.x);
    float wy[4] = float[4](w0.y, w1.y, w2.y, w3.y);
    vec3 acc = vec3(0.0);
    for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
            acc += histLoad(i0 + ivec2(x, y), readA).rgb * (wx[x] * wy[y]);
    ivec2 ib = ivec2(floor(sp));
    float a = mix(mix(histLoad(ib, readA).a, histLoad(ib + ivec2(1, 0), readA).a, f.x),
                  mix(histLoad(ib + ivec2(0, 1), readA).a, histLoad(ib + ivec2(1, 1), readA).a, f.x), f.y);
    return vec4(acc, a);
}

vec3 mvAt(vec2 pin)
{
    vec2 bp = pin / 8.0 - 0.5;
    ivec2 b0 = ivec2(floor(bp));
    vec2 f = fract(bp);
    vec3 m00 = imageLoad(FSR2_MV2, clamp(b0, ivec2(0), BSZ - 1)).xyz;
    vec3 m10 = imageLoad(FSR2_MV2, clamp(b0 + ivec2(1, 0), ivec2(0), BSZ - 1)).xyz;
    vec3 m01 = imageLoad(FSR2_MV2, clamp(b0 + ivec2(0, 1), ivec2(0), BSZ - 1)).xyz;
    vec3 m11 = imageLoad(FSR2_MV2, clamp(b0 + ivec2(1, 1), ivec2(0), BSZ - 1)).xyz;
    return mix(mix(m00, m10, f.x), mix(m01, m11, f.x), f.y);
}

void hook()
{
    ivec2 id = ivec2(gl_GlobalInvocationID.xy);
    if (id.x >= OUTSZ.x || id.y >= OUTSZ.y) return;
    vec2 scale = INSZ / vec2(OUTSZ);                    // input px per output px
    vec2 pin = (vec2(id) + 0.5) * scale;

    vec3 cur = curRGB(id);
    vec3 m1 = vec3(0.0), m2 = vec3(0.0);
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++) {
            vec3 c = rgb2ycocg(curRGB(id + ivec2(x, y)));
            m1 += c; m2 += c * c;
        }
    m1 /= 9.0; m2 /= 9.0;
    vec3 sigma = sqrt(max(m2 - m1 * m1, 0.0));

    uint fn = fsr2_frames;                              // 1 = first frame of the video
    bool readA = (fn % 2u) == 0u;                       // frame 1 writes A, frame 2 reads A, ...
    vec3 outc = cur;
    float newW = 1.0;
    vec3 mv = mvAt(pin);
    vec2 hp = vec2(id) + 0.5 - mv.xy / scale;
    if (fn > 1u && all(greaterThanEqual(hp, vec2(0.0))) && all(lessThan(hp, vec2(OUTSZ)))) {
        vec4 h = histSample(hp, readA);
        vec3 hy = rgb2ycocg(clamp(h.rgb, 0.0, 1.0));
        vec3 cy = rgb2ycocg(cur);
        vec3 bmin = m1 - {{CLIP}} * sigma - 0.003, bmax = m1 + {{CLIP}} * sigma + 0.003;
        vec3 center = 0.5 * (bmin + bmax), ext = max(0.5 * (bmax - bmin), vec3(1e-4));
        vec3 d = hy - center;
        vec3 ad = abs(d / ext);
        float ma = max(ad.x, max(ad.y, ad.z));
        vec3 hc = ma > 1.0 ? center + d / ma : hy;
        float moved = length((hc - hy) / (ext + 0.02));
        float hw = min(h.a, MAX_ACC) * clamp(1.0 - 0.6 * moved, 0.0, 1.0) * mix(0.3, 1.0, mv.z);
        newW = min(hw + 1.0, MAX_ACC);
        outc = clamp(ycocg2rgb(mix(hc, cy, 1.0 / newW)), 0.0, 1.0);
    }
    vec4 st = vec4(outc, newW);
    if (readA) imageStore(FSR2_HB, id, st); else imageStore(FSR2_HA, id, st);
    imageStore(out_image, id, vec4(outc, 1.0));
}
