using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FFmpegForge
{
    /// Settings of the SeedVR2 section.
    public class SeedSettings
    {
        public bool Enabled;
        public string Model = "seedvr2_ema_7b_fp8_e4m3fn_mixed_block35_fp16.safetensors";
        public int SizeIdx = 1;               // see SeedEngine.Sizes
        public int BatchSize = 13;            // 4n+1
        public string Color = "lab";
        public double InputNoise, LatentNoise;
        public int Seed = 42;
        public int ChunkSize = 165, Overlap = 3;
        public int BlocksToSwap;
        public bool VaeTiled, Offload, Compile;
        public string Attention = "sdpa";
        public string Gpu = "0";
    }

    /// SeedVR2 (ByteDance-Seed, Apache-2.0) one-step diffusion video / image restoration and upscaling,
    /// run through the official ComfyUI-SeedVR2_VideoUpscaler command line (inference_cli.py, Apache-2.0).
    ///   ffmpeg (trim, denoiser, filters -> lossless) -> SeedVR2 -> ffmpeg (final format, codec, audio)
    /// Nothing is shipped: the "Install SeedVR2" button downloads the official repository, a Python
    /// environment (uv) and PyTorch CUDA; the models download on first use from Hugging Face.
    public static class SeedEngine
    {
        public class Model
        {
            public string File, Label;
            public Model(string f, string l) { File = f; Label = l; }
            public override string ToString() { return Label; }
        }

        public static readonly List<Model> Models = new List<Model> {
            new Model("seedvr2_ema_3b-Q4_K_M.gguf",                                 "3B · GGUF Q4_K_M — 1.9 GB, ~8 GB VRAM (fastest)"),
            new Model("seedvr2_ema_3b-Q8_0.gguf",                                   "3B · GGUF Q8_0 — ~12 GB VRAM"),
            new Model("seedvr2_ema_3b_fp8_e4m3fn.safetensors",                      "3B · FP8 — 3.4 GB, ~16 GB VRAM (SeedVR2 default)"),
            new Model("seedvr2_ema_3b_fp16.safetensors",                            "3B · FP16 — full precision, ~18 GB VRAM"),
            new Model("seedvr2_ema_7b-Q4_K_M.gguf",                                 "7B · GGUF Q4_K_M — 4.6 GB, ~12 GB VRAM"),
            new Model("seedvr2_ema_7b_fp8_e4m3fn_mixed_block35_fp16.safetensors",   "7B · FP8 mixed — ~24 GB VRAM (recommended)"),
            new Model("seedvr2_ema_7b_fp16.safetensors",                            "7B · FP16 — best quality, 24 GB+ VRAM"),
            new Model("seedvr2_ema_7b_sharp-Q4_K_M.gguf",                           "7B Sharp · GGUF Q4_K_M — more detail, ~12 GB VRAM"),
            new Model("seedvr2_ema_7b_sharp_fp8_e4m3fn_mixed_block35_fp16.safetensors", "7B Sharp · FP8 mixed — more detail, ~24 GB VRAM"),
            new Model("seedvr2_ema_7b_sharp_fp16.safetensors",                      "7B Sharp · FP16 — sharpest, 24 GB+ VRAM"),
        };
        public const string DefaultModel = "seedvr2_ema_7b_fp8_e4m3fn_mixed_block35_fp16.safetensors";

        /// Output size: factor of the short side, or a fixed short side.
        public static readonly string[] Sizes = { "×1.5", "×2", "×3", "×4", "720p (short side 720)", "1080p (short side 1080)", "1440p (short side 1440)", "4K (short side 2160)", "Restore only (same size)" };
        static readonly double[] SizeFactor = { 1.5, 2, 3, 4, 0, 0, 0, 0, 1 };
        static readonly int[] SizeShort = { 0, 0, 0, 0, 720, 1080, 1440, 2160, 0 };

        public static readonly int[] Batches = { 1, 5, 9, 13, 17, 21, 25, 33, 45, 61, 81 };
        public static readonly string[] Colors = { "lab", "wavelet", "wavelet_adaptive", "hsv", "adain", "none" };
        public static readonly string[] ColorLabels = { "LAB — perceptual colour match (recommended)", "Wavelet", "Wavelet adaptive", "HSV", "AdaIN", "None" };
        public static readonly string[] Attentions = { "sdpa", "flash_attn_2", "sageattn_2", "sageattn_3" };
        public static readonly string[] AttentionLabels = { "PyTorch SDPA (always works)", "Flash Attention 2 (RTX 30+)", "SageAttention 2 (RTX 30/40)", "SageAttention 3 (RTX 50)" };

        public const string RepoZip = "https://github.com/numz/ComfyUI-SeedVR2_VideoUpscaler/archive/refs/heads/main.zip";
        public const string UvZip = "https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip";
        public const string TorchIndex = "https://download.pytorch.org/whl/cu128";

        public static string Dir = "";

        public static string DefaultInstallDir
        {
            get
            {
                if (AppPaths.Portable) return Path.Combine(AppPaths.AppDir, "SeedVR2");
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFmpegForge", "SeedVR2");
            }
        }

        /// Folder that contains inference_cli.py (the folder, its "app" subfolder, or one level below).
        public static string ScriptDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return null;
            try
            {
                if (File.Exists(Path.Combine(dir, "inference_cli.py"))) return dir;
                if (File.Exists(Path.Combine(dir, "app", "inference_cli.py"))) return Path.Combine(dir, "app");
                if (Directory.Exists(dir))
                    foreach (var sub in Directory.GetDirectories(dir))
                        if (File.Exists(Path.Combine(sub, "inference_cli.py"))) return sub;
            }
            catch { }
            return null;
        }

        /// The Python of the SeedVR2 environment (FFmpeg Forge install, a venv, or ComfyUI portable).
        public static string Python(string script)
        {
            if (script == null) return null;
            var c = new List<string>();
            string up1 = Parent(script), up2 = Parent(up1), up3 = Parent(up2);
            foreach (var d in new[] { script, up1 })
                if (d != null) { c.Add(Path.Combine(d, "venv", "Scripts", "python.exe")); c.Add(Path.Combine(d, ".venv", "Scripts", "python.exe")); }
            if (up3 != null) { c.Add(Path.Combine(up3, "python_embeded", "python.exe")); c.Add(Path.Combine(up2, ".venv", "Scripts", "python.exe")); c.Add(Path.Combine(up2, "venv", "Scripts", "python.exe")); }
            foreach (var p in c) try { if (File.Exists(p)) return p; } catch { }
            // non-Windows test environments
            foreach (var d in new[] { script, up1 })
                if (d != null) { string p = Path.Combine(d, "venv", "bin", "python"); if (File.Exists(p)) return p; }
            return null;
        }

        static string Parent(string d) { try { return d == null ? null : Path.GetDirectoryName(d.TrimEnd('\\', '/')); } catch { return null; } }

        /// Where the models are (and are downloaded to).
        public static string ModelDir(string script)
        {
            if (script == null) return null;
            string up1 = Parent(script), up2 = Parent(up1);
            // ComfyUI: ComfyUI\custom_nodes\<node>  ->  ComfyUI\models\SEEDVR2
            if (up1 != null && Path.GetFileName(up1).Equals("custom_nodes", StringComparison.OrdinalIgnoreCase) && up2 != null)
                return Path.Combine(up2, "models", "SEEDVR2");
            // FFmpeg Forge install:  <root>\app  ->  <root>\models
            if (Path.GetFileName(script).Equals("app", StringComparison.OrdinalIgnoreCase) && up1 != null)
                return Path.Combine(up1, "models");
            return Path.Combine(script, "models", "SEEDVR2");
        }

        public static string Script { get { return ScriptDir(Dir); } }
        public static bool Ready { get { string s = Script; return s != null && Python(s) != null; } }

        /// Models already downloaded (registry names) + extra model files found in the model folder.
        public static List<string> Downloaded()
        {
            var r = new List<string>();
            try
            {
                string md = ModelDir(Script);
                if (md != null && Directory.Exists(md))
                    r.AddRange(Directory.GetFiles(md).Select(Path.GetFileName).Where(f => (f.EndsWith(".safetensors") || f.EndsWith(".gguf")) && !f.StartsWith("ema_vae")));
            }
            catch { }
            return r;
        }

        public static string Status()
        {
            string s = Script;
            if (s == null)
                return string.IsNullOrEmpty(Dir) ? "SeedVR2 is not installed: click \"Install SeedVR2\" (≈ 4 GB download) or choose an existing folder."
                                                 : "inference_cli.py not found in " + Dir;
            string py = Python(s);
            if (py == null) return "SeedVR2 found (" + s + ") but no Python environment next to it: click \"Install SeedVR2\" to create it.";
            var dl = Downloaded();
            return "Ready: " + s + "\nModels downloaded: " + (dl.Count == 0 ? "none yet (the chosen model downloads on the first conversion)" : string.Join(", ", dl.Select(Short).ToArray()));
        }

        public static string Short(string file)
        {
            var m = Models.FirstOrDefault(x => x.File == file);
            return m != null ? m.Label.Split('—')[0].Trim() : file;
        }

        // ---------------- installer ----------------
        /// Writes the installation script (official repository + uv Python 3.12 + PyTorch CUDA 12.8 + requirements).
        public static string WriteInstaller(string root)
        {
            Directory.CreateDirectory(root);
            string bat = Path.Combine(root, "install_seedvr2.bat");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("title FFmpeg Forge - SeedVR2 installation");
            sb.AppendLine("setlocal");
            sb.AppendLine("cd /d \"%~dp0\"");
            sb.AppendLine("echo ============================================================");
            sb.AppendLine("echo  SeedVR2 for FFmpeg Forge");
            sb.AppendLine("echo  Folder: %CD%");
            sb.AppendLine("echo  Downloads: SeedVR2 CLI (GitHub), uv + Python 3.12, PyTorch CUDA 12.8 (about 4 GB).");
            sb.AppendLine("echo  The models (2 to 16 GB each) download on the first conversion.");
            sb.AppendLine("echo ============================================================");
            sb.AppendLine("echo.");
            sb.AppendLine("if not exist uv\\uv.exe (");
            sb.AppendLine("  echo [1/5] Downloading uv...");
            sb.AppendLine("  powershell -NoProfile -ExecutionPolicy Bypass -Command \"$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing '" + UvZip + "' -OutFile uv.zip; Expand-Archive -Force uv.zip uv; Remove-Item uv.zip\" || goto :fail");
            sb.AppendLine(")");
            sb.AppendLine("echo [2/5] Downloading SeedVR2 (numz/ComfyUI-SeedVR2_VideoUpscaler)...");
            sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command \"$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing '" + RepoZip + "' -OutFile repo.zip; if (Test-Path repo_tmp) { Remove-Item -Recurse -Force repo_tmp }; Expand-Archive -Force repo.zip repo_tmp; Remove-Item repo.zip; if (Test-Path app) { Remove-Item -Recurse -Force app }; Move-Item (Get-ChildItem repo_tmp)[0].FullName app; Remove-Item -Recurse -Force repo_tmp\" || goto :fail");
            sb.AppendLine("echo [3/5] Python 3.12 environment...");
            sb.AppendLine("set UV_PYTHON_INSTALL_DIR=%CD%\\python");
            sb.AppendLine("set UV_CACHE_DIR=%CD%\\cache");
            sb.AppendLine("uv\\uv.exe venv --python 3.12 --seed venv || goto :fail");
            sb.AppendLine("echo [4/5] PyTorch with CUDA 12.8 (large download)...");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe torch torchvision --index-url " + TorchIndex + " || goto :fail");
            sb.AppendLine("echo [5/5] SeedVR2 requirements...");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe -r app\\requirements.txt || goto :fail");
            sb.AppendLine("if not exist models mkdir models");
            sb.AppendLine("venv\\Scripts\\python.exe -c \"import torch;print('PyTorch',torch.__version__,'CUDA',torch.cuda.is_available(), torch.cuda.get_device_name(0) if torch.cuda.is_available() else '')\"");
            sb.AppendLine("echo.");
            sb.AppendLine("echo SeedVR2 is installed. You can close this window and go back to FFmpeg Forge.");
            sb.AppendLine("echo SEEDVR2_INSTALL_OK> installed.txt");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 0");
            sb.AppendLine(":fail");
            sb.AppendLine("echo.");
            sb.AppendLine("echo Installation failed. Check your internet connection and run install_seedvr2.bat again.");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 1");
            File.WriteAllText(bat, sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
            return bat;
        }

        // ---------------- plan ----------------
        public class Plan
        {
            public string TempDir, PreArgs, Python, Script, SeedArgs, EncArgs, SeedOut, Output;
            public double Expected;
            public int InW, InH, OutShort;
            public string Text()
            {
                return "ffmpeg " + PreArgs + "  &&  \"" + Python + "\" " + SeedArgs + "  &&  ffmpeg " + EncArgs;
            }
        }

        static string Q(string x) { return "\"" + x.Replace("\"", "\\\"") + "\""; }
        static string F(double d) { return d.ToString("0.###", CultureInfo.InvariantCulture); }

        public static Plan Build(Job job, ConvSettings s, string outPath, out string error)
        {
            error = null;
            var v = s.Seed;
            var fmt = s.Format;
            string script = Script;
            if (script == null) { error = "SeedVR2 is not installed: in the SeedVR2 section click \"Install SeedVR2\" (or choose the folder that contains inference_cli.py)."; return null; }
            string py = Python(script);
            if (py == null) { error = "No Python environment found for SeedVR2 (" + script + "). Click \"Install SeedVR2\" to create it."; return null; }
            if (job.Kind == MediaKind.Audio || fmt.Kind == MediaKind.Audio) { error = "SeedVR2 needs a video or a picture."; return null; }
            bool image = fmt.Kind == MediaKind.Image;
            bool srcVideo = job.Kind == MediaKind.Video || CommandBuilder.IsAnimatedImageSource(job);
            if (image && srcVideo) { error = "SeedVR2 + Image output works on pictures. For a video, choose the Video category."; return null; }
            if (s.ScaleActive) { error = "SeedVR2 is an upscaler too: untick \"Scale\" / OptiScaler (SeedVR2 sets the size itself)."; return null; }
            if (s.RtxActive) { error = "Untick the NVIDIA RTX AI options (VSR / TrueHDR / DLSS) to use SeedVR2."; return null; }
            if (!string.IsNullOrWhiteSpace(s.FilterComplex)) { error = "The advanced filtergraph cannot be combined with SeedVR2. Use the filter chain instead."; return null; }
            string vkey = fmt.VCodec;
            if (!image)
            {
                if (s.VCodec != "auto") vkey = s.VCodec;
                if (vkey == "copy") { error = "SeedVR2 needs a real video codec (not stream copy)."; return null; }
            }
            int w = job.Width, h = job.Height;
            if (w < 16 || h < 16) { error = "Could not read the picture size (ffprobe). Re-add the file."; return null; }
            if (s.Rotate == 1 || s.Rotate == 3) { int t = w; w = h; h = t; }
            int shortSide = Math.Min(w, h);
            int si = Math.Max(0, Math.Min(Sizes.Length - 1, v.SizeIdx));
            int target = SizeShort[si] > 0 ? SizeShort[si] : (int)Math.Round(shortSide * SizeFactor[si]);
            target += target % 2;
            int longOut = (int)Math.Round((double)Math.Max(w, h) * target / shortSide);
            if (longOut > 8192) { error = "SeedVR2 output would be about " + longOut + " pixels wide: pick a smaller size."; return null; }

            var p = new Plan { Python = py, Script = script, Output = outPath, InW = w, InH = h, OutShort = target };
            p.TempDir = Path.Combine(Path.GetTempPath(), "FFmpegForge-SeedVR2-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string tin = Path.Combine(p.TempDir, image ? "input.png" : "input.mp4");
            p.SeedOut = Path.Combine(p.TempDir, image ? "seedvr2.png" : "seedvr2.mp4");

            // 1) pre-pass: trim, rotation, denoiser, user filters -> lossless file SeedVR2 (OpenCV) can read
            int fn = job.FpsNum > 0 ? job.FpsNum : 30, fd = job.FpsDen > 0 ? job.FpsDen : 1;
            if (s.Fps > 0) { fn = (int)Math.Round(s.Fps * 1000); fd = 1000; }
            string rate = fn + "/" + fd;
            double ss = s.TrimStart > 0 ? s.TrimStart : 0, to = s.TrimEnd > 0 ? s.TrimEnd : -1;
            string trim = image ? "" : (ss > 0 ? "-ss " + Formats.Inv(ss) + " " : "") + (to > ss && to > 0 ? "-to " + Formats.Inv(to) + " " : "");
            double dur = job.Duration;
            if (to > 0 && (dur <= 0 || to < dur)) dur = to;
            dur = Math.Max(0, dur - ss);
            bool still = !image && !srcVideo;      // picture -> video clip
            if (still) { dur = s.StillDuration > 0 ? s.StillDuration : 5; trim = "-loop 1 -t " + Formats.Inv(dur) + " "; }
            p.Expected = image ? 0 : dur;
            var pre = new List<string>();
            CommandBuilder.AddRotate(pre, s.Rotate);
            string dn = Denoise.Filter(s, job, image);
            if (dn != null) pre.Add(dn);
            string userV = s.UserVideoChain();
            if (userV.Length > 0) pre.Add(userV);
            if (image)
            {
                pre.Add("format=rgb24");
                p.PreArgs = "-hide_banner -nostdin -y -loglevel error -i " + Q(job.Path) + " -map 0:v:0 -frames:v 1 -vf " + Q(string.Join(",", pre.ToArray())) + " " + Q(tin);
            }
            else
            {
                pre.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
                pre.Add("format=yuv444p");
                p.PreArgs = "-hide_banner -nostdin -y -loglevel error -progress pipe:1 -nostats " + trim + "-i " + Q(job.Path) + " -map 0:v:0 -an -sn -dn -vf " + Q(string.Join(",", pre.ToArray())) +
                            " -r " + rate + " -c:v libx264 -preset ultrafast -qp 0 -pix_fmt yuv444p " + Q(tin);
            }

            // 2) SeedVR2
            string model = string.IsNullOrEmpty(v.Model) ? DefaultModel : v.Model;
            int batch = image ? 1 : (Batches.Contains(v.BatchSize) ? v.BatchSize : 5);
            var a = new StringBuilder();
            a.Append("-u " + Q(Path.Combine(script, "inference_cli.py")) + " " + Q(tin));
            a.Append(" --output " + Q(p.SeedOut));
            a.Append(" --model_dir " + Q(ModelDir(script)));
            a.Append(" --dit_model " + Q(model));
            a.Append(" --resolution " + target);
            a.Append(" --batch_size " + batch);
            a.Append(" --seed " + v.Seed);
            a.Append(" --color_correction " + (Colors.Contains(v.Color) ? v.Color : "lab"));
            if (v.InputNoise > 0) a.Append(" --input_noise_scale " + F(Math.Min(1, v.InputNoise)));
            if (v.LatentNoise > 0) a.Append(" --latent_noise_scale " + F(Math.Min(1, v.LatentNoise)));
            if (!image)
            {
                a.Append(" --output_format mp4 --video_backend ffmpeg --10bit");
                if (v.ChunkSize > 0) a.Append(" --chunk_size " + v.ChunkSize);
                if (v.Overlap > 0) a.Append(" --temporal_overlap " + v.Overlap);
                if (batch > 1) a.Append(" --uniform_batch_size");
            }
            else a.Append(" --output_format png");
            if (!string.IsNullOrWhiteSpace(v.Gpu)) a.Append(" --cuda_device " + v.Gpu.Trim());
            if (v.BlocksToSwap > 0)
            {
                bool is7 = model.Contains("_7b");
                a.Append(" --blocks_to_swap " + Math.Min(is7 ? 36 : 32, v.BlocksToSwap) + " --dit_offload_device cpu");
            }
            else if (v.Offload) a.Append(" --dit_offload_device cpu");
            if (v.Offload) a.Append(" --vae_offload_device cpu");
            if (v.VaeTiled) a.Append(" --vae_encode_tiled --vae_decode_tiled");
            if (!string.IsNullOrEmpty(v.Attention) && v.Attention != "sdpa") a.Append(" --attention_mode " + v.Attention);
            if (v.Compile) a.Append(" --compile_dit --compile_vae");
            p.SeedArgs = a.ToString();

            // 3) final encode with FFmpeg Forge's format / codec / audio settings
            var vf = new List<string>();
            bool even = !image && CommandBuilder.NeedsEven(vkey);
            string sc = CommandBuilder.ScaleFilter(s, even);
            if (sc != null) vf.Add(sc);
            if (image)
            {
                string ia = CommandBuilder.ImageArgs(vkey ?? "auto", s.ImgQuality) ?? "";
                p.EncArgs = "-hide_banner -nostdin -y -loglevel warning -i " + Q(p.SeedOut) + " " +
                            (vf.Count > 0 ? "-vf " + Q(string.Join(",", vf.ToArray())) + " " : "") + "-frames:v 1 -update 1 " + ia +
                            (string.IsNullOrWhiteSpace(s.CustomArgs) ? "" : " " + s.CustomArgs.Trim()) + " -strict -2 " + Q(outPath);
                return p;
            }
            // SeedVR2's encoder writes untagged BT.601 YUV: convert to tagged BT.709 for HD
            vf.Add("scale=in_color_matrix=bt601:out_color_matrix=" + (target >= 720 ? "bt709" : "smpte170m") + ":out_range=tv");
            if (even) vf.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
            string va = CommandBuilder.VideoArgs(vkey, s, fmt.Ext) + (target >= 720 ? " -color_primaries bt709 -color_trc bt709 -colorspace bt709" : "");
            string akey = s.ACodec == "auto" ? fmt.ACodec : s.ACodec;
            if (s.ChLayout == "copy" && akey != "none") akey = "copy";
            string audio;
            if (akey == "none" || (job.Probed && !job.HasAudio)) audio = "-an";
            else
            {
                string userA = s.UserAudioChain();
                if (akey == "copy" && userA.Length > 0) { error = "Audio filters cannot be used with audio stream copy."; return null; }
                s.SourceChannels = job.AudioChannels; s.SourceIsVideo = true; s.SourceDuration = dur;
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
            p.EncArgs = "-hide_banner -nostdin -y -loglevel warning -progress pipe:1 -nostats " +
                        "-r " + rate + " -i " + Q(p.SeedOut) + " " + (still ? "" : trim) + "-i " + Q(job.Path) + " -map 0:v:0 " + audio +
                        " -vf " + Q(string.Join(",", vf.ToArray())) + " " + va +
                        " -sn -shortest " + (fmt.Extra ?? "") + (fmt.Dynamic && fmt.Muxer != null ? " -f " + fmt.Muxer : "") +
                        (string.IsNullOrWhiteSpace(s.CustomArgs) ? "" : " " + s.CustomArgs.Trim()) + " -strict -2 " + Q(outPath);
            return p;
        }

        // ---------------- run ----------------
        static readonly Regex Phase = new Regex(@"(Encoding|Upscaling|Decoding|Post-processing) batch (\d+)/(\d+)", RegexOptions.Compiled);
        static readonly Regex Chunk = new Regex(@"Chunk (\d+)/(\d+)", RegexOptions.Compiled);
        static readonly Regex Tqdm = new Regex(@"\d+%\s*\|", RegexOptions.Compiled);
        static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

        /// Pre-pass 5 %, SeedVR2 85 %, final encode 10 %.
        public static int Run(Plan plan, Action<double> onProgress, Action<string> onLog, Func<bool> cancelled, out string lastErrors)
        {
            try
            {
                Directory.CreateDirectory(plan.TempDir);
                try { string od = Path.GetDirectoryName(plan.Output); if (!string.IsNullOrEmpty(od)) Directory.CreateDirectory(od); } catch { }

                onLog("SeedVR2 1/3 — preparing the source (FFmpeg)");
                int code = Runner.Run(Runner.FFmpeg, plan.PreArgs, plan.Expected, x => onProgress(0.05 * x), onLog, cancelled, out lastErrors);
                if (code != 0) { Cleanup(plan); return code; }

                onLog("SeedVR2 2/3 — AI restoration " + plan.InW + "×" + plan.InH + " → short side " + plan.OutShort + " (first run: model download)");
                code = RunSeed(plan, x => onProgress(0.05 + 0.85 * x), onLog, cancelled, out lastErrors);
                if (code != 0) { Cleanup(plan); return code; }
                if (!File.Exists(plan.SeedOut)) { lastErrors = "SeedVR2 finished but did not write " + plan.SeedOut; Cleanup(plan); return 3; }

                onLog("SeedVR2 3/3 — encoding the final file (FFmpeg)");
                code = Runner.Run(Runner.FFmpeg, plan.EncArgs, plan.Expected, x => onProgress(0.90 + 0.10 * x), onLog, cancelled, out lastErrors);
                Cleanup(plan);
                if (code == 0) onProgress(1.0);
                return code;
            }
            catch (Exception ex) { lastErrors = ex.Message; Cleanup(plan); return -2; }
        }

        static int RunSeed(Plan plan, Action<double> onProgress, Action<string> onLog, Func<bool> cancelled, out string lastErrors)
        {
            var errs = new List<string>();
            var psi = new ProcessStartInfo(plan.Python, plan.SeedArgs)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = plan.Script,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            // SeedVR2's video writer calls "ffmpeg": use FFmpeg Forge's
            string ffdir = string.IsNullOrEmpty(Runner.FFmpeg) ? null : Path.GetDirectoryName(Runner.FFmpeg);
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!string.IsNullOrEmpty(ffdir)) psi.EnvironmentVariables["PATH"] = ffdir + Path.PathSeparator + path;
            psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";
            psi.EnvironmentVariables["HF_HUB_DISABLE_PROGRESS_BARS"] = "1";
            int chunk = 1, chunks = 1;
            long lastLog = 0, lastBar = -10000;
            var sw = Stopwatch.StartNew();
            Action<string> line = l =>
            {
                if (string.IsNullOrWhiteSpace(l)) return;
                l = Ansi.Replace(l, "").TrimEnd();
                var c = Chunk.Match(l);
                if (c.Success) { int.TryParse(c.Groups[1].Value, out chunk); int.TryParse(c.Groups[2].Value, out chunks); if (chunks < 1) chunks = 1; }
                var m = Phase.Match(l);
                if (m.Success)
                {
                    int i, n; int.TryParse(m.Groups[2].Value, out i); int.TryParse(m.Groups[3].Value, out n);
                    if (n > 0)
                    {
                        double f = (double)i / n, ph;
                        switch (m.Groups[1].Value)
                        {
                            case "Encoding": ph = 0.15 * f; break;
                            case "Upscaling": ph = 0.15 + 0.60 * f; break;
                            case "Decoding": ph = 0.75 + 0.20 * f; break;
                            default: ph = 0.95 + 0.05 * f; break;
                        }
                        onProgress(Math.Min(1.0, (Math.Max(0, chunk - 1) + ph) / chunks));
                    }
                    if (sw.ElapsedMilliseconds - lastLog < 3000) return;     // batch lines: at most one every 3 s in the log
                    lastLog = sw.ElapsedMilliseconds;
                }
                else if (Tqdm.IsMatch(l))
                {
                    // download / tqdm progress bars: one line every 3 s
                    if (sw.ElapsedMilliseconds - lastBar < 3000 && !l.Contains("100%")) return;
                    lastBar = sw.ElapsedMilliseconds;
                }
                lock (errs) { errs.Add(l); if (errs.Count > 40) errs.RemoveAt(0); }
                onLog(l);
            };
            using (var p = Process.Start(psi))
            {
                p.OutputDataReceived += (o, e) => line(e.Data);
                p.ErrorDataReceived += (o, e) => line(e.Data);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                while (!p.WaitForExit(250))
                {
                    if (cancelled()) { KillTree(p); lastErrors = "Cancelled"; return -1; }
                }
                p.WaitForExit();
                lock (errs) lastErrors = string.Join(Environment.NewLine, errs.ToArray());
                return p.ExitCode == -1 ? 255 : p.ExitCode;
            }
        }

        static void KillTree(Process p)
        {
            try
            {
                if (Path.DirectorySeparatorChar == '\\')
                {
                    var k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true });
                    if (k != null) k.WaitForExit(5000);
                }
            }
            catch { }
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.WaitForExit(5000); } catch { }
        }

        static void Cleanup(Plan plan)
        {
            try { if (plan.TempDir != null && Directory.Exists(plan.TempDir)) Directory.Delete(plan.TempDir, true); } catch { }
        }
    }
}
