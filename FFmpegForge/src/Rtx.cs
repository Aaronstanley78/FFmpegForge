using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FFmpegForge
{
    /// NVIDIA RTX AI video processing: RTX Video Super Resolution, RTX TrueHDR and DLSS-class
    /// NGX super resolution.
    ///
    /// Two engines are supported:
    ///  1. "native": FFmpeg builds that include NVIDIA's RTX Video filters
    ///     (vsr_cuda / vsr_drv_cuda, truehdr_cuda / truehdr_drv_cuda, isr_cuda, dlpp_drv_cuda).
    ///     The real options of each filter are read live from ffmpeg.exe.
    ///  2. "processor": RTXVideoProcessor.exe (open-source CLI built on the official
    ///     NVIDIA RTX Video SDK). VSR + TrueHDR only, fixed 2x upscale, NVENC HEVC output.
    public static class Rtx
    {
        public static string ProcessorPath = "";

        /// Built-in driver engine: ForgeRTX.exe (D3D11 video processor + NVIDIA driver extensions).
        public static string DriverPath
        {
            get
            {
                string app = AppDomain.CurrentDomain.BaseDirectory;
                foreach (var c in new[] { Path.Combine(app, "ForgeRTX.exe"), Path.Combine(app, "FFmpegForge", "ForgeRTX.exe"), Path.Combine(app, "bin", "ForgeRTX.exe") })
                    if (File.Exists(c)) return c;
                return null;
            }
        }
        public static bool HasDriver { get { return DriverPath != null && (Environment.OSVersion.Platform == PlatformID.Win32NT || Environment.GetEnvironmentVariable("FORGE_DRIVER_TEST") == "1"); } }

        public static readonly string[] VsrFilters = { "vsr_cuda", "vsr_drv_cuda" };
        public static readonly string[] HdrFilters = { "truehdr_cuda", "truehdr_drv_cuda" };
        public static readonly string[] DlssFilters = { "isr_cuda", "dlpp_drv_cuda" };

        public static string Pick(string[] names)
        {
            foreach (var n in names) if (FilterCatalog.Find(n) != null) return n;
            if (FilterCatalog.All.Count == 0) foreach (var n in names) if (FilterCatalog.Has(n)) return n;
            return null;
        }

        public static bool NativeVsr { get { return Pick(VsrFilters) != null; } }
        public static bool NativeHdr { get { return Pick(HdrFilters) != null; } }
        public static bool NativeDlss { get { return Pick(DlssFilters) != null; } }
        public static bool AnyNative { get { return NativeVsr || NativeHdr || NativeDlss; } }

        public static bool HasProcessor { get { return !string.IsNullOrEmpty(ProcessorPath) && File.Exists(ProcessorPath); } }

        public static void FindProcessor(string saved)
        {
            var cands = new List<string>();
            if (!string.IsNullOrEmpty(saved)) cands.Add(saved);
            string app = AppDomain.CurrentDomain.BaseDirectory;
            cands.Add(Path.Combine(app, "RTXVideoProcessor.exe"));
            cands.Add(Path.Combine(app, "RTXVideoProcessor", "RTXVideoProcessor.exe"));
            cands.Add(Path.Combine(app, "bin", "RTXVideoProcessor.exe"));
            cands.Add(@"C:\FFmpeg\bin\RTXVideoProcessor.exe");
            cands.Add(@"C:\FFmpeg\RTXVideoProcessor\RTXVideoProcessor.exe");
            if (!string.IsNullOrEmpty(Runner.FFmpeg)) cands.Add(Path.Combine(Path.GetDirectoryName(Runner.FFmpeg), "RTXVideoProcessor.exe"));
            foreach (var c in cands) { try { if (File.Exists(c)) { ProcessorPath = c; return; } } catch { } }
            ProcessorPath = "";
        }

        /// Which engine will process this job ("native", "driver", "processor" or null) + reason when null.
        /// DlssSkipped = true when DLSS was ticked but cannot run (VSR / TrueHDR still run).
        public static string Engine(ConvSettings s, out string why)
        {
            why = null;
            s.DlssSkipped = false;
            if (!s.RtxActive) return null;
            bool nativeOk = (!s.RtxVsr || NativeVsr) && (!s.RtxHdr || NativeHdr) && (!s.Dlss || NativeDlss);
            if (s.RtxEnginePref == "auto" || s.RtxEnginePref == "native")
                if (nativeOk) return "native";
            bool vsrOrHdr = s.RtxVsr || s.RtxHdr;
            if (s.Dlss && !NativeDlss)
            {
                if (!vsrOrHdr)
                {
                    why = "DLSS Super Resolution is a game technology: it needs motion vectors that only a game engine provides. NVIDIA has no public DLSS for video files and your FFmpeg build has no NGX filter (isr_cuda / dlpp_drv_cuda). Use \"RTX Video Super Resolution\" instead: it is NVIDIA's AI upscaler made for video.";
                    return null;
                }
                s.DlssSkipped = true;
            }
            if ((s.RtxEnginePref == "auto" || s.RtxEnginePref == "driver") && HasDriver && vsrOrHdr) return "driver";
            if ((s.RtxEnginePref == "auto" || s.RtxEnginePref == "processor") && HasProcessor && vsrOrHdr) return "processor";
            if (s.RtxEnginePref == "native") why = "Your FFmpeg build has no NVIDIA RTX Video filters. Set the RTX engine to \"Auto\" or \"NVIDIA driver\".";
            else if (s.RtxEnginePref == "processor") why = "RTXVideoProcessor.exe not found. Set the RTX engine to \"Auto\" or \"NVIDIA driver\".";
            else why = "ForgeRTX.exe (the built-in NVIDIA driver engine) is missing next to FFmpegForge.exe. Re-extract the FFmpeg Forge archive.";
            return null;
        }

        static string Norm(string n) { return n.Replace("_", "").Replace("-", "").ToLowerInvariant(); }

        /// Builds "filter=opt=val:…" mapping our generic settings onto the filter's real option names.
        static string AutoExpr(string filter, Dictionary<string, string[]> wanted)
        {
            FilterHelp h = null;
            try { h = FilterCatalog.GetHelp(filter); } catch { }
            var vals = new List<KeyValuePair<string, string>>();
            if (h != null)
            {
                foreach (var kv in wanted)
                {
                    string[] aliases = kv.Key.Split('|');
                    var opt = h.Options.FirstOrDefault(o => aliases.Contains(Norm(o.Name)) || (o.Alias != null && aliases.Contains(Norm(o.Alias))));
                    if (opt == null) continue;
                    string v = kv.Value[0];
                    // enum options (low/medium/high/ultra…): map 1..N onto the listed constants
                    int idx;
                    if (opt.Consts.Count >= 2 && kv.Value.Length > 1 && kv.Value[1] == "level" && int.TryParse(v, out idx))
                    {
                        string[] levels = { "low", "medium", "high", "ultra" };
                        var byName = opt.Consts.FirstOrDefault(c => c[0].Equals(levels[Math.Max(0, Math.Min(3, idx - 1))], StringComparison.OrdinalIgnoreCase));
                        if (byName != null) { vals.Add(new KeyValuePair<string, string>(opt.Name, byName[0])); continue; }
                        var named = opt.Consts.Where(c => !c[0].Equals("auto", StringComparison.OrdinalIgnoreCase) && !c[0].Equals("default", StringComparison.OrdinalIgnoreCase)).ToList();
                        if (named.Count > 0) v = named[Math.Max(0, Math.Min(named.Count - 1, (int)Math.Round((idx - 1) * (named.Count - 1) / 3.0)))][0];
                    }
                    vals.Add(new KeyValuePair<string, string>(opt.Name, v));
                }
            }
            return FilterCatalog.BuildExpr(filter, vals, -1, -1);
        }

        /// Native FFmpeg chain (CUDA upload → RTX filters → download), or null.
        public static string NativeChain(ConvSettings s)
        {
            var parts = new List<string>();
            if (s.Dlss)
            {
                string f = Pick(DlssFilters);
                parts.Add(!string.IsNullOrWhiteSpace(s.DlssExpr) ? s.DlssExpr.Trim() : AutoExpr(f, new Dictionary<string, string[]> {
                    { "scale|scalefactor|factor|upscale|ratio", new[] { Formats.Inv(s.DlssScale) } },
                    { "quality|qualitylevel|mode|preset|level", new[] { s.DlssQuality.ToString(), "level" } } }));
            }
            if (s.RtxVsr)
            {
                string f = Pick(VsrFilters);
                parts.Add(!string.IsNullOrWhiteSpace(s.VsrExpr) ? s.VsrExpr.Trim() : AutoExpr(f, new Dictionary<string, string[]> {
                    { "quality|qualitylevel|level|q", new[] { s.VsrQuality.ToString(), "level" } },
                    { "scale|scalefactor|factor|upscale|ratio", new[] { s.VsrScale.ToString() } } }));
            }
            if (s.RtxHdr)
            {
                string f = Pick(HdrFilters);
                parts.Add(!string.IsNullOrWhiteSpace(s.HdrExpr) ? s.HdrExpr.Trim() : AutoExpr(f, new Dictionary<string, string[]> {
                    { "contrast", new[] { s.HdrContrast.ToString() } },
                    { "saturation", new[] { s.HdrSaturation.ToString() } },
                    { "middlegray|midgray|middlegrey|midgrey", new[] { s.HdrMiddleGray.ToString() } },
                    { "maxluminance|maxlum|peakluminance|peak|maxnits|luminance", new[] { s.HdrMaxLum.ToString() } } }));
            }
            if (parts.Count == 0) return null;
            return "format=nv12,hwupload_cuda," + string.Join(",", parts.ToArray()) + ",hwdownload,format=nv12|p010le";
        }

        /// Command line for RTXVideoProcessor.exe (FFmpeg-compatible mode).
        public static string ProcessorArgs(Job job, ConvSettings s, string outPath, out double expected, out string error)
        {
            expected = 0; error = null;
            string ext = s.Format.Ext.ToLowerInvariant();
            if (ext != "mp4" && ext != "mkv" && ext != "mov" && ext != "m4v" && ext != "m3u8")
            { error = "RTXVideoProcessor writes MP4 / MKV / MOV only. Choose one of these output formats."; return null; }
            if (job.Kind != MediaKind.Video) { error = "RTX Video processing needs a video source."; return null; }
            if (s.ScaleActive) { error = "\"Scale\" / OptiScaler and NVIDIA RTX AI are different upscalers: tick only one of them."; return null; }
            var sb = new StringBuilder();
            sb.Append("-y -i \"").Append(job.Path).Append("\" ");
            double ss = s.TrimStart > 0 ? s.TrimStart : 0;
            double dur = job.Duration;
            if (ss > 0) sb.Append("-ss ").Append(Formats.Inv(ss)).Append(' ');
            if (s.TrimEnd > 0 && s.TrimEnd > ss) { sb.Append("-t ").Append(Formats.Inv(s.TrimEnd - ss)).Append(' '); dur = s.TrimEnd - ss; }
            else if (dur > 0) dur -= ss;
            expected = Math.Max(0, dur);
            if (s.RtxVsr) sb.Append("--vsr-quality ").Append(Math.Max(1, Math.Min(4, s.VsrQuality))).Append(' ');
            else sb.Append("--no-vsr ");
            if (s.RtxHdr)
                sb.Append("--thdr-contrast ").Append(s.HdrContrast)
                  .Append(" --thdr-saturation ").Append(s.HdrSaturation)
                  .Append(" --thdr-middle-gray ").Append(s.HdrMiddleGray)
                  .Append(" --thdr-max-luminance ").Append(s.HdrMaxLum).Append(' ');
            else sb.Append("--no-thdr ");
            if (s.Fps > 0) sb.Append("-r ").Append(Formats.Inv(s.Fps)).Append(' ');
            if (s.VBitrate > 0) sb.Append("--nvenc-rc vbr --nvenc-bitrate ").Append(Formats.Inv(s.VBitrate / 1000.0)).Append(' ');
            else sb.Append("--nvenc-qp ").Append(new[] { 17, 21, 24, 27, 31 }[Math.Max(0, Math.Min(4, s.QualityIdx))]).Append(' ');
            sb.Append("--nvenc-preset ").Append(new[] { "p1", "p2", "p4", "p5", "p6", "p7" }[Math.Max(0, Math.Min(5, s.PresetIdx))]).Append(' ');
            if (s.ACodec == "none") sb.Append("-an ");
            else sb.Append("-c:a copy ");
            sb.Append("\"").Append(outPath).Append("\"");
            return sb.ToString();
        }

        public static string StatusText()
        {
            var nat = new List<string>();
            foreach (var arr in new[] { VsrFilters, HdrFilters, DlssFilters }) { var p = Pick(arr); if (p != null) nat.Add(p); }
            string a = nat.Count > 0 ? "FFmpeg RTX filters: " + string.Join(", ", nat.ToArray()) : "FFmpeg RTX filters: not in this build";
            string c = HasDriver ? "NVIDIA driver engine: ready (ForgeRTX.exe) — enable RTX Video in the NVIDIA App" : "NVIDIA driver engine: ForgeRTX.exe missing";
            string b = HasProcessor ? "RTXVideoProcessor: " + Path.GetFileName(Path.GetDirectoryName(ProcessorPath)) + "\\" + Path.GetFileName(ProcessorPath) : "RTXVideoProcessor: not found";
            return c + "\n" + a + "\n" + b;
        }
    
        // =====================================================================
        //  Driver engine: ffmpeg (decode NV12) -> ForgeRTX.exe (D3D11 + NVIDIA) -> ffmpeg (encode)
        // =====================================================================
        static string Q(string x) { return "\"" + x.Replace("\"", "\\\"") + "\""; }

        public class DriverPlan
        {
            public string DecArgs, HelperArgs, EncArgs; public double Expected; public int W, H, OW, OH;
            public string Text() { return "ffmpeg " + DecArgs + "  |  ForgeRTX " + HelperArgs + "  |  ffmpeg " + EncArgs; }
        }

        public static DriverPlan PlanDriver(Job job, ConvSettings s, string outPath, out string error)
        {
            error = null;
            var fmt = s.Format;
            s.SourceChannels = job.AudioChannels; s.SourceIsVideo = true;
            if (s.ScaleActive) { error = "\"Scale\" / OptiScaler and NVIDIA RTX AI are different upscalers: tick only one of them."; return null; }
            if (fmt.Kind != MediaKind.Video) { error = "RTX processing needs a video output format."; return null; }
            if (job.Kind != MediaKind.Video && !CommandBuilder.IsAnimatedImageSource(job)) { error = "RTX processing needs a video source."; return null; }
            string vkey = fmt.VCodec;
            if (vkey == "gif" || vkey == "apng" || vkey == "webpanim") { error = "RTX processing is not available for GIF / APNG / animated WEBP outputs."; return null; }
            if (s.VCodec != "auto") vkey = s.VCodec;
            if (vkey == "copy") { error = "RTX processing needs a real video codec (not stream copy)."; return null; }
            if (!string.IsNullOrWhiteSpace(s.FilterComplex)) { error = "The advanced filtergraph cannot be combined with the RTX driver engine. Use the filter chain instead."; return null; }
            string mk = CommandBuilder.MapCodec(vkey, true);
            if (s.RtxHdr && (mk == "h264" || mk == "nvh264" || mk == "qsvh264" || mk == "amfh264" || mk == "mpeg2" || mk == "mpeg4" || mk == "mjpeg" || mk == "wmv2" || mk == "theora" || mk == "vp8"))
            { error = "RTX Video HDR produces 10-bit HDR10: choose H.265 (HEVC), AV1, VP9, ProRes or DNxHR HQX. H.264 and other 8-bit codecs cannot carry HDR."; return null; }
            int w = job.Width - job.Width % 2, h = job.Height - job.Height % 2;
            if (w < 16 || h < 16) { error = "Could not read the video size (ffprobe). Re-add the file."; return null; }
            int f = s.RtxVsr ? Math.Max(1, s.VsrScale) : 1;
            int ow = w * f, oh = h * f;
            int fn = job.FpsNum > 0 ? job.FpsNum : 30, fd = job.FpsDen > 0 ? job.FpsDen : 1;
            if (s.Fps > 0) { fn = (int)Math.Round(s.Fps * 1000); fd = 1000; }
            string rate = fn + "/" + fd;

            double ss = s.TrimStart > 0 ? s.TrimStart : 0;
            double to = s.TrimEnd > 0 ? s.TrimEnd : -1;
            string trim = (ss > 0 ? "-ss " + Formats.Inv(ss) + " " : "") + (to > ss && to > 0 ? "-to " + Formats.Inv(to) + " " : "");
            double dur = job.Duration;
            if (to > 0 && (dur <= 0 || to < dur)) dur = to;
            dur = Math.Max(0, dur - ss);

            var p = new DriverPlan { W = w, H = h, OW = ow, OH = oh, Expected = dur };
            p.DecArgs = "-hide_banner -nostdin -loglevel error " + trim + "-i " + Q(job.Path) +
                        " -map 0:v:0 -an -sn -dn -vf " + Q("scale=" + w + ":" + h + ",format=nv12") + " -r " + rate + " -pix_fmt nv12 -f rawvideo -";
            p.HelperArgs = "-w " + w + " -h " + h + " -ow " + ow + " -oh " + oh + " -vsr " + (s.RtxVsr ? 1 : 0) + " -hdr " + (s.RtxHdr ? 1 : 0) +
                           " -csp " + (h >= 720 ? 709 : 601) + " -fps " + fn + " " + fd;

            // encoder: raw frames from ForgeRTX + audio from the source
            var vf = new List<string>();
            CommandBuilder.AddRotate(vf, s.Rotate);
            string userV = s.UserVideoChain();
            if (userV.Length > 0) vf.Add(userV);
            { string dn = Denoise.Filter(s, job, false); if (dn != null) vf.Add(dn); }
            bool even = CommandBuilder.NeedsEven(vkey);
            string sc = CommandBuilder.ScaleFilter(s, even);
            if (sc != null) vf.Add(sc);
            if (s.RtxHdr) vf.Add("scale=out_color_matrix=bt2020:out_range=tv");
            else vf.Add("scale=out_color_matrix=" + (oh >= 720 ? "bt709" : "smpte170m") + ":out_range=tv");
            if (even) vf.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");

            string va = CommandBuilder.VideoArgs(vkey, s, fmt.Ext);
            if (s.RtxHdr)
            {
                bool nv = va.Contains("_nvenc");
                va = va.Replace("-pix_fmt yuv420p", "-pix_fmt " + (nv ? "p010le" : "yuv420p10le"));
                if (!va.Contains("-pix_fmt")) va += " -pix_fmt " + (nv ? "p010le" : "yuv420p10le");
                va += " -color_primaries bt2020 -color_trc smpte2084 -colorspace bt2020nc -color_range tv";
                if (va.Contains("libx265")) va += " -x265-params hdr10=1:repeat-headers=1";
            }
            else va += oh >= 720 ? " -color_primaries bt709 -color_trc bt709 -colorspace bt709" : "";

            string akey = s.ACodec == "auto" ? fmt.ACodec : s.ACodec;
            if (s.ChLayout == "copy" && akey != "none") akey = "copy";
            string audio;
            if (akey == "none" || (job.Probed && !job.HasAudio)) audio = "-an";
            else
            {
                string userA = s.UserAudioChain();
                if (akey == "copy" && userA.Length > 0) { error = "Audio filters cannot be used with audio stream copy."; return null; }
                string af = akey != "copy" ? CommandBuilder.AudioChain(s, userA) : null;
                if (akey != "copy")
                {
                    string lerr = ChannelLayouts.Check(s.ChLayout, akey);
                    if (lerr != null && s.ACodec == "auto") { string alt = ChannelLayouts.AutoCodec(fmt.Ext); if (alt != null) { akey = alt; lerr = null; } }
                    if (lerr != null) { error = lerr; return null; }
                }
                audio = "-map 1:a:0? " + (af != null ? "-af " + Q(af) + " " : "") + CommandBuilder.AudioFormatArgs(s, akey != "copy") + " " +
                        CommandBuilder.AudioArgs(akey, s.AKbps) + (akey != "copy" ? " " + ChannelLayouts.ExtraArgs(s.ChLayout, akey) : "");
            }

            string inPix = s.RtxHdr ? "x2bgr10le" : "bgra";
            string inTags = s.RtxHdr ? "-color_primaries bt2020 -color_trc smpte2084 -colorspace rgb -color_range pc " : "-color_range pc ";
            p.EncArgs = "-hide_banner -nostdin -y -loglevel warning -progress pipe:1 -nostats " +
                        "-f rawvideo -pix_fmt " + inPix + " -s " + ow + "x" + oh + " -framerate " + rate + " " + inTags + "-i - " +
                        trim + "-i " + Q(job.Path) + " -map 0:v:0 " + audio + " -vf " + Q(string.Join(",", vf.ToArray())) + " " + va +
                        " -sn -shortest " + (fmt.Extra ?? "") + (fmt.Dynamic && fmt.Muxer != null ? " -f " + fmt.Muxer : "") +
                        (string.IsNullOrWhiteSpace(s.CustomArgs) ? "" : " " + s.CustomArgs.Trim()) + " -strict -2 " + Q(outPath);
            return p;
        }

        static System.Diagnostics.Process Start(string exe, string args, bool redirectIn)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = redirectIn, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8
            };
            return System.Diagnostics.Process.Start(psi);
        }

        static void Pump(Stream from, Stream to, Action<Exception> onError)
        {
            try
            {
                var buf = new byte[1 << 20];
                int n;
                while ((n = from.Read(buf, 0, buf.Length)) > 0) to.Write(buf, 0, n);
            }
            catch (Exception ex) { onError(ex); }
            finally { try { to.Close(); } catch { } }
        }

        /// Runs the 3-process pipeline. Returns the encoder exit code (-1 = cancelled).
        public static int RunDriver(DriverPlan plan, Action<double> onProgress, Action<string> onLog, Func<bool> cancelled, out string lastErrors)
        {
            var errs = new List<string>();
            Action<string, string> addErr = (tag, line) =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                lock (errs) { errs.Add(tag + line); if (errs.Count > 40) errs.RemoveAt(0); }
                onLog(tag + line);
            };
            System.Diagnostics.Process dec = null, rtx = null, enc = null;
            try
            {
                enc = new System.Diagnostics.Process { StartInfo = new System.Diagnostics.ProcessStartInfo(Runner.FFmpeg, plan.EncArgs)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8
                } };
                enc.OutputDataReceived += (o, e) =>
                {
                    if (e.Data == null) return;
                    string l = e.Data;
                    if (plan.Expected > 0 && (l.StartsWith("out_time_us=") || l.StartsWith("out_time_ms=")))
                    {
                        long us;
                        if (long.TryParse(l.Substring(12), out us) && us > 0) onProgress(Math.Min(1.0, us / 1e6 / plan.Expected));
                    }
                    else if (l == "progress=end") onProgress(1.0);
                };
                enc.ErrorDataReceived += (o, e) => addErr("", e.Data);
                enc.Start();
                enc.BeginOutputReadLine();
                enc.BeginErrorReadLine();

                rtx = Start(DriverPath, plan.HelperArgs, true);
                rtx.ErrorDataReceived += (o, e) => addErr("", e.Data);
                rtx.BeginErrorReadLine();

                dec = Start(Runner.FFmpeg, plan.DecArgs, false);
                dec.ErrorDataReceived += (o, e) => addErr("[decoder] ", e.Data);
                dec.BeginErrorReadLine();

                var d1 = dec; var r1 = rtx; var e1 = enc;
                var t1 = new System.Threading.Thread(() => Pump(d1.StandardOutput.BaseStream, r1.StandardInput.BaseStream, ex => { })) { IsBackground = true };
                var t2 = new System.Threading.Thread(() => Pump(r1.StandardOutput.BaseStream, e1.StandardInput.BaseStream, ex => { })) { IsBackground = true };
                t1.Start(); t2.Start();

                while (!enc.WaitForExit(200))
                {
                    if (cancelled())
                    {
                        foreach (var pr in new[] { dec, rtx, enc }) { try { if (!pr.HasExited) pr.Kill(); } catch { } }
                        lastErrors = "Cancelled";
                        return -1;
                    }
                    if (rtx.HasExited && rtx.ExitCode != 0 && !enc.HasExited)
                    {
                        System.Threading.Thread.Sleep(300);
                        try { if (!dec.HasExited) dec.Kill(); } catch { }
                    }
                }
                enc.WaitForExit();
                try { if (!dec.HasExited) dec.Kill(); } catch { }
                rtx.WaitForExit(3000);
                int code = enc.ExitCode;
                if (code == 0 && rtx.HasExited && rtx.ExitCode != 0) code = 100 + rtx.ExitCode;
                lock (errs) lastErrors = string.Join(Environment.NewLine, errs.ToArray());
                return code;
            }
            catch (Exception ex)
            {
                foreach (var pr in new[] { dec, rtx, enc }) { try { if (pr != null && !pr.HasExited) pr.Kill(); } catch { } }
                lastErrors = ex.Message;
                return -2;
            }
        }
    }
}
