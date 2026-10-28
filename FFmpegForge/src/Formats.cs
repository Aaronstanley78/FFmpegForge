using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FFmpegForge
{
    public enum MediaKind { Video, Audio, Image, Unknown }

    public class OutFormat
    {
        public MediaKind Kind;
        public string Ext;
        public string Label;
        public string VCodec;   // default video / image codec key
        public string ACodec;   // default audio codec key
        public string Extra;    // extra container options
        public bool Image2;     // uses the image2 muxer (single image)
        public bool Dynamic;    // read from ffmpeg.exe (not in the curated list)
        public string Muxer;    // forced muxer (-f) for dynamic formats

        public OutFormat(MediaKind kind, string ext, string label, string v, string a, string extra, bool image2)
        {
            Kind = kind; Ext = ext; Label = label; VCodec = v; ACodec = a; Extra = extra; Image2 = image2;
        }
        public override string ToString() { return Label; }
    }

    public class Choice
    {
        public string Key; public string Label;
        public Choice(string key, string label) { Key = key; Label = label; }
        public override string ToString() { return Label; }
    }

    public static class Formats
    {
        public static readonly List<OutFormat> All = new List<OutFormat>();

        // ---------- Recognised input extensions ----------
        public static readonly HashSet<string> VideoExt = Set(
            "mp4 m4v mkv mov qt avi wmv flv f4v webm mpg mpeg mpe m1v m2v mp2v ts m2t m2ts mts tp trp vob 3gp 3g2 ogv ogm ogx " +
            "asf rm rmvb divx xvid dv dif mxf nut y4m hevc h264 h265 264 265 avc ivf amv mjpeg mjpg swf wtv dvr-ms bik smk " +
            "nsv roq vp6 drc gxf r3d mod tod evo mk3d apng flc fli wm vro dat"); // dat = VCD
        public static readonly HashSet<string> AudioExt = Set(
            "mp3 wav wave flac aac m4a m4b m4r ogg oga opus wma aiff aif aifc alac ac3 eac3 ec3 dts dtshd mka amr awb ape wv " +
            "tta tak mp2 mp1 mpa mpc au snd caf voc ra w64 rf64 spx gsm 8svx dsf dff xm mod s3m it mptm shn ofr thd mlp truehd " +
            "adx hca at3 aa3 oma vqf qcp sln sbc weba");
        public static readonly HashSet<string> ImageExt = Set(
            "png jpg jpeg jpe jfif pjpeg webp bmp dib tif tiff gif ico cur tga icb vda vst psd exr hdr pic dpx cin jp2 j2k j2c jpx " +
            "jpc avif heic heif jxl qoi pcx ppm pgm pbm pam pnm pfm phm sgi rgb rgba bw ras sun xbm xpm xwd dds svg wbmp pgx " +
            "jls ptx sunras im1 im8 im24 im32 crw cr2 nef arw dng"); // RAW: preview only, depends on the build

        static HashSet<string> Set(string s)
        {
            return new HashSet<string>(s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        }

        public static string ExtOf(string path)
        {
            string e = Path.GetExtension(path);
            return string.IsNullOrEmpty(e) ? "" : e.Substring(1).ToLowerInvariant();
        }

        public static MediaKind KindFromExt(string path)
        {
            string e = ExtOf(path);
            if (VideoExt.Contains(e)) return MediaKind.Video;
            if (AudioExt.Contains(e)) return MediaKind.Audio;
            if (ImageExt.Contains(e)) return MediaKind.Image;
            return MediaKind.Unknown;
        }

        public static bool IsMediaFile(string path) { return KindFromExt(path) != MediaKind.Unknown; }

        public static string OpenDialogFilter()
        {
            Func<HashSet<string>, string> pat = s => string.Join(";", s.Select(x => "*." + x).ToArray());
            var all = new HashSet<string>(VideoExt); all.UnionWith(AudioExt); all.UnionWith(ImageExt);
            return "All media|" + pat(all) +
                   "|Videos|" + pat(VideoExt) +
                   "|Audio|" + pat(AudioExt) +
                   "|Images|" + pat(ImageExt) +
                   "|All files (*.*)|*.*";
        }

        // ---------- Codecs ----------
        public static readonly List<Choice> VideoCodecs = new List<Choice> {
            new Choice("auto",   "Auto (best for the format)"),
            new Choice("h264",   "H.264 / AVC  (libx264)"),
            new Choice("h265",   "H.265 / HEVC  (libx265)"),
            new Choice("av1",    "AV1  (SVT-AV1)"),
            new Choice("vp9",    "VP9  (libvpx-vp9)"),
            new Choice("vp8",    "VP8  (libvpx)"),
            new Choice("mpeg4",  "MPEG-4 Part 2  (DivX/Xvid)"),
            new Choice("mpeg2",  "MPEG-2"),
            new Choice("prores_proxy", "Apple ProRes 422 Proxy"),
            new Choice("prores_lt",    "Apple ProRes 422 LT"),
            new Choice("prores_std",   "Apple ProRes 422"),
            new Choice("prores",       "Apple ProRes 422 HQ"),
            new Choice("prores_4444",  "Apple ProRes 4444 (alpha)"),
            new Choice("prores_4444xq","Apple ProRes 4444 XQ (alpha)"),
            new Choice("dnxhr_lb",  "Avid DNxHR LB"),
            new Choice("dnxhr_sq",  "Avid DNxHR SQ"),
            new Choice("dnxhr",     "Avid DNxHR HQ"),
            new Choice("dnxhr_hqx", "Avid DNxHR HQX (10-bit)"),
            new Choice("dnxhr_444", "Avid DNxHR 444 (10-bit)"),
            new Choice("ffv1",   "FFV1  (lossless archiving)"),
            new Choice("theora", "Theora"),
            new Choice("wmv2",   "WMV2  (Windows Media)"),
            new Choice("mjpeg",  "Motion JPEG"),
            new Choice("nvh264", "H.264  GPU NVIDIA (NVENC)"),
            new Choice("nvh265", "H.265  GPU NVIDIA (NVENC)"),
            new Choice("nvav1",  "AV1  GPU NVIDIA (NVENC, RTX 40+)"),
            new Choice("qsvh264","H.264  GPU Intel (QuickSync)"),
            new Choice("amfh264","H.264  GPU AMD (AMF)"),
            new Choice("copy",   "Stream copy (no re-encoding)"),
        };

        public static readonly List<Choice> AudioCodecsForVideo = new List<Choice> {
            new Choice("auto",   "Auto (best for the format)"),
            new Choice("aac",    "AAC"),
            new Choice("mp3",    "MP3"),
            new Choice("opus",   "Opus"),
            new Choice("vorbis", "Vorbis"),
            new Choice("ac3",    "Dolby Digital AC3"),
            new Choice("eac3",   "Dolby Digital Plus E-AC3"),
            new Choice("flac",   "FLAC (lossless)"),
            new Choice("pcm16",  "PCM 16-bit"),
            new Choice("pcm24",  "PCM 24-bit"),
            new Choice("pcm32f", "PCM 32-bit float"),
            new Choice("alac",   "ALAC (Apple Lossless)"),
            new Choice("wmav2",  "WMA v2"),
            new Choice("mp2",    "MP2"),
            new Choice("dts",    "DTS"),
            new Choice("copy",   "Stream copy (no re-encoding)"),
            new Choice("none",   "No audio (mute)"),
        };

        static Formats()
        {
            // ---------------- VIDEO ----------------
            V("mp4",  "MP4 — H.264 + AAC (universal)", "h264", "aac", "-movflags +faststart");
            V("mkv",  "MKV — Matroska", "h264", "aac", "");
            V("mov",  "MOV — Apple ProRes 422 Proxy", "prores_proxy", "pcm24", "");
            V("mov",  "MOV — Apple ProRes 422 LT", "prores_lt", "pcm24", "");
            V("mov",  "MOV — Apple ProRes 422", "prores_std", "pcm24", "");
            V("mov",  "MOV — Apple ProRes 422 HQ", "prores", "pcm24", "");
            V("mov",  "MOV — Apple ProRes 4444 (alpha)", "prores_4444", "pcm24", "");
            V("mov",  "MOV — Apple ProRes 4444 XQ (alpha)", "prores_4444xq", "pcm24", "");
            V("mkv",  "MKV — Apple ProRes 422 HQ", "prores", "pcm24", "");
            V("mov",  "MOV — Avid DNxHR LB", "dnxhr_lb", "pcm24", "");
            V("mov",  "MOV — Avid DNxHR SQ", "dnxhr_sq", "pcm24", "");
            V("mov",  "MOV — Avid DNxHR HQ", "dnxhr", "pcm24", "");
            V("mov",  "MOV — Avid DNxHR HQX (10-bit)", "dnxhr_hqx", "pcm24", "");
            V("mov",  "MOV — Avid DNxHR 444 (10-bit)", "dnxhr_444", "pcm24", "");
            V("mxf",  "MXF — Avid DNxHR HQ", "dnxhr", "pcm24", "-ar 48000");
            V("mkv",  "MKV — FFV1 lossless archive", "ffv1", "flac", "");
            V("mov",  "MOV — QuickTime / Apple", "h264", "aac", "-movflags +faststart");
            V("webm", "WEBM — VP9 + Opus (web)", "vp9", "opus", "");
            V("avi",  "AVI — MPEG-4 + MP3", "mpeg4", "mp3", "");
            V("wmv",  "WMV — Windows Media Video", "wmv2", "wmav2", "");
            V("flv",  "FLV — Flash Video", "h264", "aac", "");
            V("m4v",  "M4V — Apple / iTunes", "h264", "aac", "");
            V("ts",   "TS — MPEG Transport Stream", "h264", "aac", "");
            V("m2ts", "M2TS — Blu-ray / AVCHD", "h264", "ac3", "");
            V("mpg",  "MPG — MPEG-2 (DVD / TV)", "mpeg2", "mp2", "");
            V("vob",  "VOB — DVD Video", "mpeg2", "ac3", "-f vob");
            V("3gp",  "3GP — Mobile phone", "h264", "aac", "");
            V("3g2",  "3G2 — Mobile phone (CDMA)", "h264", "aac", "");
            V("ogv",  "OGV — Theora + Vorbis (open)", "theora", "vorbis", "");
            V("asf",  "ASF — Advanced Systems Format", "wmv2", "wmav2", "");
            V("mxf",  "MXF — Broadcast (MPEG-2)", "mpeg2", "pcm16", "-ar 48000");
            V("nut",  "NUT — FFmpeg container", "h264", "flac", "");
            V("gif",  "GIF — Animation", "gif", "none", "-loop 0");
            V("webp", "WEBP — Animation", "webpanim", "none", "-loop 0");
            V("apng", "APNG — Animated PNG", "apng", "none", "-plays 0");

            // ---------------- AUDIO ----------------
            A("mp3",  "MP3 — MPEG Layer 3", "mp3");
            A("m4a",  "M4A — AAC (Apple / iTunes)", "aac");
            A("aac",  "AAC — ADTS stream", "aac");
            A("wav",  "WAV — PCM 16-bit", "pcm16");
            A("wav",  "WAV — PCM 24-bit (Hi-Res)", "pcm24");
            A("flac", "FLAC — Lossless", "flac");
            A("m4a",  "M4A — ALAC Apple Lossless", "alac");
            A("ogg",  "OGG — Vorbis", "vorbis");
            A("opus", "OPUS — Opus", "opus");
            A("wma",  "WMA — Windows Media Audio", "wmav2");
            A("aiff", "AIFF — Apple PCM", "pcm16be");
            A("ac3",  "AC3 — Dolby Digital", "ac3");
            A("eac3", "EAC3 — Dolby Digital Plus", "eac3");
            A("dts",  "DTS — Digital Theater Systems", "dts");
            A("mp2",  "MP2 — MPEG Layer 2", "mp2");
            A("amr",  "AMR — Mobile voice", "amr");
            A("mka",  "MKA — Matroska Audio (Opus)", "opus");
            A("wv",   "WV — WavPack (lossless)", "wavpack");
            A("tta",  "TTA — True Audio (lossless)", "tta");
            A("caf",  "CAF — Core Audio (ALAC)", "alac");
            A("au",   "AU — Sun / NeXT", "pcm16be");
            A("w64",  "W64 — Sony Wave64", "pcm24");
            A("oga",  "OGA — Ogg FLAC", "flac");

            // ---------------- IMAGE ----------------
            I("png",  "PNG — Lossless + transparence", "png", true);
            I("jpg",  "JPG — Compressed photo", "mjpeg", true);
            I("webp", "WEBP — Modern web", "webp", false);
            I("webp", "WEBP — Lossless", "webpll", false);
            I("avif", "AVIF — Next-gen (AV1)", "avif", false);
            I("jxl",  "JXL — JPEG XL", "jxl", true);
            I("bmp",  "BMP — Bitmap Windows", "bmp", true);
            I("tiff", "TIFF — Print / pro", "tiff", true);
            I("gif",  "GIF — Still image", "gif", false);
            I("ico",  "ICO — Windows icon (≤ 256 px)", "ico", false);
            I("tga",  "TGA — Targa", "targa", true);
            I("jp2",  "JP2 — JPEG 2000", "jpeg2000", true);
            I("exr",  "EXR — OpenEXR (HDR)", "exr", true);
            I("hdr",  "HDR — Radiance", "hdr", true);
            I("dpx",  "DPX — Digital cinema", "dpx", true);
            I("qoi",  "QOI — Quite OK Image", "qoi", true);
            I("ppm",  "PPM — Portable Pixmap", "ppm", true);
            I("pgm",  "PGM — Grayscale", "pgm", true);
            I("pbm",  "PBM — Black & white", "pbm", true);
            I("pam",  "PAM — Portable Arbitrary Map", "pam", true);
            I("pfm",  "PFM — Portable Float Map", "pfm", true);
            I("sgi",  "SGI — Silicon Graphics", "sgi", true);
            I("pcx",  "PCX — PC Paintbrush", "pcx", true);
            I("ras",  "RAS — Sun Raster", "sunrast", true);
            I("xbm",  "XBM — X11 Bitmap", "xbm", true);
            I("xwd",  "XWD — X Window Dump", "xwd", true);
        }

        static void V(string ext, string label, string v, string a, string extra) { All.Add(new OutFormat(MediaKind.Video, ext, label, v, a, extra, false)); }
        static void A(string ext, string label, string a) { All.Add(new OutFormat(MediaKind.Audio, ext, label, null, a, "", false)); }
        static void I(string ext, string label, string v, bool image2) { All.Add(new OutFormat(MediaKind.Image, ext, label, v, "none", "", image2)); }

        public static List<OutFormat> Of(MediaKind k) { return All.Where(f => f.Kind == k && FormatCatalog.HasEncoder(RequiredEncoder(f))).ToList(); }

        /// Main library encoder needed by a tuned preset (hidden if the FFmpeg build lacks it).
        static string RequiredEncoder(OutFormat f)
        {
            string k = f.Kind == MediaKind.Audio ? f.ACodec : f.VCodec;
            switch (k)
            {
                case "amr": return "libopencore_amrnb";
                case "mp3": return "libmp3lame";
                case "opus": return "libopus";
                case "vorbis": return "libvorbis";
                case "avif": return "libaom-av1";
                case "jxl": return "libjxl";
                case "webp": case "webpll": return "libwebp";
                case "webpanim": return "libwebp_anim";
                case "vp9": return "libvpx-vp9";
                case "theora": return "libtheora";
                case "h264": return "libx264";
                case "prores": case "prores_proxy": case "prores_lt": case "prores_std": case "prores_4444": case "prores_4444xq": return "prores_ks";
                default: return null;
            }
        }
        public static List<OutFormat> DynamicOf(MediaKind k) { return FormatCatalog.Dynamic.Where(f => f.Kind == k).ToList(); }

        public static string Inv(double d) { return d.ToString("0.###", CultureInfo.InvariantCulture); }

        public static string KindName(MediaKind k)
        {
            switch (k)
            {
                case MediaKind.Video: return "Video";
                case MediaKind.Audio: return "Audio";
                case MediaKind.Image: return "Image";
                default: return "?";
            }
        }
    }
}
