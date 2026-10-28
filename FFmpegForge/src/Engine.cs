using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace FFmpegForge
{
    public class Job
    {
        public string Path;
        public MediaKind Kind = MediaKind.Unknown;
        public double Duration;      // seconds, 0 if unknown
        public long Size;
        public string Status = "Queued";
        public double Progress;      // 0..1
        public string Output;
        public string Error;
        public bool HasVideo, HasAudio;
        public string Info = "";
        public int Width, Height;            // first video stream
        public int FpsNum, FpsDen;           // r_frame_rate
        public bool NoiseProfiled; public double NoiseY, NoiseC;   // denoiser auto profile (8-bit σ; -1 = could not measure)
        public int AudioChannels;            // first audio stream
        public bool Probed;
    }

    public class ConvSettings
    {
        public OutFormat Format;
        // video
        public string VCodec = "auto";
        public int QualityIdx = 1;    // 0 = max ... 4 = smallest
        public int VBitrate;          // kbps, 0 = auto
        public int PresetIdx = 2;     // 0 = ultrafast ... 5 = veryslow
        public int ResW, ResH;        // 0 = original / automatic (no upper limit)
        public int ScaleMode;         // when both set: 0 stretch, 1 fit (pad), 2 fill (crop)
        public double Fps;            // 0 = original
        public int Rotate;            // 0 none, 1 90°, 2 180°, 3 270°, 4 h-mirror, 5 v-mirror
        public bool KeepSubs;
        public double StillDuration = 5;
        // audio
        public string ACodec = "auto";
        public int AKbps;
        public int SampleRate;
        public string ChLayout = "";
        public int UpmixStyle;        // 0 auto, 1 music, 2 cinema, 3 live, 4 off
        public int SourceChannels;    // filled per job (for the immersive upmix)
        public double SourceDuration; // filled per job: audio length after trimming (s), 0 = unknown
        public bool SourceIsVideo;  // "" original, "copy" = passthrough (Atmos / DTS:X), or a layout key
        public bool Normalize;
        public int VolumePct = 100;
        // image
        public int ImgQuality = 90;
        public int ExtractMode;       // 0 single, 1 all, 2 every N s
        public double ExtractAt = 1;
        public double ExtractEvery = 1;
        // common
        public double TrimStart = -1, TrimEnd = -1;
        public string CustomArgs = "";
        public string OutDir;
        public bool Overwrite;
        // filters
        public List<FilterEntry> Filters = new List<FilterEntry>();
        public string FilterComplex = "";
        public List<string> ExtraInputs = new List<string>();
        // NVIDIA RTX AI
        public bool RtxVsr, RtxHdr, Dlss;
        public int VsrQuality = 4, VsrScale = 2;
        public int HdrContrast = 125, HdrSaturation = 100, HdrMiddleGray = 25, HdrMaxLum = 1000;
        public int DlssQuality = 4; public double DlssScale = 2;
        public string VsrExpr, HdrExpr, DlssExpr;     // custom expressions from the parameter editor
        public string RtxEnginePref = "auto";         // auto | native | processor
        public SeedSettings Seed = new SeedSettings();
        public int NvCrf;             // H.264 / H.265 NVENC constant quality (-cq) chosen by the user, 0 = from Quality
        public bool RtxActive { get { return RtxVsr || RtxHdr || Dlss; } }
        public bool DlssSkipped;
        // Scale (Échelle)
        public bool ScaleEnabled;
        public string ScaleType = "fsr";
        public double ScaleFactor = 2;    // 0 = use the Resolution setting
        public int ScaleSharpness = 100;  // 0..100 %
        public bool ScaleOptimized = true;
        public bool ScaleFidelity = true;     // screen-quality encode (10-bit, lower CRF, edge-preserving tuning)
        public bool ScaleFullChroma = true;   // 4:4:4 chroma (no colour blur on sharpened edges)
        // OptiScaler output stage (GPL-3.0 port): supersample × OptiMult, RCAS, downscale with OptiKernel
        // Denoiser (Neat Video-style)
        public bool DnEnabled, DnTemporal = true, DnAutoProfile = true;
        public int DnPreset, DnEngine, DnStrength = 100, DnSharpen = 20;
        public bool OptiEnabled;
        public double OptiMult = 1.5;
        public int OptiKernel = 3;          // 1 Bicubic, 2 Catmull-Rom, 3 Lanczos 2, 4 Lanczos 3, 5 Kaiser 2, 6 Kaiser 3, 7 Magic Kernel
        public int OptiSharpness = 40;      // RCAS 0..100 %
        public bool OptiContrast;
        public bool ScaleActive { get { return ScaleEnabled || OptiEnabled; } }

        public string UserVideoChain() { return string.Join(",", Filters.Where(f => f.Enabled && f.Kind == 'V').Select(f => f.Expr.Trim()).Where(e => e.Length > 0).ToArray()); }
        public string UserAudioChain() { return string.Join(",", Filters.Where(f => f.Enabled && f.Kind == 'A').Select(f => f.Expr.Trim()).Where(e => e.Length > 0).ToArray()); }
    }

    public static class CommandBuilder
    {
        static readonly string[] X264Presets = { "ultrafast", "veryfast", "fast", "medium", "slow", "veryslow" };

        static string Q(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }

        public static bool IsAnimatedImageSource(Job job)
        {
            string e = Formats.ExtOf(job.Path);
            return job.Kind == MediaKind.Image && (e == "gif" || e == "apng" || e == "webp") && job.Duration > 0.05;
        }

        static string Join(params string[] parts)
        {
            return string.Join(",", parts.Where(p => !string.IsNullOrEmpty(p)).ToArray());
        }

        /// Builds the FFmpeg argument line. Returns null + error when the conversion is impossible.
        public static string Build(Job job, ConvSettings s, string outPath, out double expected, out string error)
        {
            expected = 0; error = null;
            var pre = new List<string>();
            var post = new List<string>();
            var fmt = s.Format;
            MediaKind src = job.Kind == MediaKind.Unknown ? (job.HasVideo ? MediaKind.Video : MediaKind.Audio) : job.Kind;
            bool animated = IsAnimatedImageSource(job);
            if (animated) src = MediaKind.Video;

            s.SourceChannels = job.AudioChannels;
            s.SourceIsVideo = job.Kind == MediaKind.Video;
            string userV = s.UserVideoChain();
            string userA = s.UserAudioChain();
            bool userGraph = !string.IsNullOrWhiteSpace(s.FilterComplex);

            string vf = null, af = null, graph = null;
            var maps = new List<string>();
            bool needVideoMap = false, needAudioMap = false;

            double ss = s.TrimStart > 0 ? s.TrimStart : 0;
            double to = s.TrimEnd > 0 ? s.TrimEnd : -1;
            double trimmedDur = job.Duration;
            if (to > 0 && (trimmedDur <= 0 || to < trimmedDur)) trimmedDur = to;
            trimmedDur = Math.Max(0, trimmedDur - ss);
            s.SourceDuration = trimmedDur;

            Action addTrim = () =>
            {
                if (ss > 0) { pre.Add("-ss"); pre.Add(Formats.Inv(ss)); }
                if (to > 0 && to > ss) { pre.Add("-to"); pre.Add(Formats.Inv(to)); }
            };

            // ============================ VIDEO OUTPUT ============================
            if (fmt.Kind == MediaKind.Video)
            {
                string vkey = fmt.VCodec;
                bool special = vkey == "gif" || vkey == "apng" || vkey == "webpanim";
                if (!special && s.VCodec != "auto") vkey = s.VCodec;
                bool copyV = vkey == "copy";

                double fps = s.Fps;
                bool fromStill = src == MediaKind.Image;
                bool fromAudio = src == MediaKind.Audio;

                if (fromStill)
                {
                    pre.Add("-loop 1");
                    pre.Add("-framerate " + Formats.Inv(fps > 0 ? fps : 25));
                    pre.Add("-t " + Formats.Inv(s.StillDuration));
                    expected = s.StillDuration;
                    if (copyV) { error = "Stream copy is impossible from a still image."; return null; }
                }
                else
                {
                    addTrim();
                    expected = trimmedDur;
                }
                if (copyV && userV.Length > 0) { error = "Video filters cannot be used with \"Stream copy\". Pick a real video codec."; return null; }
                string rtxChain = null;
                if (s.RtxActive)
                {
                    string why;
                    if (Rtx.Engine(s, out why) != "native") { error = why ?? "RTX processing is handled by RTXVideoProcessor."; return null; }
                    if (copyV || special || fromAudio) { error = "RTX AI processing needs a real video codec (not stream copy / GIF / audio source)."; return null; }
                    if (s.RtxHdr)
                    {
                        string k = MapCodec(vkey, true);
                        if (k == "h264" || k == "nvh264" || k == "qsvh264" || k == "amfh264" || k == "mpeg2" || k == "mpeg4" || k == "mjpeg" || k == "wmv2" || k == "theora" || k == "vp8")
                        { error = "RTX TrueHDR produces 10-bit HDR: choose H.265, AV1, VP9, ProRes or DNxHR HQX (8-bit codecs such as H.264 cannot carry HDR)."; return null; }
                    }
                    rtxChain = Rtx.NativeChain(s);
                }
                bool scaleVk = false;
                string scaleChain = null;
                if (s.ScaleActive && !fromAudio && !copyV)
                {
                    if (s.RtxActive) { error = "\"Scale\" / OptiScaler and NVIDIA RTX AI are different upscalers: tick only one of them."; return null; }
                    string serr;
                    scaleChain = Scaling.Filter(s, job, false, out scaleVk, out serr);
                    if (serr != null) { error = serr; return null; }
                    if (scaleVk) pre.Insert(0, Scaling.VulkanArgs.Trim());
                }

                string dnChain = (!fromAudio && !copyV) ? Denoise.Filter(s, job, false) : null;
                var rot = new List<string>();
                AddRotate(rot, s.Rotate);
                string rotChain = string.Join(",", rot.ToArray());

                if (fromAudio)
                {
                    if (copyV || special) { error = "This animated format cannot be created from an audio file."; return null; }
                    int w, h; WaveSize(s, 1280, 720, out w, out h);
                    w += w % 2; h += h % 2;
                    string rate = Formats.Inv(fps > 0 ? fps : 30);
                    graph = "[0:a]showwaves=s=" + w + "x" + h + ":mode=cline:rate=" + rate + ":colors=0xFF7A18," + Join(rotChain, userV, "format=yuv420p") + "[v]";
                    maps.Add("-map \"[v]\""); maps.Add("-map 0:a");
                    post.Add(VideoArgs(vkey, s, fmt.Ext));
                }
                else if (vkey == "gif")
                {
                    vf = Join(rotChain, userV, dnChain, scaleChain,
                        "fps=" + Formats.Inv(fps > 0 ? fps : 15),
                        ScaleFilter(s, false) ?? (scaleChain != null ? null : "scale='min(640,iw)':-1:flags=lanczos"),
                        "split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4");
                    post.Add("-c:v gif");
                    needVideoMap = true;
                }
                else if (vkey == "apng" || vkey == "webpanim")
                {
                    vf = Join(rotChain, userV, dnChain, scaleChain, fps <= 0 ? "fps=20" : null,
                        ScaleFilter(s, false) ?? (scaleChain != null ? null : "scale='min(800,iw)':-1:flags=lanczos"));
                    if (fps > 0) post.Add("-r " + Formats.Inv(fps));
                    post.Add(vkey == "apng" ? "-c:v apng -f apng" : "-c:v libwebp_anim -lossless 0 -quality " + new[] { 95, 90, 80, 70, 55 }[Clamp(s.QualityIdx, 0, 4)]);
                    needVideoMap = true;
                }
                else
                {
                    if (!copyV)
                    {
                        bool even = NeedsEven(vkey);
                        string[] rule = FormatRules.Video(Tail(MapCodec(vkey, true)), fmt.Muxer);
                        vf = Join(rtxChain, rotChain, userV, dnChain, scaleChain, ScaleFilter(s, even) ?? (even ? "scale=trunc(iw/2)*2:trunc(ih/2)*2" : null), rule != null ? rule[0] : null,
                            vkey.StartsWith("dnxhr") ? "scale='max(256,trunc(iw/2)*2)':'max(120,trunc(ih/2)*2)'" : null);   // DNxHR minimum 256x120
                        if (fps > 0 && !fromStill) post.Add("-r " + Formats.Inv(fps));
                        if (rule != null && rule[2] != null) { post.Add(rule[2]); vkey = "__replaced"; }
                        else if (rule != null) post.Add(rule[1]);
                    }
                    if (vkey != "__replaced")
                    {
                        string va = VideoArgs(vkey, s, fmt.Ext);
                        if (s.RtxHdr)   // keep the 10-bit HDR10 signal produced by TrueHDR
                        {
                            bool nv = va.Contains("_nvenc");
                            va = va.Replace("-pix_fmt yuv420p", "-pix_fmt " + (nv ? "p010le" : "yuv420p10le"));
                            if (!va.Contains("-pix_fmt")) va += " -pix_fmt " + (nv ? "p010le" : "yuv420p10le");
                            va += " -color_primaries bt2020 -color_trc smpte2084 -colorspace bt2020nc";
                            if (va.Contains("libx265")) va += " -x265-params hdr10=1:repeat-headers=1";
                        }
                        if (scaleChain != null && !s.RtxHdr) va = Scaling.Fidelity(s, va, ref vf, scaleVk);
                        post.Add(va);
                    }
                    needVideoMap = true;
                }

                // ---- audio track ----
                string akey = s.ACodec == "auto" ? fmt.ACodec : s.ACodec;
                if (s.ChLayout == "copy" && akey != "none") akey = "copy";   // keep Atmos / DTS:X bitstream untouched
                if (special) akey = "none";
                bool graphAudio = userGraph && s.FilterComplex.Contains("[aout]");
                if (!graphAudio && (fromStill || (!job.HasAudio && job.Probed && !fromAudio))) akey = "none";
                if (fmt.Muxer == "mxf_opatom") akey = "none";   // OP-Atom = exactly one stream
                if (akey == "none") post.Add("-an");
                else
                {
                    if (akey == "copy" && userA.Length > 0) { error = "Audio filters cannot be used with audio \"Stream copy\"."; return null; }
                    if (akey != "copy")
                    {
                        string lerr = ChannelLayouts.Check(s.ChLayout, akey);
                        if (lerr != null && s.ACodec == "auto") { string alt = ChannelLayouts.AutoCodec(fmt.Ext); if (alt != null) { akey = alt; lerr = null; } }
                        if (lerr != null) { error = lerr; return null; }
                        af = AudioChain(s, userA);
                    }
                    post.Add(AudioFormatArgs(s, akey != "copy"));
                    post.Add(AudioArgs(akey, s.AKbps));
                    if (akey != "copy") post.Add(ChannelLayouts.ExtraArgs(s.ChLayout, akey));
                    if (akey != "copy") post.Add(FormatRules.Audio(Tail(MapCodec(akey, false)), fmt.Muxer));
                    needAudioMap = true;
                }

                // ---- subtitles ----
                if (s.KeepSubs && fmt.Ext == "mkv" && !fromAudio && !fromStill && !special && !userGraph)
                {
                    maps.Add("-map 0:v:0? -map 0:a? -map 0:s? -c:s copy");
                    needVideoMap = needAudioMap = false;
                }
                else post.Add("-sn");

                if (!string.IsNullOrEmpty(fmt.Extra)) post.Add(fmt.Extra);
                if (fmt.Dynamic && fmt.Muxer != null) post.Add("-f " + fmt.Muxer);
                if (fmt.Ext == "mxf" && fps <= 0 && !copyV) post.Add("-r 25");
                if (fromAudio) needVideoMap = needAudioMap = false;
            }
            // ============================ AUDIO OUTPUT ============================
            else if (fmt.Kind == MediaKind.Audio)
            {
                if (src == MediaKind.Image && !userGraph) { error = "An image does not contain audio."; return null; }
                if (job.Probed && !job.HasAudio && !userGraph) { error = "This file has no audio track."; return null; }
                addTrim();
                expected = trimmedDur;
                post.Add("-vn -sn");
                string akeyA = s.ACodec == "auto" || s.ACodec == "none" ? fmt.ACodec : s.ACodec;
                if (s.ChLayout == "copy") akeyA = "copy";
                if (akeyA == "copy" && (s.VolumePct != 100 || s.Normalize || userA.Length > 0 || s.SampleRate > 0))
                { error = "Audio filters, volume, normalization or sample-rate changes cannot be used with stream copy / Atmos-DTS:X passthrough."; return null; }
                if (akeyA != "copy")
                {
                    string lerr = ChannelLayouts.Check(s.ChLayout, akeyA);
                    if (lerr != null && (s.ACodec == "auto" || s.ACodec == "none")) { string alt = ChannelLayouts.AutoCodec(fmt.Ext); if (alt != null) { akeyA = alt; lerr = null; } }
                    if (lerr != null) { error = lerr; return null; }
                    af = AudioChain(s, userA);
                }
                post.Add(AudioFormatArgs(s, akeyA != "copy"));
                post.Add(AudioArgs(akeyA, s.AKbps));
                if (akeyA != "copy") post.Add(ChannelLayouts.ExtraArgs(s.ChLayout, akeyA));
                if (akeyA != "copy") post.Add(FormatRules.Audio(Tail(MapCodec(akeyA, false)), fmt.Muxer));
                post.Add("-map_metadata 0");
                if (fmt.Dynamic && fmt.Muxer != null) post.Add("-f " + fmt.Muxer);
                needAudioMap = true;
            }
            // ============================ IMAGE OUTPUT ============================
            else
            {
                var chain = new List<string>();
                bool sequence = false;
                if (src == MediaKind.Audio)
                {
                    addTrim();
                    int w, h; WaveSize(s, 1920, 1080, out w, out h);
                    if (fmt.VCodec == "ico") { w = Math.Min(w, 256); h = Math.Min(h, 256); }
                    graph = "[0:a]showwavespic=s=" + w + "x" + h + ":colors=0xFF7A18" + (userV.Length > 0 ? "," + userV : "") + (fmt.VCodec == "ico" ? ",format=rgba" : "") + "[v]";
                    maps.Add("-map \"[v]\"");
                    post.Add("-frames:v 1");
                }
                else
                {
                    if (src == MediaKind.Video)
                    {
                        if (s.ExtractMode == 0)
                        {
                            double at = Math.Max(0, s.ExtractAt);
                            if (job.Duration > 0 && at >= job.Duration) at = Math.Max(0, job.Duration / 2);
                            pre.Add("-ss " + Formats.Inv(at));
                        }
                        else
                        {
                            addTrim();
                            expected = trimmedDur;
                            sequence = true;
                            if (s.ExtractMode == 2) chain.Add("fps=1/" + Formats.Inv(Math.Max(0.04, s.ExtractEvery)));
                        }
                    }
                    AddRotate(chain, s.Rotate);
                    if (userV.Length > 0) chain.Add(userV);
                    { string dni = Denoise.Filter(s, job, true); if (dni != null) chain.Add(dni); }
                    if (s.ScaleActive)
                    {
                        bool ivk; string serr;
                        string sc = Scaling.Filter(s, job, true, out ivk, out serr);
                        if (serr != null) { error = serr; return null; }
                        if (sc != null) chain.Add(sc);
                        if (ivk) pre.Insert(0, Scaling.VulkanArgs.Trim());
                    }
                    string ik = fmt.VCodec;
                    if (ik == "ico")
                    {
                        string sc = ScaleFilter(s, false);
                        if (sc != null) chain.Add(sc);
                        chain.Add("scale='min(256,iw)':'min(256,ih)':force_original_aspect_ratio=decrease:flags=lanczos,format=rgba");
                    }
                    else { string sc = ScaleFilter(s, ik == "mjpeg" || ik == "avif"); if (sc != null) chain.Add(sc); }
                    string imgRule = FormatRules.ImageExt(fmt.Ext);
                    if (imgRule != null) chain.Add(imgRule);
                    if (ik == "gif") chain.Add("split[a][b];[a]palettegen[p];[b][p]paletteuse");
                    if (chain.Count > 0) vf = string.Join(",", chain.ToArray());
                    if (!sequence) post.Add("-frames:v 1");
                    needVideoMap = true;
                }

                if (sequence)
                {
                    string k = fmt.VCodec;
                    if (k == "avif" || k == "ico" || k == "gif")
                    { error = "This format cannot be used to export an image sequence. Choose PNG, JPG, WEBP…"; return null; }
                }

                post.Add(ImageArgs(fmt.VCodec, s.ImgQuality));
                post.Add("-an -sn");
                if (sequence) post.Add("-f image2");
                else if (fmt.Image2) post.Add("-update 1");
            }

            // ============================ FILTERS & MAPPING ============================
            var filt = new List<string>();
            if (userGraph)
            {
                string g = s.FilterComplex.Replace("\r", "").Replace("\n", "").Trim().TrimEnd(';');
                filt.Add("-filter_complex " + Q(g));
                bool vout = g.Contains("[vout]"), aout = g.Contains("[aout]");
                if (vout) filt.Add("-map \"[vout]\"");
                else if (fmt.Kind != MediaKind.Audio) filt.Add("-map 0:v:0?");
                if (aout) filt.Add("-map \"[aout]\"");
                else if (needAudioMap || fmt.Kind == MediaKind.Audio) filt.Add("-map 0:a:0?");
                // our own -vf/-af are merged after the user graph only when it does not produce the stream itself
                if (!vout && !string.IsNullOrEmpty(vf) && graph == null) filt.Add("-vf " + Q(vf));
                if (!aout && !string.IsNullOrEmpty(af)) filt.Add("-af " + Q(af));
            }
            else
            {
                if (graph != null) filt.Add("-filter_complex " + Q(graph));
                filt.AddRange(maps);
                if (maps.Count == 0 && s.ExtraInputs.Count > 0)
                {
                    // extra inputs must not steal the main streams
                    if (needVideoMap) filt.Add("-map 0:v:0");
                    if (needAudioMap) filt.Add("-map 0:a:0?");
                }
                if (!string.IsNullOrEmpty(vf)) filt.Add("-vf " + Q(vf));
                if (!string.IsNullOrEmpty(af)) filt.Add("-af " + Q(af));
            }

            if (!string.IsNullOrWhiteSpace(s.CustomArgs)) post.Add(s.CustomArgs.Trim());

            var sb = new StringBuilder();
            sb.Append("-hide_banner -nostdin -y -loglevel warning -progress pipe:1 -nostats ");
            foreach (var p in pre) if (p.Length > 0) sb.Append(p).Append(' ');
            sb.Append("-i ").Append(Q(job.Path)).Append(' ');
            foreach (var x in s.ExtraInputs) if (x.Trim().Length > 0) sb.Append("-i ").Append(Q(x.Trim())).Append(' ');
            foreach (var p in filt) if (!string.IsNullOrEmpty(p)) sb.Append(p).Append(' ');
            foreach (var p in post) if (!string.IsNullOrEmpty(p)) sb.Append(p).Append(' ');
            sb.Append("-strict -2 ");   // allow FFmpeg's experimental encoders (TrueHD, MLP, Opus-in-MP4…)
            sb.Append(Q(outPath));
            return sb.ToString();
        }

        static string Tail(string key) { return key != null && key.Contains(":") ? key.Substring(key.IndexOf(':') + 1) : key; }

        /// Codecs whose chroma subsampling needs even dimensions.
        public static bool NeedsEven(string k)
        {
            if (k == null) return false;
            k = MapCodec(k, true);
            if (k.StartsWith("codec:") || k.StartsWith("enc:")) return false;
            return !(k == "ffv1" || k == "copy" || k == "prores_4444" || k == "prores_4444xq" || k == "dnxhr_444" || k == "png");
        }

        /// Scale filter for the requested output size (no upper limit). Null = keep original size.
        public static string ScaleFilter(ConvSettings s, bool even)
        {
            int W = s.ResW, H = s.ResH;
            if (W <= 0 && H <= 0) return null;
            if (even) { if (W > 0) W += W % 2; if (H > 0) H += H % 2; }
            string auto = even ? "-2" : "-1";
            if (W > 0 && H > 0)
            {
                switch (s.ScaleMode)
                {
                    case 1:
                        return "scale=" + W + ":" + H + ":force_original_aspect_ratio=decrease" + (even ? ":force_divisible_by=2" : "") + ":flags=lanczos,pad=" + W + ":" + H + ":(ow-iw)/2:(oh-ih)/2:color=black,setsar=1";
                    case 2:
                        return "scale=" + W + ":" + H + ":force_original_aspect_ratio=increase:flags=lanczos,crop=" + W + ":" + H + ",setsar=1";
                    default:
                        return "scale=" + W + ":" + H + ":flags=lanczos,setsar=1";
                }
            }
            return W > 0 ? "scale=" + W + ":" + auto + ":flags=lanczos" : "scale=" + auto + ":" + H + ":flags=lanczos";
        }

        static void WaveSize(ConvSettings s, int dw, int dh, out int w, out int h)
        {
            w = s.ResW; h = s.ResH;
            if (w <= 0 && h <= 0) { w = dw; h = dh; }
            else if (w <= 0) w = (int)Math.Round(h * 16.0 / 9.0);
            else if (h <= 0) h = (int)Math.Round(w * 9.0 / 16.0);
        }

        /// Maps a FFmpeg codec name (muxer default) to one of our tuned presets, or to a generic encoder.
        /// H.264 / H.265 NVIDIA NVENC (the codecs that take the user's CRF).
        public static bool IsNvencCrf(string key)
        {
            return key == "nvh264" || key == "nvh265" || key == "enc:h264_nvenc" || key == "enc:hevc_nvenc";
        }

        public static string MapCodec(string key, bool video)
        {
            if (key == null || !key.StartsWith("codec:")) return key;
            string c = key.Substring(6);
            var map = video
                ? new Dictionary<string, string> { { "h264", "h264" }, { "hevc", "h265" }, { "vp9", "vp9" }, { "vp8", "vp8" }, { "mpeg4", "mpeg4" }, { "mpeg2video", "mpeg2" }, { "prores", "prores" }, { "ffv1", "ffv1" }, { "theora", "theora" }, { "wmv2", "wmv2" }, { "mjpeg", "mjpeg" } }
                : new Dictionary<string, string> { { "aac", "aac" }, { "mp3", "mp3" }, { "opus", "opus" }, { "vorbis", "vorbis" }, { "ac3", "ac3" }, { "eac3", "eac3" }, { "flac", "flac" }, { "alac", "alac" }, { "pcm_s16le", "pcm16" }, { "pcm_s24le", "pcm24" }, { "pcm_s16be", "pcm16be" }, { "wmav2", "wmav2" }, { "mp2", "mp2" }, { "dts", "dts" } };
            string k;
            if (map.TryGetValue(c, out k)) return k;
            if (video && c == "av1" && FormatCatalog.HasEncoder("libsvtav1")) return "av1";
            return key;
        }

        public static void AddRotate(List<string> vf, int r)
        {
            switch (r)
            {
                case 1: vf.Add("transpose=1"); break;
                case 2: vf.Add("hflip,vflip"); break;
                case 3: vf.Add("transpose=2"); break;
                case 4: vf.Add("hflip"); break;
                case 5: vf.Add("vflip"); break;
            }
        }

        static int Clamp(int v, int a, int b) { return v < a ? a : (v > b ? b : v); }

        public static string AudioChain(ConvSettings s, string userA)
        {
            var af = new List<string>();
            string lay = ChannelLayouts.Upmix(s.ChLayout, s.UpmixStyle, s.SourceChannels, s.SourceIsVideo, s.SourceDuration) ?? ChannelLayouts.Filter(s.ChLayout);
            if (lay != null) af.Add(lay);
            if (s.VolumePct != 100) af.Add("volume=" + Formats.Inv(s.VolumePct / 100.0));
            if (userA.Length > 0) af.Add(userA);
            if (s.Normalize) af.Add("loudnorm=I=-16:TP=-1.5:LRA=11");
            return af.Count > 0 ? string.Join(",", af.ToArray()) : null;
        }

        public static string AudioFormatArgs(ConvSettings s, bool reencode)
        {
            if (!reencode) return "";
            var parts = new List<string>();
            if (s.SampleRate > 0) parts.Add("-ar " + s.SampleRate);
            else if (s.Normalize) parts.Add("-ar 48000"); // loudnorm outputs 192 kHz otherwise
            return string.Join(" ", parts.ToArray());
        }

        public static string VideoArgs(string key, ConvSettings s, string ext)
        {
            int q = Clamp(s.QualityIdx, 0, 4);
            int p = Clamp(s.PresetIdx, 0, 5);
            int br = s.VBitrate;
            string brArg = br > 0 ? "-b:v " + br + "k -maxrate " + (int)(br * 1.5) + "k -bufsize " + br * 2 + "k" : null;
            bool apple = ext == "mp4" || ext == "mov" || ext == "m4v";
            key = MapCodec(key, true);
            if (key == "enc:h264_nvenc") key = "nvh264";       // same tuned NVENC settings (and CRF) as the curated entries
            else if (key == "enc:hevc_nvenc") key = "nvh265";
            if (key != null && (key.StartsWith("codec:") || key.StartsWith("enc:")))
                return "-c:v " + key.Substring(key.IndexOf(':') + 1) + (br > 0 ? " -b:v " + br + "k" : "");
            switch (key)
            {
                case "h264":
                    return "-c:v libx264 -preset " + X264Presets[p] + " " + (brArg ?? "-crf " + new[] { 17, 20, 23, 26, 30 }[q]) + " -pix_fmt yuv420p";
                case "h265":
                    return "-c:v libx265 -preset " + X264Presets[p] + " " + (brArg ?? "-crf " + new[] { 19, 22, 26, 29, 33 }[q]) + " -pix_fmt yuv420p" + (apple ? " -tag:v hvc1" : "");
                case "av1":
                    return "-c:v libsvtav1 -preset " + new[] { 12, 10, 8, 6, 4, 2 }[p] + " " + (br > 0 ? "-b:v " + br + "k" : "-crf " + new[] { 22, 27, 32, 37, 45 }[q]) + " -pix_fmt yuv420p";
                case "vp9":
                    return "-c:v libvpx-vp9 " + (br > 0 ? "-b:v " + br + "k" : "-crf " + new[] { 24, 28, 32, 36, 42 }[q] + " -b:v 0") +
                           " -row-mt 1 -deadline good -cpu-used " + new[] { 5, 5, 4, 3, 2, 1 }[p] + " -pix_fmt yuv420p";
                case "vp8":
                    return "-c:v libvpx -b:v " + (br > 0 ? br : new[] { 12000, 8000, 5000, 3000, 1500 }[q]) + "k -crf " + new[] { 6, 10, 14, 20, 30 }[q] +
                           " -deadline good -cpu-used " + new[] { 5, 5, 4, 3, 2, 1 }[p] + " -pix_fmt yuv420p";
                case "mpeg4":
                    return "-c:v mpeg4 " + (brArg ?? "-q:v " + new[] { 2, 3, 5, 7, 10 }[q]) + (ext == "avi" ? " -tag:v XVID" : "") + " -pix_fmt yuv420p";
                case "mpeg2":
                    return "-c:v mpeg2video " + (br > 0 ? "-b:v " + br + "k -maxrate " + (int)(br * 1.5) + "k -bufsize " + br * 2 + "k" : "-q:v " + new[] { 2, 3, 5, 7, 10 }[q]) + " -pix_fmt yuv420p";
                case "prores_proxy":
                    return "-c:v prores_ks -profile:v 0 -vendor apl0 -pix_fmt yuv422p10le";
                case "prores_lt":
                    return "-c:v prores_ks -profile:v 1 -vendor apl0 -pix_fmt yuv422p10le";
                case "prores_std":
                    return "-c:v prores_ks -profile:v 2 -vendor apl0 -pix_fmt yuv422p10le";
                case "prores":
                    return "-c:v prores_ks -profile:v 3 -vendor apl0 -pix_fmt yuv422p10le";
                case "prores_4444":
                    return "-c:v prores_ks -profile:v 4 -vendor apl0 -pix_fmt yuva444p10le -alpha_bits 16";
                case "prores_4444xq":
                    return "-c:v prores_ks -profile:v 5 -vendor apl0 -pix_fmt yuva444p10le -alpha_bits 16";
                case "dnxhr_lb":
                    return "-c:v dnxhd -profile:v dnxhr_lb -pix_fmt yuv422p";
                case "dnxhr_sq":
                    return "-c:v dnxhd -profile:v dnxhr_sq -pix_fmt yuv422p";
                case "dnxhr":
                    return "-c:v dnxhd -profile:v dnxhr_hq -pix_fmt yuv422p";
                case "dnxhr_hqx":
                    return "-c:v dnxhd -profile:v dnxhr_hqx -pix_fmt yuv422p10le";
                case "dnxhr_444":
                    return "-c:v dnxhd -profile:v dnxhr_444 -pix_fmt yuv444p10le";
                case "ffv1":
                    return "-c:v ffv1 -level 3 -g 1 -slicecrc 1";
                case "theora":
                    return "-c:v libtheora " + (br > 0 ? "-b:v " + br + "k" : "-q:v " + new[] { 10, 9, 7, 5, 3 }[q]) + " -pix_fmt yuv420p";
                case "wmv2":
                    return "-c:v wmv2 -b:v " + (br > 0 ? br : new[] { 12000, 8000, 5000, 3000, 1500 }[q]) + "k";
                case "mjpeg":
                    return "-c:v mjpeg -q:v " + new[] { 2, 3, 5, 8, 12 }[q] + " -pix_fmt yuvj420p";
                case "nvh264":
                    return "-c:v h264_nvenc -preset " + new[] { "p1", "p2", "p4", "p5", "p6", "p7" }[p] + " " + (br > 0 ? "-b:v " + br + "k" : "-rc vbr -cq " + (s.NvCrf > 0 ? Math.Min(51, s.NvCrf) : new[] { 19, 21, 24, 27, 31 }[q]) + " -b:v 0") + " -pix_fmt yuv420p";
                case "nvh265":
                    return "-c:v hevc_nvenc -preset " + new[] { "p1", "p2", "p4", "p5", "p6", "p7" }[p] + " " + (br > 0 ? "-b:v " + br + "k" : "-rc vbr -cq " + (s.NvCrf > 0 ? Math.Min(51, s.NvCrf) : new[] { 21, 23, 26, 29, 33 }[q]) + " -b:v 0") + " -pix_fmt yuv420p" + (apple ? " -tag:v hvc1" : "");
                case "nvav1":
                    return "-c:v av1_nvenc -preset " + new[] { "p1", "p2", "p4", "p5", "p6", "p7" }[p] + " " + (br > 0 ? "-b:v " + br + "k" : "-rc vbr -cq " + new[] { 24, 28, 32, 36, 42 }[q] + " -b:v 0") + " -pix_fmt yuv420p";
                case "qsvh264":
                    return "-c:v h264_qsv -preset " + new[] { "veryfast", "veryfast", "fast", "medium", "slow", "veryslow" }[p] + " " + (br > 0 ? "-b:v " + br + "k" : "-global_quality " + new[] { 18, 21, 24, 27, 31 }[q]) + " -pix_fmt nv12";
                case "amfh264":
                    {
                        int c = new[] { 18, 21, 24, 27, 31 }[q];
                        return "-c:v h264_amf -quality " + new[] { "speed", "speed", "balanced", "balanced", "quality", "quality" }[p] + " " +
                               (br > 0 ? "-b:v " + br + "k" : "-rc cqp -qp_i " + c + " -qp_p " + c + " -qp_b " + c) + " -pix_fmt yuv420p";
                    }
                case "copy":
                    return "-c:v copy";
                default:
                    return "-c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p";
            }
        }

        public static string AudioArgs(string key, int k)
        {
            key = MapCodec(key, false);
            if (key != null && (key.StartsWith("codec:") || key.StartsWith("enc:")))
                return "-c:a " + key.Substring(key.IndexOf(':') + 1) + (k > 0 ? " -b:a " + k + "k" : "");
            switch (key)
            {
                case "aac": return "-c:a aac -b:a " + (k > 0 ? Math.Min(k, 512) : 192) + "k";
                case "mp3": return "-c:a libmp3lame " + (k > 0 ? "-b:a " + Math.Min(k, 320) + "k" : "-q:a 2");
                case "opus": return "-c:a libopus -b:a " + (k > 0 ? Math.Min(k, 510) : 160) + "k";
                case "vorbis": return "-c:a libvorbis " + (k > 0 ? "-b:a " + Math.Min(k, 500) + "k" : "-q:a 6");
                case "ac3": return "-c:a ac3 -b:a " + (k > 0 ? Math.Min(Math.Max(k, 96), 640) : 448) + "k";
                case "eac3": return "-c:a eac3 -b:a " + (k > 0 ? Math.Min(Math.Max(k, 96), 640) : 448) + "k";
                case "flac": return "-c:a flac -compression_level 8";
                case "alac": return "-c:a alac";
                case "pcm16": return "-c:a pcm_s16le";
                case "pcm24": return "-c:a pcm_s24le";
                case "pcm32f": return "-c:a pcm_f32le";
                case "pcm16be": return "-c:a pcm_s16be";
                case "wmav2": return "-c:a wmav2 -b:a " + (k > 0 ? Math.Min(k, 320) : 192) + "k";
                case "mp2": return "-c:a mp2 -b:a " + (k > 0 ? Math.Min(k, 384) : 256) + "k";
                case "dts": return "-c:a dca -strict -2 -ar 48000";
                case "amr": return "-c:a libopencore_amrnb -ar 8000 -ac 1 -b:a 12.2k";
                case "wavpack": return "-c:a wavpack";
                case "tta": return "-c:a tta";
                case "copy": return "-c:a copy";
                case "none": return "-an";
                default: return "-c:a aac -b:a 192k";
            }
        }

        public static string ImageArgs(string key, int quality)
        {
            int q = Clamp(quality, 1, 100);
            switch (key)
            {
                case "png": return "-c:v png -compression_level 9";
                case "mjpeg": return "-c:v mjpeg -q:v " + (2 + (int)Math.Round((100 - q) * 29.0 / 99.0)) + " -pix_fmt yuvj444p";
                case "webp": return "-c:v libwebp -quality " + q + " -compression_level 6";
                case "webpll": return "-c:v libwebp -lossless 1 -quality 100 -compression_level 6";
                case "avif": return "-c:v libaom-av1 -still-picture 1 -cpu-used 6 -crf " + Clamp((int)Math.Round(52 - q * 0.45), 0, 63) + " -pix_fmt yuv444p";
                case "jxl": return "-c:v libjxl -distance " + (q >= 100 ? "0" : Formats.Inv(Math.Min(15, 0.1 + (100 - q) * 0.09)));
                case "tiff": return "-c:v tiff -compression_algo lzw";
                case "gif": return "-c:v gif";
                case "ico": return "-c:v png";
                case "auto": return "";
                default: return "-c:v " + key;
            }
        }
    }

    /// Portable mode: a file "portable.txt" next to FFmpegForge.exe keeps the settings and caches in
    /// the program folder (Settings\) and uses the FFmpeg shipped in bin\ first.
    public static class AppPaths
    {
        public static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
        public static bool Portable { get { return File.Exists(System.IO.Path.Combine(AppDir, "portable.txt")); } }
        public static string DataDir
        {
            get
            {
                if (Portable)
                {
                    string d = System.IO.Path.Combine(AppDir, "Settings");
                    try { Directory.CreateDirectory(d); return d; } catch { }
                }
                return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FFmpegForge");
            }
        }
    }

    public static class Runner
    {
        public static string FFmpeg = "";
        public static string FFprobe = "";

        public static string[] Candidates()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            return new[] {
                System.IO.Path.Combine(appDir, "ffmpeg.exe"),
                System.IO.Path.Combine(appDir, "bin", "ffmpeg.exe"),
                @"C:\FFmpeg\bin\ffmpeg.exe",
                @"C:\FFmpeg\ffmpeg.exe",
                @"C:\ffmpeg\bin\ffmpeg.exe",
            };
        }

        public static bool SetFFmpeg(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            FFmpeg = path;
            string dir = System.IO.Path.GetDirectoryName(path);
            string probe = System.IO.Path.Combine(dir, "ffprobe.exe");
            if (!File.Exists(probe)) probe = System.IO.Path.Combine(dir, "ffprobe");
            FFprobe = File.Exists(probe) ? probe : "";
            return true;
        }

        public static bool AutoDetect(string saved)
        {
            if (AppPaths.Portable)      // the bundled FFmpeg wins, wherever the folder was moved
            {
                if (SetFFmpeg(System.IO.Path.Combine(AppPaths.AppDir, "bin", "ffmpeg.exe"))) return true;
                if (SetFFmpeg(System.IO.Path.Combine(AppPaths.AppDir, "ffmpeg.exe"))) return true;
            }
            if (SetFFmpeg(saved)) return true;
            foreach (var c in Candidates()) if (SetFFmpeg(c)) return true;
            // PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var d in pathEnv.Split(System.IO.Path.PathSeparator))
            {
                try
                {
                    if (SetFFmpeg(System.IO.Path.Combine(d.Trim(), "ffmpeg.exe"))) return true;
                    if (SetFFmpeg(System.IO.Path.Combine(d.Trim(), "ffmpeg"))) return true;
                }
                catch { }
            }
            return false;
        }

        static string RunCapture(string exe, string args, int timeoutMs)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                var errTask = p.StandardError.ReadToEndAsync();
                string o = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } }
                return o + "\n" + errTask.Result;
            }
        }

        public static string Version()
        {
            try
            {
                string o = RunCapture(FFmpeg, "-hide_banner -version", 8000);
                string first = o.Split('\n')[0].Trim();
                // "ffmpeg version 7.1-full_build-www.gyan.dev Copyright..."
                int i = first.IndexOf("Copyright", StringComparison.OrdinalIgnoreCase);
                if (i > 0) first = first.Substring(0, i).Trim();
                string ver = first.Replace("ffmpeg version", "").Trim();
                int cut = ver.IndexOf("-full"); if (cut < 0) cut = ver.IndexOf("-essentials");
                if (cut > 0) ver = ver.Substring(0, cut);
                if (ver.Length > 22) ver = ver.Substring(0, 22) + "…";
                return "FFmpeg " + ver + " detected";
            }
            catch (Exception ex) { return "Error: " + ex.Message; }
        }

        /// Frame rate: r_frame_rate, or avg_frame_rate when r_frame_rate is a container time base (e.g. 1000/1).
        static void SetFps(Job job, string r, string avg)
        {
            foreach (var cand in new[] { r, avg })
            {
                if (cand == null) continue;
                var fr = cand.Split('/'); int fn, fd;
                if (fr.Length == 2 && int.TryParse(fr[0], out fn) && int.TryParse(fr[1], out fd) && fn > 0 && fd > 0 && (double)fn / fd <= 240)
                { job.FpsNum = fn; job.FpsDen = fd; return; }
            }
        }

        /// Some files (recordings started mid-stream, e.g. VLC records) give no video size in the quick probe:
        /// analyse more of the file, then decode the first frame if needed.
        static void DeepProbe(Job job)
        {
            try
            {
                string o = RunCapture(FFprobe, "-v error -analyzeduration 100M -probesize 100M -select_streams v:0 -show_entries stream=width,height,r_frame_rate,avg_frame_rate -of default=nw=1 \"" + job.Path + "\"", 30000);
                int w = 0, h = 0; string r = null, a = null;
                foreach (var raw in o.Split('\n'))
                {
                    string line = raw.Trim(); int eq = line.IndexOf('='); if (eq < 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    if (k == "width") int.TryParse(v, out w); else if (k == "height") int.TryParse(v, out h);
                    else if (k == "r_frame_rate") r = v; else if (k == "avg_frame_rate") a = v;
                }
                if (w <= 0 || h <= 0)
                {
                    o = RunCapture(FFprobe, "-v error -select_streams v:0 -read_intervals %+#1 -show_entries frame=width,height -of default=nw=1 \"" + job.Path + "\"", 30000);
                    foreach (var raw in o.Split('\n'))
                    {
                        string line = raw.Trim(); int eq = line.IndexOf('='); if (eq < 0) continue;
                        string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                        if (k == "width" && w <= 0) int.TryParse(v, out w); else if (k == "height" && h <= 0) int.TryParse(v, out h);
                    }
                }
                if (w > 0 && h > 0) { job.Width = w; job.Height = h; }
                if (job.FpsNum <= 0) SetFps(job, r, a);
            }
            catch { }
        }

        public static void Probe(Job job)
        {
            try { job.Size = new FileInfo(job.Path).Length; } catch { }
            job.Kind = Formats.KindFromExt(job.Path);
            if (string.IsNullOrEmpty(FFprobe)) { job.Probed = false; job.HasAudio = job.Kind != MediaKind.Image; job.HasVideo = job.Kind != MediaKind.Audio; return; }
            try
            {
                string o = RunCapture(FFprobe, "-v error -show_entries format=duration:stream=codec_type,codec_name,width,height,r_frame_rate,avg_frame_rate,channels:stream_disposition=attached_pic -of default=nw=1 \"" + job.Path + "\"", 20000);
                string codec = null, curCodec = null, curType = null, cfr = null, cafr = null; int cchan = 0; int w = 0, h = 0, cw = 0, ch = 0; bool attached = false;
                Action flush = () =>
                {
                    if (curType == "video" && !attached) { job.HasVideo = true; if (codec == null) { codec = curCodec; w = cw; h = ch; job.Width = cw; job.Height = ch; SetFps(job, cfr, cafr); } }
                    if (curType == "audio") { if (!job.HasAudio) job.AudioChannels = cchan; job.HasAudio = true; if (codec == null && !job.HasVideo) codec = curCodec; }
                    curType = null; curCodec = null; cw = ch = 0; attached = false; cfr = null; cafr = null; cchan = 0;
                };
                foreach (var raw in o.Split('\n'))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    switch (k)
                    {
                        case "codec_name": if (curType != null || curCodec != null) flush(); curCodec = v; break;
                        case "codec_type": curType = v; break;
                        case "width": int.TryParse(v, out cw); break;
                        case "height": int.TryParse(v, out ch); break;
                        case "r_frame_rate": cfr = v; break;
                        case "avg_frame_rate": cafr = v; break;
                        case "channels": int.TryParse(v, out cchan); break;
                        case "DISPOSITION:attached_pic": attached = v == "1"; break;
                        case "duration":
                            double d;
                            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) job.Duration = d;
                            break;
                    }
                }
                flush();
                job.Probed = job.HasAudio || job.HasVideo;
                if (job.HasVideo && (job.Width <= 0 || job.Height <= 0)) DeepProbe(job);
                if (job.HasVideo && w <= 0 && job.Width > 0) { w = job.Width; h = job.Height; }
                if (job.Kind == MediaKind.Unknown)
                {
                    if (job.HasVideo && job.Duration > 0.2) job.Kind = MediaKind.Video;
                    else if (job.HasVideo) job.Kind = MediaKind.Image;
                    else if (job.HasAudio) job.Kind = MediaKind.Audio;
                }
                if (job.Kind == MediaKind.Video && !job.HasVideo && job.HasAudio) job.Kind = MediaKind.Audio;
                if (codec != null) job.Info = (w > 0 ? w + "×" + h + "  " : "") + codec;
                if (!job.Probed) job.Info = "unreadable?";
            }
            catch (Exception ex) { job.Info = "probe: " + ex.Message; }
        }

        /// Runs FFmpeg; returns the exit code.
        public static int Run(string args, double expected, Action<double> onProgress, Action<string> onLog, Func<bool> cancelled, out string lastErrors)
        {
            return Run(FFmpeg, args, expected, onProgress, onLog, cancelled, out lastErrors);
        }

        static readonly System.Text.RegularExpressions.Regex TimeRx = new System.Text.RegularExpressions.Regex(@"time=\s*(\d+):(\d+):(\d+(?:\.\d+)?)");

        public static int Run(string exe, string args, double expected, Action<double> onProgress, Action<string> onLog, Func<bool> cancelled, out string lastErrors)
        {
            var errs = new List<string>();
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) return;
                    string l = e.Data;
                    if (expected > 0 && (l.StartsWith("out_time_us=") || l.StartsWith("out_time_ms=")))
                    {
                        long us;
                        if (long.TryParse(l.Substring(12), out us) && us > 0) onProgress(Math.Min(1.0, us / 1e6 / expected));
                    }
                    else if (l == "progress=end") onProgress(1.0);
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    if (expected > 0)
                    {
                        // fallback progress for tools printing FFmpeg-style "time=hh:mm:ss.xx"
                        var tm = TimeRx.Match(e.Data);
                        if (tm.Success)
                        {
                            double t = int.Parse(tm.Groups[1].Value) * 3600 + int.Parse(tm.Groups[2].Value) * 60 + double.Parse(tm.Groups[3].Value, CultureInfo.InvariantCulture);
                            onProgress(Math.Min(1.0, t / expected));
                            return;
                        }
                    }
                    lock (errs) { errs.Add(e.Data); if (errs.Count > 30) errs.RemoveAt(0); }
                    onLog(e.Data);
                };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                while (!p.WaitForExit(200))
                {
                    if (cancelled()) { try { p.Kill(); } catch { } p.WaitForExit(5000); lastErrors = "Cancelled"; return -1; }
                }
                p.WaitForExit();
                lock (errs) lastErrors = string.Join(Environment.NewLine, errs.ToArray());
                return p.ExitCode;
            }
        }
    }
}

