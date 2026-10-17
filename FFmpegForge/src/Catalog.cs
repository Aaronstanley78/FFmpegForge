using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FFmpegForge
{
    public class MuxerInfo
    {
        public string Name, LongName, Exts = "", VCodec, ACodec;
    }

    public class EncoderInfo
    {
        public string Name, Desc, Codec; public char Type; public bool Experimental;
    }

    /// Reads every muxer (output format) and every encoder from the installed ffmpeg.exe.
    public static class FormatCatalog
    {
        public static List<MuxerInfo> Muxers = new List<MuxerInfo>();
        public static List<EncoderInfo> Encoders = new List<EncoderInfo>();
        public static List<OutFormat> Dynamic = new List<OutFormat>();
        public static bool Loaded;
        public static string LoadError;

        static readonly HashSet<string> Excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "null", "tee", "fifo", "fifo_test", "framecrc", "framehash", "framemd5", "crc", "hash", "md5", "streamhash",
            "uncodedframecrc", "rtp", "rtp_mpegts", "rtsp", "sap", "image2pipe", "chromaprint", "webm_chunk",
            "webm_dash_manifest", "data", "ffmetadata", "segment", "stream_segment", "ssegment", "hds", "smoothstreaming", "ico",
            // output devices (screen / sound card), not files
            "alsa", "fbdev", "opengl", "oss", "pulse", "sdl", "sdl2", "xv", "v4l2", "caca", "audiotoolbox", "sndio", "decklink"
        };

        static readonly Regex MuxLine = new Regex(@"^\s*D?E\s+(\S+)\s+(.*)$");
        static readonly Regex EncLine = new Regex(@"^\s([VAS])([F.])([S.])([X.])([B.])([D.])\s+(\S+)\s+(.*)$");

        static string Run(string args)
        {
            var psi = new ProcessStartInfo(Runner.FFmpeg, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(15000);
                return o + "\n" + err.Result;
            }
        }

        static string CachePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FFmpegForge", "formats.cache"); }
        }

        static string CacheKey()
        {
            try { var fi = new FileInfo(Runner.FFmpeg); return "v3|" + fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks; }
            catch { return "v3|?"; }
        }

        public static void Load()
        {
            try
            {
                // ---------- encoders (one fast call) ----------
                var enc = new List<EncoderInfo>();
                foreach (var raw in Run("-hide_banner -encoders").Split('\n'))
                {
                    var m = EncLine.Match(raw.TrimEnd('\r'));
                    if (!m.Success || m.Groups[7].Value == "=") continue;
                    if (m.Groups[7].Value == "anull" || m.Groups[7].Value == "vnull" || m.Groups[7].Value == "comfortnoise" || m.Groups[7].Value == "wrapped_avframe") continue;
                    string desc = m.Groups[8].Value.Trim();
                    string codec = m.Groups[7].Value;
                    var cm = Regex.Match(desc, @"\(codec (\S+)\)\s*$");
                    if (cm.Success) { codec = cm.Groups[1].Value; desc = desc.Substring(0, cm.Index).Trim(); }
                    enc.Add(new EncoderInfo { Type = m.Groups[1].Value[0], Experimental = m.Groups[4].Value == "X", Name = m.Groups[7].Value, Desc = desc, Codec = codec });
                }
                Encoders = enc.OrderBy(e => e.Name).ToList();

                // ---------- muxers (cached, details read in parallel) ----------
                string key = CacheKey();
                List<MuxerInfo> mux = null;
                try
                {
                    if (File.Exists(CachePath))
                    {
                        var lines = File.ReadAllLines(CachePath, Encoding.UTF8);
                        if (lines.Length > 1 && lines[0] == key)
                            mux = lines.Skip(1).Select(l => l.Split('\t')).Where(p => p.Length >= 5)
                                .Select(p => new MuxerInfo { Name = p[0], LongName = p[1], Exts = p[2], VCodec = p[3].Length > 0 ? p[3] : null, ACodec = p[4].Length > 0 ? p[4] : null }).ToList();
                    }
                }
                catch { mux = null; }

                if (mux == null)
                {
                    var names = new List<string[]>();
                    bool started = false;
                    foreach (var raw in Run("-hide_banner -muxers").Split('\n'))
                    {
                        string l = raw.TrimEnd('\r');
                        if (l.Trim() == "--") { started = true; continue; }
                        if (!started) continue;
                        var m = MuxLine.Match(l);
                        if (m.Success) names.Add(new[] { m.Groups[1].Value, m.Groups[2].Value.Trim() });
                    }
                    var bag = new System.Collections.Concurrent.ConcurrentBag<MuxerInfo>();
                    Parallel.ForEach(names, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, n =>
                    {
                        var mi = new MuxerInfo { Name = n[0], LongName = n[1] };
                        try
                        {
                            foreach (var raw in Run("-hide_banner -h muxer=" + n[0].Split(',')[0]).Split('\n'))
                            {
                                string l = raw.Trim();
                                if (l.StartsWith("Common extensions:")) mi.Exts = l.Substring(18).Trim().TrimEnd('.');
                                else if (l.StartsWith("Default video codec:")) mi.VCodec = l.Substring(20).Trim().TrimEnd('.');
                                else if (l.StartsWith("Default audio codec:")) mi.ACodec = l.Substring(20).Trim().TrimEnd('.');
                                else if (l.EndsWith("AVOptions:")) break;
                            }
                        }
                        catch { }
                        bag.Add(mi);
                    });
                    mux = bag.OrderBy(m => m.Name).ToList();
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                        var outl = new List<string> { key };
                        outl.AddRange(mux.Select(m => string.Join("\t", new[] { m.Name, m.LongName.Replace("\t", " "), m.Exts, m.VCodec ?? "", m.ACodec ?? "" })));
                        File.WriteAllLines(CachePath, outl.ToArray(), Encoding.UTF8);
                    }
                    catch { }
                }
                Muxers = mux;
                BuildDynamic();
                LoadError = null;
            }
            catch (Exception ex) { LoadError = ex.Message; }
            Loaded = true;
        }

        static void BuildDynamic()
        {
            var list = new List<OutFormat>();
            foreach (var m in Muxers)
            {
                string first = m.Name.Split(',')[0];
                if (Excluded.Contains(first) || m.LongName.ToLowerInvariant().Contains("device")) continue;
                if (first == "image2")
                {
                    // every still-image extension known by FFmpeg (codec guessed from the extension)
                    foreach (var e in m.Exts.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
                        list.Add(new OutFormat(MediaKind.Image, e, e.ToUpperInvariant() + " — image  [image2]", "auto", "none", "", true) { Dynamic = true, Muxer = "image2" });
                    continue;
                }
                if (m.VCodec == null && m.ACodec == null) continue;   // subtitles / data only
                // hide formats whose default codec cannot be encoded by this build
                if (m.VCodec != null && !HasCodecEncoder(m.VCodec)) continue;
                if (m.VCodec == null && !HasCodecEncoder(m.ACodec)) continue;
                if (m.VCodec != null && m.ACodec != null && !HasCodecEncoder(m.ACodec)) m.ACodec = HasCodecEncoder("aac") ? "aac" : null;
                string ext = m.Exts.Split(',')[0].Trim();
                if (ext.Length == 0) ext = first;
                MediaKind k = m.VCodec != null ? MediaKind.Video : MediaKind.Audio;
                string label = ext.ToUpperInvariant() + " — " + m.LongName + "  [" + first + "]";
                list.Add(new OutFormat(k, ext, label, m.VCodec != null ? "codec:" + m.VCodec : null, m.ACodec != null ? "codec:" + m.ACodec : "none", "", false) { Dynamic = true, Muxer = first });
            }
            Dynamic = list.OrderBy(f => f.Ext).ThenBy(f => f.Label).ToList();
        }

        public static IEnumerable<EncoderInfo> Of(char type) { return Encoders.Where(e => e.Type == type); }

        public static bool HasCodecEncoder(string codec)
        {
            if (Encoders.Count == 0) return true;
            if (codec == "wrapped_avframe") return false;
            return Encoders.Any(e => e.Codec == codec || e.Name == codec);
        }

        public static bool HasEncoder(string name) { return name == null || Encoders.Count == 0 || Encoders.Any(e => e.Name == name); }
    }
}
