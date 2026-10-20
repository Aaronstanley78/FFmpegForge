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
            if (!s.ScaleEnabled) return null;
            var t = Get(s.ScaleType);
            double sharp = Math.Max(0, Math.Min(100, s.ScaleSharpness)) / 100.0;

            // output size: factor, or the Resolution setting when chosen
            int sw = job.Width, sh = job.Height;
            if (s.Rotate == 1 || s.Rotate == 3) { int tmp = sw; sw = sh; sh = tmp; }
            string W, H;
            double f = s.ScaleFactor <= 0 ? 2 : s.ScaleFactor;
            if ((s.ResW > 0 || s.ResH > 0) && sw > 0 && sh > 0)
            {
                double fx = s.ResW > 0 ? (double)s.ResW / sw : 0, fy = s.ResH > 0 ? (double)s.ResH / sh : 0;
                if (fx > 0 && fy > 0) f = s.ScaleMode == 2 ? Math.Max(fx, fy) : (s.ScaleMode == 1 ? Math.Min(fx, fy) : 0);
                else f = Math.Max(fx, fy);
            }
            bool even = !forImage;
            if (f > 0)
            {
                if (sw > 0 && sh > 0)
                {
                    int ow = (int)Math.Round(sw * f), oh = (int)Math.Round(sh * f);
                    if (even) { ow += ow % 2; oh += oh % 2; }
                    W = ow.ToString(); H = oh.ToString();
                }
                else { W = "'trunc(iw*" + Inv(f) + "/2)*2'"; H = "'trunc(ih*" + Inv(f) + "/2)*2'"; }
            }
            else { W = s.ResW.ToString(); H = s.ResH.ToString(); f = Math.Max((double)s.ResW / Math.Max(1, sw), (double)s.ResH / Math.Max(1, sh)); }
            int n = Math.Max(1, (int)Math.Floor(f + 1e-6));     // integer factor
            int nUp = Math.Max(2, Math.Min(4, (int)Math.Ceiling(f - 1e-6)));

            bool gpu = t.GpuOnly || (s.ScaleOptimized && HasLibplacebo && (t.Key == "nearest" || t.Key == "bicubic" || t.Key == "lanczos" || t.Key == "spline" || t.Key == "bcas"));
            if (t.GpuOnly && !HasLibplacebo)
            {
                error = t.Label + " runs on the GPU through FFmpeg's libplacebo filter (Vulkan), which your FFmpeg build does not include. " +
                        "Use a \"full\" FFmpeg build (gyan.dev full / BtbN), or pick a CPU type: xBR, Integer, Sharp bilinear, BCAS, Lanczos…";
                return null;
            }

            if (gpu)
            {
                needsVulkan = true;
                string lp = "libplacebo=w=" + W + ":h=" + H;
                string shader = WriteShader(t.Key, sharp);
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
                if (s.ScaleOptimized) lp += ":dithering=blue";
                if (t.Key == "bcas") lp += ",cas=strength=" + Inv(sharp);
                return lp;
            }

            // CPU path
            string flagsTo = ":flags=";
            switch (t.Key)
            {
                case "xbr":
                    return "xbr=n=" + nUp + ",scale=" + W + ":" + H + flagsTo + "lanczos";
                case "hqx":
                    return "hqx=n=" + nUp + ",scale=" + W + ":" + H + flagsTo + "lanczos";
                case "integer":
                    {
                        string iw = "iw*" + n, ih = "ih*" + n;
                        return "scale=" + iw + ":" + ih + flagsTo + "neighbor" + (n < f - 1e-6 ? ",pad=" + W + ":" + H + ":(ow-iw)/2:(oh-ih)/2:color=black" : "") + ",setsar=1";
                    }
                case "nearest":
                    return "scale=" + W + ":" + H + flagsTo + "neighbor";
                case "sharpbilinear":
                    return "scale=iw*" + n + ":ih*" + n + flagsTo + "neighbor,scale=" + W + ":" + H + flagsTo + "bilinear";
                case "bcas":
                    return "scale=" + W + ":" + H + flagsTo + "bicubic,cas=strength=" + Inv(sharp);
                case "bicubic":
                    return "scale=" + W + ":" + H + flagsTo + "bicubic";
                case "spline":
                    return "scale=" + W + ":" + H + flagsTo + "spline";
                default:
                    return "scale=" + W + ":" + H + flagsTo + "lanczos";
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
                if (gpu && vf != null && vf.Contains("libplacebo=") && !vf.Contains(":format="))
                {
                    string lpPix = pix == "p010le" ? "yuv420p10le" : (pix == "yuv444p16le" ? "yuv444p10le" : pix);
                    int i = vf.IndexOf("libplacebo="), end = vf.IndexOf(',', i);
                    if (end < 0) end = vf.Length;
                    vf = vf.Substring(0, end) + ":format=" + lpPix + vf.Substring(end);
                }
            }
            if (s.ScaleFidelity && s.VBitrate <= 0)
            {
                va = Bump(va, "-crf", 4, 0);
                va = Bump(va, "-cq", 4, 1);
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