namespace FFmpegForge
{
    /// Automatic fixes for formats / encoders that only accept specific sizes, rates or layouts.
    public static class FormatRules
    {
        static string Pad(int w, int h)
        {
            return "scale=" + w + ":" + h + ":force_original_aspect_ratio=decrease:flags=lanczos,pad=" + w + ":" + h + ":(ow-iw)/2:(oh-ih)/2,setsar=1";
        }

        /// Returns { filter, extraArgs, replacementVideoArgs } or null.
        public static string[] Video(string enc, string muxer)
        {
            switch (muxer)
            {
                case "gxf": return new[] { Pad(720, 576), "-r 25 -pix_fmt yuv420p", null };
                case "filmstrip": return new[] { "", "-pix_fmt rgba", null };
                case "dv": return new[] { Pad(720, 576), "-r 25", "-c:v dvvideo -pix_fmt yuv420p -r 25" };
                case "mxf_d10":
                    return new[] { Pad(720, 608), null, "-c:v mpeg2video -r 25 -pix_fmt yuv422p -g 0 -flags +ildct+ilme -top 1 -intra_vlc 1 -non_linear_quant 1 -qmin 1 -qmax 12 -dc 10 -b:v 50M -minrate 50M -maxrate 50M -bufsize 2000000 -rc_init_occupancy 2000000 -rc_max_vbv_use 1 -rc_min_vbv_use 1" };
            }
            switch (enc)
            {
                case "h261": return new[] { Pad(352, 288), "", null };
                case "h263": return new[] { Pad(352, 288), "", null };
                case "dvvideo": return new[] { Pad(720, 576), "-r 25 -pix_fmt yuv420p", null };
                case "rv10": case "rv20": return new[] { "scale=trunc(iw/16)*16:trunc(ih/16)*16", "", null };
                case "roqvideo": case "roq": return new[] { "scale=trunc(iw/16)*16:trunc(ih/16)*16", "-r 30", null };
                case "a64multi": case "a64multi5": case "a64_multi": case "a64_multi5": return new[] { "scale=320:200", "", null };
                case "amv": return new[] { "scale=trunc(iw/16)*16:trunc(ih/16)*16", "-r 25", null };
                case "xface": return new[] { "scale=48:48", "", null };
                case "vbn": return new[] { "scale=trunc(iw/4)*4:trunc(ih/4)*4", "", null };
                case "pgmyuv": return new[] { "scale=trunc(iw/2)*2:trunc(ih/2)*2", "", null };
                case "dnxhd": return new[] { "scale=trunc(iw/2)*2:trunc(ih/2)*2", "-profile:v dnxhr_hq -pix_fmt yuv422p", null };
                case "cinepak": return new[] { "scale=trunc(iw/4)*4:trunc(ih/4)*4", "", null };
                case "msvideo1": return new[] { "scale=trunc(iw/4)*4:trunc(ih/4)*4", "", null };
                case "smc": case "rpza": return new[] { "scale=trunc(iw/4)*4:trunc(ih/4)*4", "", null };
            }
            return null;
        }

