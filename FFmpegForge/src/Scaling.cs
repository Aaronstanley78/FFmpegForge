using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FFmpegForge
{
    /// "Scale" (Échelle): spatial upscalers like the ones offered by game upscaling tools,
    /// implemented with open-source algorithms only:
    ///   GPU (FFmpeg libplacebo + Vulkan, shaders embedded in FFmpegForge.exe):
    ///     AMD FSR 1 (EASU + RCAS), NVIDIA Image Scaling, Snapdragon GSR, Anime4K, EWA Lanczos Sharp
    ///   CPU/GPU: xBR, hqx, Integer, Nearest neighbor, Sharp bilinear, BCAS (bicubic + CAS),
    ///     Bicubic, Lanczos, Spline
    public static class Scaling
    {
        public class ScaleType
        {
            public string Key, Label; public bool GpuOnly, Sharpness;
            public ScaleType(string k, string l, bool gpu, bool sharp) { Key = k; Label = l; GpuOnly = gpu; Sharpness = sharp; }
            public override string ToString() { return Label; }
        }

        public static readonly List<ScaleType> Types = new List<ScaleType> {
            new ScaleType("fsr",     "FSR 1 — AMD FidelityFX Super Resolution", true, true),
            new ScaleType("fsr2",    "FSR 2 — temporal upscaling (experimental)", true, true),
            new ScaleType("fsr3",    "FSR 3 — temporal + frame generation ×2 (experimental)", true, true),
            new ScaleType("nis",     "NIS — NVIDIA Image Scaling", true, true),
            new ScaleType("sgsr",    "SGSR — Snapdragon Game Super Resolution", true, true),
            new ScaleType("anime4k", "Anime4K — anime / cartoons (CNN)", true, false),
            new ScaleType("xbr",     "xBR — pixel art", false, false),
            new ScaleType("hqx",     "hqx — pixel art", false, false),
            new ScaleType("integer", "Integer — exact pixel multiples", false, false),
            new ScaleType("nearest", "Nearest neighbor", false, false),
            new ScaleType("sharpbilinear", "Sharp bilinear", false, false),
            new ScaleType("bcas",    "BCAS — bicubic + contrast adaptive sharpening", false, true),
            new ScaleType("ewa",     "EWA Lanczos Sharp (high quality)", true, false),
            new ScaleType("lanczos", "Lanczos", false, false),
            new ScaleType("bicubic", "Bicubic", false, false),
            new ScaleType("spline",  "Spline", false, false),
        };

        public static ScaleType Get(string key) { return Types.FirstOrDefault(t => t.Key == key) ?? Types[0]; }

        public static bool HasLibplacebo { get { return FilterCatalog.Has("libplacebo"); } }

        static string Res(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var st = asm.GetManifestResourceStream(name))
            {
                if (st == null) throw new InvalidOperationException("Embedded shader missing: " + name);
                using (var r = new StreamReader(st, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        static string Inv(double d) { return Formats.Inv(d); }

        /// FSR 2 / FSR 3 (temporal): motion estimation + FSR 1 EASU + history accumulation + RCAS, sized for this job.
        static string WriteTemporal(double sharp, int inW, int inH, int outW, int outH)
        {
            string t = Res("FSR2.glsl"), f = Res("FSR.glsl");
            int cut = f.IndexOf("//!DESC FSR 1.0 RCAS");
            string easu = f.Substring(0, cut), rcas = f.Substring(cut).Replace("{{RCAS_STOPS}}", Inv(Math.Round(2.0 * (1.0 - sharp), 4)));
            int a = t.IndexOf("//!DESC FSR 2 (FFmpeg Forge) reproject");
            t = t.Substring(0, a) + easu + "\n" + t.Substring(a) + "\n" + rcas;
            t = t.Replace("{{IN_W}}", inW.ToString()).Replace("{{IN_H}}", inH.ToString())
                 .Replace("{{BW}}", ((inW + 7) / 8).ToString()).Replace("{{BH}}", ((inH + 7) / 8).ToString())
                 .Replace("{{OUT_W}}", outW.ToString()).Replace("{{OUT_H}}", outH.ToString())
                 .Replace("{{MAX_ACC}}", "6.0").Replace("{{CLIP}}", "1.25");
            return SaveShader("fsr2", t);
        }

        /// Writes the shader with its parameters baked in; returns the file path.
        static string WriteShader(string key, double sharp)
        {
            string text;
            switch (key)
            {
                case "fsr":
                    text = Res("FSR.glsl").Replace("{{RCAS_STOPS}}", Inv(Math.Round(2.0 * (1.0 - sharp), 4)));
                    break;
                case "nis":
                    {
                        double s = Math.Max(0, Math.Min(1, sharp)), sl = s - 0.5;
                        double maxS = sl >= 0 ? 1.25 : 1.75, minS = sl >= 0 ? 1.25 : 1.0, limS = sl >= 0 ? 1.25 : 1.0;
                        double smin = Math.Max(0, 0.4 + sl * minS * 1.2), smax = 1.6 + sl * maxS * 1.8;
                        double lmin = Math.Max(0.1, 0.14 + sl * limS * 0.32), lmax = 0.5 + sl * limS * 0.6;
                        text = Res("NIS.glsl")
                            .Replace("{{kDetectRatio}}", Inv(2 * 1127.0 / 1024.0)).Replace("{{kDetectThres}}", Inv(64.0 / 1024.0))
                            .Replace("{{kMinContrastRatio}}", "2.0").Replace("{{kRatioNorm}}", Inv(1.0 / 8.0))
                            .Replace("{{kSharpStartY}}", "0.45").Replace("{{kSharpScaleY}}", Inv(1.0 / 0.45))
                            .Replace("{{kSharpStrengthMin}}", Inv(smin)).Replace("{{kSharpStrengthScale}}", Inv(smax - smin))
                            .Replace("{{kSharpLimitMin}}", Inv(lmin)).Replace("{{kSharpLimitScale}}", Inv(lmax - lmin));
                        break;
                    }
                case "sgsr":
                    text = Res("SGSR.glsl").Replace("{{EDGE_SHARPNESS}}", Inv(1.0 + Math.Max(0, Math.Min(1, sharp))));
                    break;
                case "anime4k":
                    text = Res("Anime4K.glsl");
                    break;
                default: return null;
            }
            return SaveShader(key, text);
        }

        static string SaveShader(string key, string text)
        {
            // GLSL wants '.' decimals: values above are already invariant
            string dir = Path.Combine(Path.GetTempPath(), "FFmpegForge");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "scale_" + key + "_" + ((uint)text.GetHashCode()).ToString("x8") + ".glsl");
            if (!File.Exists(path)) File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        /// Builds the scaling filter chain. needsVulkan = the command must create a Vulkan device.
        public static string Filter(ConvSettings s, Job job, bool forImage, out bool needsVulkan, out string error)
        {
            needsVulkan = false; error = null;
            if (!s.ScaleEnabled && !s.OptiEnabled) return null;
            // OptiScaler alone (Scale unticked): EWA Lanczos Sharp to the source size or the Resolution setting
            var t = s.ScaleEnabled ? Get(s.ScaleType) : Get("ewa");
            if (forImage && (t.Key == "fsr2" || t.Key == "fsr3")) t = Get("fsr");     // a single picture has no motion: FSR 1
            double sharp = Math.Max(0, Math.Min(100, s.ScaleSharpness)) / 100.0;

            // output size: factor, or the Resolution setting when chosen
            int sw = job.Width, sh = job.Height;
            if (s.Rotate == 1 || s.Rotate == 3) { int tmp = sw; sw = sh; sh = tmp; }
            double f = !s.ScaleEnabled ? 1 : (s.ScaleFactor <= 0 ? 2 : s.ScaleFactor);
            if ((s.ResW > 0 || s.ResH > 0) && sw > 0 && sh > 0)
            {
                double fx = s.ResW > 0 ? (double)s.ResW / sw : 0, fy = s.ResH > 0 ? (double)s.ResH / sh : 0;
                if (fx > 0 && fy > 0) f = s.ScaleMode == 2 ? Math.Max(fx, fy) : (s.ScaleMode == 1 ? Math.Min(fx, fy) : 0);
                else f = Math.Max(fx, fy);
            }
            bool even = !forImage;
            double m = s.OptiEnabled ? Math.Max(1.0, Math.Min(3.0, s.OptiMult)) : 1.0;
            string W, H, W1, H1;   // final size, and the upscaler's size (final × OptiScaler multiplier)
            if (f > 0)
            {
                if (sw > 0 && sh > 0)
                {
                    int ow = (int)Math.Round(sw * f), oh = (int)Math.Round(sh * f);
                    if (even) { ow += ow % 2; oh += oh % 2; }
                    W = ow.ToString(); H = oh.ToString();
                    W1 = ((int)Math.Round(ow * m)).ToString(); H1 = ((int)Math.Round(oh * m)).ToString();
                }
                else
                {
                    W = "'trunc(iw*" + Inv(f) + "/2)*2'"; H = "'trunc(ih*" + Inv(f) + "/2)*2'";
                    W1 = "'trunc(iw*" + Inv(f * m) + ")'"; H1 = "'trunc(ih*" + Inv(f * m) + ")'";
                }
            }
            else
            {
                W = s.ResW.ToString(); H = s.ResH.ToString();
                W1 = ((int)Math.Round(s.ResW * m)).ToString(); H1 = ((int)Math.Round(s.ResH * m)).ToString();
                f = Math.Max((double)s.ResW / Math.Max(1, sw), (double)s.ResH / Math.Max(1, sh));
            }

            if (s.OptiEnabled && !HasLibplacebo)
            {
                error = "OptiScaler (output scaling + RCAS) runs on the GPU through FFmpeg's libplacebo filter (Vulkan), which your FFmpeg build does not include. " +
                        "Use a \"full\" FFmpeg build (gyan.dev full / BtbN).";
                return null;
            }
            // FSR 3 frame generation: motion-compensated in-between frames (×2), made before upscaling
            string fg = "";
            if (t.Key == "fsr3" && job.FpsNum > 0 && job.FpsDen > 0)
                fg = "minterpolate=fps=" + (job.FpsNum * 2) + "/" + job.FpsDen + ":mi_mode=mci:mc_mode=aobmc:me_mode=bidir:vsbmc=1:scd=fdiff,";
            if (!s.OptiEnabled)
            {
                string c = Chain(s, t, W, H, f, sharp, null, sw, sh, out needsVulkan, out error);
                return c == null ? null : fg + c;
            }

            // OptiScaler: upscaler at (output × multiplier) in 16-bit RGB, then RCAS + downscale to the output size
            string up = Chain(s, t, W1, H1, f * m, sharp, "gbrp16le", sw, sh, out needsVulkan, out error);
            if (up == null) return null;
            up = fg + up;
            needsVulkan = true;
            string shader = WriteOpti(s);
            int hgt; int.TryParse(H, out hgt);
            if (hgt == 0) hgt = sh > 0 ? (int)Math.Round(sh * f) : 1080;
            string matrix = hgt >= 720 ? "bt709" : "smpte170m";
            return up + ",libplacebo=w=" + W + ":h=" + H + ":upscaler=ewa_lanczossharp:downscaler=ewa_lanczos:custom_shader_path=" + FilterCatalog.EscapeValue(shader) +
                   ":dithering=blue" + (forImage ? ":format=gbrp" : ":format=yuv444p10le:colorspace=" + matrix + ":range=tv");
        }

        public static readonly string[] OptiKernels = { "Bicubic", "Catmull-Rom", "Lanczos 2", "Lanczos 3", "Kaiser 2", "Kaiser 3", "Magic Kernel" };

        static string WriteOpti(ConvSettings s)
        {
            int k = Math.Max(1, Math.Min(7, s.OptiKernel));
            double sh = Math.Max(0, Math.Min(100, s.OptiSharpness)) / 100.0;
            string text = Res("OptiScaler.glsl")
                .Replace("{{OPTI_SHARP_WHEN}}", sh > 0 ? "1" : "0")
                .Replace("{{OPTI_SHARPNESS}}", Inv(sh))
                .Replace("{{OPTI_CONTRAST}}", s.OptiContrast ? "1.0" : "0.0")
                .Replace("{{OPTI_KERNEL}}", k.ToString())
                .Replace("{{OPTI_KERNEL_NAME}}", OptiKernels[k - 1]);
            return SaveShader("opti", text);
        }

        static string Chain(ConvSettings s, ScaleType t, string W, string H, double f, double sharp, string rgbFormat, int inW, int inH, out bool needsVulkan, out string error)
        {
            needsVulkan = false; error = null;
            int n = Math.Max(1, (int)Math.Floor(f + 1e-6));     // integer factor
            int nUp = Math.Max(2, Math.Min(4, (int)Math.Ceiling(f - 1e-6)));

            bool gpu = t.GpuOnly || (s.ScaleOptimized && HasLibplacebo && (t.Key == "nearest" || t.Key == "bicubic" || t.Key == "lanczos" || t.Key == "spline" || t.Key == "bcas"));
            if (t.GpuOnly && !HasLibplacebo)
            {
                error = t.Label + " runs on the GPU through FFmpeg's libplacebo filter (Vulkan), which your FFmpeg build does not include. " +
                        "Use a \"full\" FFmpeg build (gyan.dev full / BtbN), or pick a CPU type: xBR, Integer, Sharp bilinear, BCAS, Lanczos…";
                return null;
            }
            string fmt = rgbFormat != null ? ",format=" + rgbFormat : "";

            if (gpu)
            {
                needsVulkan = true;
                string lp = "libplacebo=w=" + W + ":h=" + H;
                string shader;
                if (t.Key == "fsr2" || t.Key == "fsr3")
                {
                    int ow, oh;
                    // the temporal passes need fixed sizes; if the file never reports its size, use FSR 1 spatial
                    if (inW <= 0 || inH <= 0 || !int.TryParse(W, out ow) || !int.TryParse(H, out oh)) shader = WriteShader("fsr", sharp);
                    else shader = WriteTemporal(sharp, inW, inH, ow, oh);
                }
                else shader = WriteShader(t.Key, sharp);
                switch (t.Key)
                {
                    case "nearest": lp += ":upscaler=nearest"; break;
                    case "bicubic": lp += ":upscaler=bicubic"; break;
                    case "lanczos": lp += ":upscaler=lanczos"; break;
                    case "spline": lp += ":upscaler=spline36"; break;
                    case "bcas": lp += ":upscaler=bicubic"; break;
                    case "ewa": lp += ":upscaler=ewa_lanczossharp:antiringing=0.6"; break;
                    default: lp += ":upscaler=ewa_lanczos"; break;   // chroma / leftover scaling around the shader
                }
                if (shader != null) lp += ":custom_shader_path=" + FilterCatalog.EscapeValue(shader);
                if (s.ScaleOptimized && rgbFormat == null) lp += ":dithering=blue";
                if (rgbFormat != null) lp += ":format=" + rgbFormat;
                if (t.Key == "bcas") lp += ",cas=strength=" + Inv(sharp);
                return lp;
            }

            // CPU path
            string flagsTo = ":flags=";
            switch (t.Key)
            {
                case "xbr":
                    return "xbr=n=" + nUp + ",scale=" + W + ":" + H + flagsTo + "lanczos" + fmt;
                case "hqx":
                    return "hqx=n=" + nUp + ",scale=" + W + ":" + H + flagsTo + "lanczos" + fmt;
                case "integer":
                    {
                        string iw = "iw*" + n, ih = "ih*" + n;
                        return "scale=" + iw + ":" + ih + flagsTo + "neighbor" + (n < f - 1e-6 ? ",pad=" + W + ":" + H + ":(ow-iw)/2:(oh-ih)/2:color=black" : "") + ",setsar=1" + fmt;
                    }
                case "nearest":
                    return "scale=" + W + ":" + H + flagsTo + "neighbor" + fmt;
                case "sharpbilinear":
                    return "scale=iw*" + n + ":ih*" + n + flagsTo + "neighbor,scale=" + W + ":" + H + flagsTo + "bilinear" + fmt;
                case "bcas":
                    return "scale=" + W + ":" + H + flagsTo + "bicubic,cas=strength=" + Inv(sharp) + fmt;
                case "bicubic":
                    return "scale=" + W + ":" + H + flagsTo + "bicubic" + fmt;
                case "spline":
                    return "scale=" + W + ":" + H + flagsTo + "spline" + fmt;
                default:
                    return "scale=" + W + ":" + H + flagsTo + "lanczos" + fmt;
            }
        }

        static string Bump(string va, string opt, int delta, int min)
        {
            return System.Text.RegularExpressions.Regex.Replace(va, "(" + opt + @" )(\d+)", m => m.Groups[1].Value + Math.Max(min, int.Parse(m.Groups[2].Value) - delta));
        }

        /// Screen-quality encode for upscaled video: the upscaler's fine edges and RCAS sharpening are what an
        /// 8-bit 4:2:0 encode at a default CRF smooths away first. Lowers CRF/CQ by 4, switches HEVC / AV1 / VP9
        /// to 10-bit, uses edge-preserving encoder tuning and lets libplacebo write the final pixel format itself
        /// (high-precision conversion with blue-noise dithering instead of an 8-bit round trip).
        public static string Fidelity(ConvSettings s, string va, ref string vf, bool gpu)
        {
            if (!s.ScaleFidelity && !s.ScaleFullChroma) return va;
            bool x265 = va.Contains("libx265"), x264 = va.Contains("libx264"), av1 = va.Contains("libsvtav1"), vp9 = va.Contains("libvpx-vp9");
            bool nv = va.Contains("hevc_nvenc") || va.Contains("av1_nvenc") || va.Contains("h264_nvenc");
            if (!(x265 || x264 || av1 || vp9 || nv)) return va;
            bool tenBit = s.ScaleFidelity && (x265 || av1 || vp9 || va.Contains("hevc_nvenc") || va.Contains("av1_nvenc"));
            bool full = s.ScaleFullChroma && (x265 || vp9 || va.Contains("hevc_nvenc"));   // H.264 4:4:4 plays almost nowhere
            string pix = full ? (nv ? (tenBit ? "yuv444p16le" : "yuv444p") : (tenBit ? "yuv444p10le" : "yuv444p"))
                              : (tenBit ? (nv ? "p010le" : "yuv420p10le") : null);
            if (pix != null)
            {
                va = System.Text.RegularExpressions.Regex.Replace(va, @"-pix_fmt \S+", "-pix_fmt " + pix);
                if (!va.Contains("-pix_fmt")) va += " -pix_fmt " + pix;
                if (gpu && vf != null && vf.Contains("libplacebo="))
                {
                    string lpPix = pix == "p010le" ? "yuv420p10le" : (pix == "yuv444p16le" ? "yuv444p10le" : pix);
                    int i = vf.LastIndexOf("libplacebo="), end = vf.IndexOf(',', i);
                    if (end < 0) end = vf.Length;
                    string seg = vf.Substring(i, end - i);
                    seg = System.Text.RegularExpressions.Regex.IsMatch(seg, ":format=[a-z0-9]+")
                        ? System.Text.RegularExpressions.Regex.Replace(seg, ":format=[a-z0-9]+", ":format=" + lpPix)
                        : seg + ":format=" + lpPix;
                    vf = vf.Substring(0, i) + seg + vf.Substring(end);
                }
            }
            if (s.ScaleFidelity && s.VBitrate <= 0)
            {
                va = Bump(va, "-crf", 4, 0);
                if (s.NvCrf <= 0) va = Bump(va, "-cq", 4, 1);      // a CRF typed by the user is kept as is
                if (x265) va += " -x265-params aq-mode=3:psy-rd=2.0:psy-rdoq=1.0:deblock=-1,-1:sao=0";
                else if (x264) va += " -aq-mode 3 -deblock -1:-1 -psy-rd 1.0:0.15";
                else if (av1) va += " -svtav1-params tune=0:enable-overlays=1";
                else if (vp9) va += " -tune-content film -arnr-strength 2";
                else if (nv) va += " -spatial-aq 1 -aq-strength 8";
            }
            return va;
        }

        public const string VulkanArgs = "-init_hw_device vulkan=vk -filter_hw_device vk ";
    }
}
