using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace FFmpegForge
{
    /// Denoiser in the style of Neat Video: the noise of the video is measured first (auto noise
    /// profile, luma and chroma separately), then a temporal + spatial noise reduction is tuned to it,
    /// followed by optional detail recovery and luma sharpening.
    ///   Fast          hqdn3d (spatio-temporal, real time)
    ///   Balanced      fftdnoiz: 3D FFT Wiener filter over the previous / current / next frame
    ///   High quality  fftdnoiz + non-local means (nlmeans) clean-up
    public static class Denoise
    {
        public class Preset
        {
            public string Name; public double K, ChromaExtra, Detail; public int Sharpen;
            public Preset(string n, double k, double c, double d, int sh) { Name = n; K = k; ChromaExtra = c; Detail = d; Sharpen = sh; }
            public override string ToString() { return Name; }
        }

        public static readonly List<Preset> Presets = new List<Preset> {
            new Preset("Auto — measures the noise of each video", 1.0, 0, 0.08, 20),
            new Preset("Light — keeps a natural texture", 0.6, 0, 0.15, 15),
            new Preset("Medium", 1.0, 0, 0.08, 20),
            new Preset("Strong", 1.5, 0, 0.03, 30),
            new Preset("Very strong", 2.2, 0, 0.0, 35),
            new Preset("Film grain — clean but keep the grain", 0.8, 0, 0.30, 10),
            new Preset("Low light / smartphone — heavy colour noise", 1.3, 2.5, 0.05, 25),
            new Preset("Animation / cartoons — flat clean areas", 1.8, 1.5, 0.0, 20),
            new Preset("Old VHS / analog — colour noise & grain", 1.6, 3.0, 0.05, 25),
        };

        public static readonly string[] Engines = {
            "Balanced — 3D FFT temporal (recommended)",
            "High quality — 3D FFT + non-local means (slower)",
            "Fast — hqdn3d (real time)" };

        // ---------------- auto noise profile ----------------
        /// Estimates the noise standard deviation (8-bit units) of luma and chroma from a few frames
        /// (Immerkær's Laplacian estimator, taken on the flattest 10 % of the picture so details are not
        /// mistaken for noise).
        public static void Profile(Job job)
        {
            if (job.NoiseProfiled) return;
            job.NoiseProfiled = true;
            try
            {
                int w = job.Width, h = job.Height;
                if (w <= 0 || h <= 0) return;
                w -= w % 2; h -= h % 2;
                var ys = new List<double>(); var cs = new List<double>();
                double dur = job.Duration;
                double[] at = dur > 2 ? new[] { 0.12, 0.35, 0.6, 0.85 } : new[] { 0.0 };
                foreach (var a in at)
                {
                    string ss = dur > 2 ? "-ss " + (dur * a).ToString("0.###", CultureInfo.InvariantCulture) + " " : "";
                    byte[] fr = Grab("-v error " + ss + "-i \"" + job.Path + "\" -frames:v 1 -vf scale=" + w + ":" + h + ":flags=neighbor -f rawvideo -pix_fmt yuv420p -", w * h * 3 / 2);
                    if (fr == null) continue;
                    ys.Add(Estimate(fr, 0, w, h));
                    double u = Estimate(fr, w * h, w / 2, h / 2), v = Estimate(fr, w * h + (w / 2) * (h / 2), w / 2, h / 2);
                    cs.Add((u + v) / 2);
                }
                if (ys.Count == 0) { job.NoiseY = -1; return; }
                ys.Sort(); cs.Sort();
                job.NoiseY = ys[ys.Count / 2]; job.NoiseC = cs[cs.Count / 2];
            }
            catch { }
        }

        static byte[] Grab(string args, int size)
        {
            var psi = new ProcessStartInfo(Runner.FFmpeg, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                p.ErrorDataReceived += (s, e) => { };
                p.BeginErrorReadLine();
                var buf = new byte[size]; int got = 0;
                var st = p.StandardOutput.BaseStream;
                while (got < size) { int n = st.Read(buf, got, size - got); if (n <= 0) break; got += n; }
                try { st.CopyTo(System.IO.Stream.Null); } catch { }
                p.WaitForExit(30000);
                return got == size ? buf : null;
            }
        }

        static double Estimate(byte[] d, int off, int w, int h)
        {
            const int B = 16;
            int bw = (w - 2) / B, bh = (h - 2) / B;
            if (bw < 2 || bh < 2) return 0;
            var blocks = new List<double>(bw * bh);
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    double sum = 0;
                    for (int y = by * B + 1; y < by * B + 1 + B; y++)
                    {
                        int r0 = off + (y - 1) * w, r1 = off + y * w, r2 = off + (y + 1) * w;
                        for (int x = bx * B + 1; x < bx * B + 1 + B; x++)
                        {
                            int l = d[r0 + x - 1] - 2 * d[r0 + x] + d[r0 + x + 1]
                                  - 2 * d[r1 + x - 1] + 4 * d[r1 + x] - 2 * d[r1 + x + 1]
                                  + d[r2 + x - 1] - 2 * d[r2 + x] + d[r2 + x + 1];
                            sum += Math.Abs(l);
                        }
                    }
                    blocks.Add(sum / (B * B) * Math.Sqrt(Math.PI / 2) / 6.0);
                }
            blocks.Sort();
            return blocks[blocks.Count / 10];
        }

        static string F(double d) { return Formats.Inv(Math.Round(d, 3)); }

        // ---------------- filter chain ----------------
        public static string Filter(ConvSettings s, Job job, bool forImage)
        {
            if (!s.DnEnabled) return null;
            var p = Presets[Math.Max(0, Math.Min(Presets.Count - 1, s.DnPreset))];
            double sy = 4.0, sc = 3.0;                                   // "medium" noise when not measured
            if (s.DnAutoProfile)
            {
                Profile(job);
                if (job.NoiseY > 0) { sy = job.NoiseY; sc = Math.Max(job.NoiseC, job.NoiseY * 0.5); }
                else if (job.NoiseY == 0 && job.Width > 0) { sy = 0.6; sc = 0.6; }   // measured: clean video
            }
            sy = Math.Max(0.6, sy); sc = Math.Max(0.5, sc);
            double k = p.K * Math.Max(0, Math.Min(200, s.DnStrength)) / 100.0;
            if (k <= 0) return null;
            bool temporal = s.DnTemporal && !forImage;
            string t = temporal ? ":prev=1:next=1" : "";
            var chain = new List<string>();
            switch (s.DnEngine)
            {
                case 2: // fast
                    {
                        double ls = sy * k * 0.6, cs = sc * k * 0.7;
                        chain.Add("hqdn3d=" + F(ls) + ":" + F(cs) + ":" + F(temporal ? ls * 2.0 : 0) + ":" + F(temporal ? cs * 2.0 : 0));
                        break;
                    }
                default:
                    chain.Add("fftdnoiz=sigma=" + F(Math.Min(100, sy * k)) + ":block=64:overlap=0.5" + t);
                    if (s.DnEngine == 1) chain.Add("nlmeans=s=" + F(Math.Max(1, Math.Min(30, sy * k * 0.2))) + ":p=7:r=9");
                    break;
            }
            if (p.ChromaExtra > 0)   // extra colour-noise pass (chroma planes only)
                chain.Add(s.DnEngine == 2 ? "hqdn3d=0:" + F(sc * k * p.ChromaExtra) + ":0:" + F(temporal ? sc * k * p.ChromaExtra * 1.5 : 0)
                                          : "fftdnoiz=sigma=" + F(Math.Min(100, sc * k * p.ChromaExtra)) + ":planes=6:block=32" + t);
            string dn = string.Join(",", chain.ToArray());
            // detail recovery: a little of the original picture (texture / grain) mixed back in
            if (p.Detail > 0) dn = "split=2[dno][dni];[dni]" + dn + "[dnf];[dno][dnf]blend=all_mode=normal:all_opacity=" + F(p.Detail);
            int sharp = Math.Max(0, Math.Min(100, s.DnSharpen));
            if (sharp > 0) dn += ",unsharp=5:5:" + F(sharp / 100.0) + ":5:5:0";      // luma only: no colour fringes
            return dn;
        }

        public static string Describe(Job job)
        {
            if (!job.NoiseProfiled || job.NoiseY <= 0) return null;
            return "noise profile: luma σ " + job.NoiseY.ToString("0.0", CultureInfo.InvariantCulture) + ", chroma σ " + job.NoiseC.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