        public static string Audio(string enc, string muxer)
        {
            switch (muxer)
            {
                case "mxf": case "mxf_opatom": case "gxf": return "-ar 48000";
                case "mxf_d10": case "dv": return "-ar 48000 -ac 2";
                case "rso": return "-ac 1";
                case "amv": return "-ar 22050 -ac 1 -block_size 882";
                case "daud": return "-ar 96000 -ac 6";
                case "mmf": return "-ac 1";
            }
            switch (enc)
            {
                case "gsm": case "libgsm": case "gsm_ms": case "libgsm_ms":
                case "g723_1": case "adpcm_g726": case "adpcm_g726le": case "g726": case "g726le":
                case "amr_nb": case "libopencore_amrnb": case "ilbc": case "libilbc": case "codec2": case "libcodec2":
                    return "-ar 8000 -ac 1";
                case "adpcm_g722": case "g722": case "amr_wb": case "libvo_amrwbenc": return "-ar 16000 -ac 1";
                case "adpcm_ima_amv": return "-ar 22050 -ac 1 -block_size 882";
                case "roq_dpcm": case "adpcm_ima_alp": return "-ar 22050";
                case "pcm_s24daud": return "-ar 96000 -ac 6";
                case "nellymoser": return "-ac 1";
                case "dca": case "dts": return "-ar 48000";
                case "adpcm_ima_ws": case "adpcm_swf": return "-ar 22050";
            }
            return "";
        }

        public static string ImageExt(string ext)
        {
            switch (ext)
            {
                case "xface": return "scale=48:48";
                case "vbn": return "scale=trunc(iw/4)*4:trunc(ih/4)*4";
                case "pgmyuv": case "y": return "scale=trunc(iw/2)*2:trunc(ih/2)*2";
                case "xbm": case "pbm": case "wbmp": return null;
            }
            return null;
        }
    }
}

namespace FFmpegForge
{
    /// Channel layouts, including immersive Dolby Atmos / DTS:X bed layouts (5.1.2 … 9.1.6, 22.2).
    /// FFmpeg cannot encode Atmos objects or DTS:X metadata: immersive layouts are written as
    /// discrete channel beds (PCM, Opus, FLAC ≤ 8 ch…); "copy" passes an existing Atmos / DTS:X
    /// track through untouched.
    public static class ChannelLayouts
    {
        public class Layout
        {
            public string Key, Label, Std, Custom; public int Count;
            public Layout(string key, string label, string std, string custom, int count) { Key = key; Label = label; Std = std; Custom = custom; Count = count; }
        }

        const string Base714 = "FL=FL|FR=FR|FC=FC|LFE=LFE|BL=BL|BR=BR|SL=SL|SR=SR|TFL=TFL|TFR=TFR|TBL=TBL|TBR=TBR";

        public static readonly List<Layout> All = new List<Layout> {
            new Layout("",       "Original", null, null, 0),
            new Layout("copy",   "Passthrough: keep Dolby Atmos / DTS:X track untouched (copy)", null, null, 0),
            new Layout("mono",   "Mono (1.0)", "mono", null, 1),
            new Layout("stereo", "Stereo (2.0)", "stereo", null, 2),
            new Layout("2.1",    "2.1", "2.1", null, 3),
            new Layout("quad",   "Quad (4.0)", "quad", null, 4),
            new Layout("5.0",    "5.0", "5.0", null, 5),
            new Layout("5.1",    "5.1 Surround", "5.1", null, 6),
            new Layout("6.1",    "6.1 Surround", "6.1", null, 7),
            new Layout("7.1",    "7.1 Surround", "7.1", null, 8),
            new Layout("5.1.2",  "5.1.2 (Dolby Atmos / DTS:X bed)", "5.1.2", null, 8),
            new Layout("5.1.4",  "5.1.4 (Dolby Atmos / DTS:X bed)", "5.1.4", null, 10),
            new Layout("7.1.2",  "7.1.2 (Dolby Atmos / DTS:X bed)", "7.1.2", null, 10),
            new Layout("7.1.4",  "7.1.4 (Dolby Atmos / DTS:X bed)", "7.1.4", null, 12),
            new Layout("7.1.6",  "7.1.6 (Dolby Atmos bed)", null,
                "pan=FL+FR+FC+LFE+BL+BR+SL+SR+TFL+TFR+TSL+TSR+TBL+TBR|" + Base714, 14),
            new Layout("9.1.4",  "9.1.4 (Dolby Atmos / DTS:X Pro bed)", null,
                "pan=FL+FR+FC+LFE+BL+BR+SL+SR+WL+WR+TFL+TFR+TBL+TBR|" + Base714, 14),
            new Layout("9.1.6",  "9.1.6 (Dolby Atmos bed)", null,
                "pan=FL+FR+FC+LFE+BL+BR+SL+SR+WL+WR+TFL+TFR+TSL+TSR+TBL+TBR|" + Base714, 16),
            new Layout("22.2",   "22.2 (NHK Super Hi-Vision)", "22.2", null, 24),
            new Layout("binaural", "Spatial Audio — headphones (3D binaural, like Apple Music)", "stereo", null, 2),
        };

        public static Layout Get(string key) { return All.FirstOrDefault(l => l.Key == (key ?? "")); }

        public static string Filter(string key)
        {
            var l = Get(key);
            if (l == null || l.Count == 0) return null;
            if (l.Std != null) return "aformat=channel_layouts=" + l.Std;
            return "aformat=channel_layouts=7.1.4," + l.Custom;     // build the 7.1.4 bed, then add wides / top-sides
        }


        // ---------------------------------------------------------------------
        //  Immersive upmix: builds a real multichannel mix instead of silent channels.
        //  Stereo sources: spectral surround upmix (FFmpeg "surround"): centered
        //  content (lead vocal / dialogue) is steered to the center speaker,
        //  panned instruments keep their position across FL/FR/sides, bass goes to
        //  the LFE. The mid/side "side" signal (stereo width, reverb, room,
        //  cymbals) feeds the wides and the height layer, so the voice never
        //  goes up. Multichannel sources keep their bed; heights come from the
        //  surround ambience.
        // ---------------------------------------------------------------------
        class Style
        {
            public double Fc, Focus, Side, Back, Wide, HFront, HSide, HBack, HPass, Smooth, Cancel = 0.95, Spread, HInstr, Reverb, RevLevel;
            public int Delay; public bool Room, Lfe = true;
        }

        public const int StyleSpatial = 5;

        static Style GetStyle(int style, bool video)
        {
            if (style == 0) style = video ? 2 : StyleSpatial;
            switch (style)
            {
                case 2: // Cinema: dialogue locked to the center, ambience overhead
                    return new Style { Fc = 1.35, Focus = 0.45, Side = 0.85, Back = 0.8, Wide = 0.5, HFront = 0.8, HSide = 0.7, HBack = 0.7, HPass = 1500, Smooth = 0.4, Delay = 12, Room = false };
                case 3: // Live concert: big room, audience all around
                    return new Style { Fc = 1.0, Focus = 0.0, Side = 1.0, Back = 1.0, Wide = 0.7, HFront = 1.0, HSide = 0.9, HBack = 0.9, HPass = 900, Smooth = 0.2, Delay = 18, Room = true, Cancel = 0.6 };
                case StyleSpatial:
                    // Spatial Music (Dolby Atmos music mixes as heard on Apple Music): dry lead vocal in the
                    // center, instruments opened up to the sides and wides, cymbals / air and a decorrelated
                    // reverb field in the height and rear speakers, every speaker playing its own reverb.
                    return new Style { Fc = 1.1, Focus = 0.3, Side = 1.0, Back = 0.75, Wide = 0.8, HFront = 0.85, HSide = 0.8, HBack = 0.8, HPass = 1100, Smooth = 0.25,
                                       Delay = 8, Room = false, Spread = 0.45, HInstr = 0.3, Reverb = 1.7, RevLevel = 0.32 };
                default: // Music: vocals forward, instruments spread, air & cymbals above
                    return new Style { Fc = 1.2, Focus = 0.25, Side = 0.8, Back = 0.7, Wide = 0.6, HFront = 0.9, HSide = 0.75, HBack = 0.75, HPass = 1800, Smooth = 0.3, Delay = 10, Room = false };
            }
        }

        static string F(double d) { return Formats.Inv(d); }

        static readonly string[] UpmixTargets = { "5.1", "7.1", "5.1.2", "5.1.4", "7.1.2", "7.1.4", "7.1.6", "9.1.4", "9.1.6" };

        /// Returns an -af graph that upmixes to the target layout, or null (no upmix needed / Off).
        public static string Upmix(string key, int style, int srcCh, bool video, double duration)
        {
            string g = Upmix(key, style, srcCh, video);
            // convolution passes deliver their last samples late: pad the input, then cut back to the exact length
            if (g != null && duration > 0 && (g.Contains("afir") || g.Contains("headphone")))
                g = "apad=pad_dur=1," + g + ",atrim=end=" + F(Math.Round(duration, 3));
            return g;
        }

        public static string Upmix(string key, int style, int srcCh, bool video)
        {
            if (key == "binaural") return Binaural(style, srcCh, video);
            if (style == 4) return null;
            var l = Get(key);
            if (l == null || !UpmixTargets.Contains(l.Key)) return null;
            if (srcCh > 0 && srcCh >= l.Count) return null;          // already enough channels: plain conversion
            var st = GetStyle(style, video);
            string core = Core(l, st, srcCh);
            if (core == null) return null;
            return (st.Reverb > 0 ? "aresample=48000," : "") + core + ",alimiter=limit=0.95:level=disabled";
        }

        static string TargetOrder(Layout l) { return l.Std != null ? StdOrder(l.Std) : l.Custom.Substring(4, l.Custom.IndexOf('|') - 4); }

        /// Headphones: the immersive mix rendered binaurally (7.1.4 room, measured HRIRs + room) to stereo.
        static string Binaural(int style, int srcCh, bool video)
        {
            string bed;
            if (srcCh >= 12 || style == 4) bed = "aformat=channel_layouts=7.1.4";
            else bed = Upmix("7.1.4", style, srcCh, video) ?? "aformat=channel_layouts=7.1.4";
            string brir = Spatial.BinauralFile(Spatial.Room714);
            return bed + ",aresample=48000[sp];amovie=" + FilterCatalog.EscapeValue(brir) + "[hr];" +
                   "[sp][hr]headphone=map=" + string.Join("|", Spatial.Room714.Select(x => x.Name).ToArray()) + ":hrir=multich:type=freq:lfe=-4:gain=" + F(BinauralGainDb) +
                   ",alimiter=limit=0.95:level=disabled";
        }

        public static double BinauralGainDb = 3 * 12 - 5;   // headphone filter attenuates 3 dB per input channel

        static int revSeed;

        /// afir option that turns its IR normalization off (FFmpeg 7+: irnorm, older: gtype).
        static string AfirNoNorm
        {
            get
            {
                try { if (FilterCatalog.GetHelp("afir").Options.Any(o => o.Name == "irnorm")) return "irnorm=-1"; } catch { }
                return "gtype=none";
            }
        }

        /// afir pass: dry channels go through untouched, send channels become decorrelated reverb tails.
        static string ConvPass(int dryCh, double[] sends, Style st, int mask, double dryHighPass = 0)
        {
            string ir = Spatial.SendIr(dryCh, sends.Select(x => x * st.RevLevel).ToArray(), st.Reverb, 4242 + 97 * (revSeed++), mask, dryHighPass);
            string lbl = "rv" + revSeed;
            return "[" + lbl + "i];amovie=" + FilterCatalog.EscapeValue(ir) + "[" + lbl + "r];[" + lbl + "i][" + lbl + "r]afir=" + AfirNoNorm;
        }

        // WAV channel masks of the layouts the reverb IRs are convolved with (7.1.4, 7.1, quad)
        const int Mask71 = 0x63F, Mask714 = 0x63F | 0x1000 | 0x4000 | 0x8000 | 0x20000, MaskQuad = 0x33;

        // reverb sends, built from the 7.1 bed (c0..c7): instruments, ambience, a little of the lead vocal
        const string SendL = "0.5*c0+0.6*c6+0.6*c4+0.25*c2+0.15*c1", SendR = "0.5*c1+0.6*c7+0.6*c5+0.25*c2+0.15*c0";
        // height reverb: more of the lead vocal (the "plate" above the singer heard in Atmos music mixes)
        const string HSendL = "0.45*c0+0.5*c6+0.5*c4+0.45*c2+0.15*c1", HSendR = "0.45*c1+0.5*c7+0.5*c5+0.45*c2+0.15*c0";

        static string Core(Layout l, Style st, int srcCh)
        {
            revSeed = 0;
            bool bed71 = l.Key.StartsWith("7.1") || l.Key.StartsWith("9.1");
            bool wides = l.Key.StartsWith("9.1");
            int heights = l.Key.EndsWith(".2") ? 2 : l.Key.EndsWith(".4") ? 4 : l.Key.EndsWith(".6") ? 6 : 0;
            bool multi = srcCh >= 6, rev = st.Reverb > 0;
            var g = new StringBuilder();

            // 1) 7.1 steered bed. Stereo: spectral upmix, then remove the centered sound (voice) that
            //    leaks into FL/FR so it really sits in the center speaker, closer to the listener.
            if (!multi)
            {
                double cancel = st.Cancel;
                string inst0 = "c0-" + F(cancel) + "*c2", inst1 = "c1-" + F(cancel) + "*c2";
                g.Append("aformat=channel_layouts=stereo,surround=chl_out=7.1:smooth=" + F(st.Smooth) + ":lfe_low=80:lfe_high=160,");
                g.Append("pan=7.1|c0=" + inst0 + "|c1=" + inst1 + "|c2=" + F(st.Fc) + "*c2|c3=c3|c4=" + F(st.Back) + "*c4|c5=" + F(st.Back) + "*c5|c6=" + F(st.Side) + "*c6" +
                         (st.Spread > 0 ? "+" + F(st.Spread) + "*c0-" + F(st.Spread * cancel) + "*c2" : "") +
                         "|c7=" + F(st.Side) + "*c7" + (st.Spread > 0 ? "+" + F(st.Spread) + "*c1-" + F(st.Spread * cancel) + "*c2" : ""));
            }
            else g.Append("aformat=channel_layouts=7.1");

            string bed = bed71 ? "7.1" : "5.1";
            string[] bedCh = bed71 ? new[] { "FL", "FR", "FC", "LFE", "BL", "BR", "SL", "SR" } : new[] { "FL", "FR", "FC", "LFE", "BL", "BR" };
            int pairs = heights / 2;
            int branches = 1 + (heights > 0 ? 1 : 0) + (wides ? 1 : 0);
            if (branches == 1 && !rev) return g.Append(bed71 ? "" : ",aformat=channel_layouts=5.1").ToString();   // 5.1 / 7.1 target, dry

            g.Append(branches == 1 ? "[m]" : ",asplit=" + branches + "[m]" + (heights > 0 ? "[h]" : "") + (wides ? "[w]" : ""));

            // 2) ground bed (+ its reverb in the surrounds)
            if (!rev) g.Append(";[m]" + (bed71 ? "" : "aformat=channel_layouts=5.1,"));
            else if (bed71)
            {
                // 7.1 bed + 4 sends (rears, sides) = 12 channels; one convolution, then each send is added to its speaker
                g.Append(";[m]pan=7.1.4|c0=c0|c1=c1|c2=c2|c3=c3|c4=c4|c5=c5|c6=c6|c7=c7|c8=" + SendL + "|c9=" + SendR + "|c10=" + SendL + "|c11=" + SendR);
                g.Append(ConvPass(8, new[] { 0.8, 0.8, 0.55, 0.55 }, st, Mask714));
                g.Append(",pan=7.1|c0=c0|c1=c1|c2=c2|c3=c3|c4=c4+c8|c5=c5+c9|c6=c6+c10|c7=c7+c11,");
            }
            else
            {
                // 5.1 bed (sides folded into the rears) + 2 sends = 8 channels
                g.Append(";[m]pan=7.1|c0=c0|c1=c1|c2=c2|c3=c3|c4=0.7*c4+0.7*c6|c5=0.7*c5+0.7*c7|c6=" + SendL + "|c7=" + SendR);
                g.Append(ConvPass(6, new[] { 0.8, 0.8 }, st, Mask71));
                g.Append(",pan=5.1|c0=c0|c1=c1|c2=c2|c3=c3|c4=c4+c6|c5=c5+c7,");
            }
            g.Append("channelsplit=channel_layout=" + bed + string.Concat(bedCh.Select(c => "[" + c + "]").ToArray()));
            var order = new List<string>(bedCh);

            // 3) wides: the instruments of FL/FR (voice already removed), keeping their side
            if (wides)
            {
                g.Append(";[w]pan=stereo|c0=c0|c1=c1,highpass=f=150,lowpass=f=8000,adelay=6|6,volume=" + F(st.Wide) +
                         ",channelsplit=channel_layout=stereo[WL][WR]");
                order.Add("WL"); order.Add("WR");
            }
            // 4) height layer: the ambience the upmix steered to sides/rears (reverb, room, cymbals, air),
            //    high-passed so voice body and bass stay on the ground
            if (heights > 0)
            {
                string hi0 = st.HInstr > 0 ? "+" + F(st.HInstr) + "*c0" : "", hi1 = st.HInstr > 0 ? "+" + F(st.HInstr) + "*c1" : "";
                string hl = "0.6*c6+0.4*c4" + hi0, hr = "0.6*c7+0.4*c5" + hi1;
                // with reverb, the dry height signal is high-passed inside the convolution (the sends keep their body)
                g.Append(";[h]" + (rev ? "pan=quad|c0=" + hl + "|c1=" + hr + "|c2=" + HSendL + "|c3=" + HSendR : "pan=stereo|c0=" + hl + "|c1=" + hr + ",highpass=f=" + F(st.HPass)) +
                         (pairs > 1 ? ",asplit=" + pairs + "[hf]" + (heights == 6 ? "[hs]" : "") + "[hb];[hf]" : ","));
                var groups = new List<string[]>();     // label prefix, names, delay, volume, reverb amount, echo
                groups.Add(new[] { "TF", "TFL", "TFR", st.Delay.ToString(), F(st.HFront), "0.65", "" });
                if (heights == 6) groups.Add(new[] { "TS", "TSL", "TSR", (st.Delay + 7).ToString(), F(st.HSide), "0.8", "" });
                if (heights >= 4) groups.Add(new[] { "TB", "TBL", "TBR", (st.Delay + 15).ToString(), F(st.HBack), "0.9",
                    rev ? "" : (st.Room ? ",aecho=0.8:0.75:45|70:0.35|0.25" : ",aecho=0.8:0.6:35:0.2") });
                for (int i = 0; i < groups.Count; i++)
                {
                    var gr = groups[i];
                    if (i > 0) g.Append(";[" + (gr[0] == "TS" ? "hs" : "hb") + "]");
                    string dl = rev ? gr[3] + "|" + gr[3] + "|" + gr[3] + "|" + gr[3] : gr[3] + "|" + gr[3];
                    g.Append("adelay=" + dl + gr[6] + ",volume=" + gr[4]);
                    if (rev)
                    {
                        double amt = double.Parse(gr[5], System.Globalization.CultureInfo.InvariantCulture);
                        g.Append(ConvPass(2, new[] { amt, amt }, st, MaskQuad, st.HPass));
                        g.Append(",pan=stereo|c0=c0+c2|c1=c1+c3");
                    }
                    g.Append(",channelsplit=channel_layout=stereo[" + gr[1] + "][" + gr[2] + "]");
                    order.Add(gr[1]); order.Add(gr[2]);
                }
            }
            string target = TargetOrder(l);
            var want = target.Split('+');
            if (want.Length != order.Count) return null;
            g.Append(";" + string.Concat(want.Select(c => "[" + c + "]").ToArray()) + "join=inputs=" + want.Length + ":channel_layout=" + target +
                     ":map=" + string.Join("|", want.Select((c, i) => i + ".0-" + c).ToArray()));
            return g.ToString();
        }

        static string StdOrder(string std)
        {
            switch (std)
            {
                case "5.1": return "FL+FR+FC+LFE+BL+BR";
                case "7.1": return "FL+FR+FC+LFE+BL+BR+SL+SR";
                case "5.1.2": return "FL+FR+FC+LFE+BL+BR+TFL+TFR";
                case "5.1.4": return "FL+FR+FC+LFE+BL+BR+TFL+TFR+TBL+TBR";
                case "7.1.2": return "FL+FR+FC+LFE+BL+BR+SL+SR+TFL+TFR";
                case "7.1.4": return "FL+FR+FC+LFE+BL+BR+SL+SR+TFL+TFR+TBL+TBR";
                default: return std;
            }
        }

        /// Automatic codec for immersive layouts when the format's default codec cannot carry them.
        public static string AutoCodec(string ext)
        {
            switch ((ext ?? "").ToLowerInvariant())
            {
                case "wav": case "w64": case "caf": case "mov": case "mkv": case "mka": case "nut": return "pcm24";
                case "opus": case "ogg": case "oga": case "webm": return "opus";
                default: return null;
            }
        }

        static int MaxChannels(string akey)
        {
            switch (CommandBuilder.MapCodec(akey, false))
            {
                case "mp3": case "mp2": case "wmav2": return 2;
                case "amr": return 1;
                case "ac3": case "eac3": case "dts": return 6;
                case "aac": case "flac": case "alac": return 8;
                case "opus": return 255;      // mapping family 255 above 8 channels
                default: return 0;            // PCM, Vorbis, WavPack, TTA, other encoders: no check
            }
        }

        /// Null when OK, otherwise a readable error.
        public static string Check(string key, string akey)
        {
            var l = Get(key);
            if (l == null || l.Count == 0) return null;
            int max = MaxChannels(akey);
            if (max > 0 && l.Count > max)
                return "Channel layout " + l.Key + " needs " + l.Count + " channels but this audio codec supports only " + max +
                       ". Pick an output format that carries it: MKV or MOV for video; WAV (PCM 24-bit), W64, CAF, MKA or OPUS for audio. The codec is then chosen automatically. To keep an existing Dolby Atmos / DTS:X track, choose \"Passthrough\".";
            return null;
        }

        public static string ExtraArgs(string key, string akey)
        {
            var l = Get(key);
            // Opus only has a standard mapping for mono/stereo/quad/5.0/5.1/6.1/7.1 — anything else uses mapping family 255
            if (l != null && l.Count > 0 && CommandBuilder.MapCodec(akey, false) == "opus" &&
                !new[] { "mono", "stereo", "quad", "5.0", "5.1", "6.1", "7.1" }.Contains(l.Std ?? l.Key)) return "-mapping_family 255";
            return "";
        }
    }
}
