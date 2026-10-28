using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FFmpegForge
{
    /// Impulse responses generated at run time for the immersive "Spatial Music" mix:
    ///   * binaural room responses (BRIR) of a 7.1.4 speaker room, for headphone listening
    ///     (measured HRIRs: IRCAM LISTEN "Composite" set as distributed with WebKit, + synthetic room);
    ///   * decorrelated reverb tails, one per speaker, for the surround / height envelopment.
    /// Files are cached in %TEMP%\FFmpegForge.
    public static class Spatial
    {
        public const int Rate = 48000;

        // ---------------- HRIR database (24 azimuths × 4 elevations, 256 taps @ 44.1 kHz) ----------------
        static short[][] hrir;      // [az*4+el] interleaved L,R
        static int[] elevs;
        static int nTaps;

        static void Load()
        {
            if (hrir != null) return;
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("hrir_ircam.bin"))
            using (var br = new BinaryReader(st))
            {
                if (new string(br.ReadChars(4)) != "IRCH") throw new InvalidDataException("HRIR data");
                int naz = br.ReadUInt16(), nel = br.ReadUInt16(); nTaps = br.ReadUInt16();
                elevs = new int[nel];
                for (int i = 0; i < nel; i++) elevs[i] = br.ReadInt16();
                var h = new short[naz * nel][];
                for (int i = 0; i < h.Length; i++)
                {
                    h[i] = new short[nTaps * 2];
                    for (int k = 0; k < nTaps * 2; k++) h[i][k] = br.ReadInt16();
                }
                hrir = h;
            }
        }

        /// HRIR for a direction (azimuth counter-clockwise, 90 = left), resampled to 48 kHz. [0]=left ear, [1]=right ear.
        static double[][] Hrir(double az, double el)
        {
            Load();
            az = ((az % 360) + 360) % 360;
            int ai = (int)Math.Round(az / 15.0) % 24;
            int ei = 0; double best = 1e9;
            for (int i = 0; i < elevs.Length; i++) { double d = Math.Abs(elevs[i] - el); if (d < best) { best = d; ei = i; } }
            var raw = hrir[ai * elevs.Length + ei];
            var l = new double[nTaps]; var r = new double[nTaps];
            for (int k = 0; k < nTaps; k++) { l[k] = raw[2 * k] / 32768.0; r[k] = raw[2 * k + 1] / 32768.0; }
            return new[] { Resample(l, 44100, Rate), Resample(r, 44100, Rate) };
        }

        static double Sinc(double x) { if (Math.Abs(x) < 1e-9) return 1; x *= Math.PI; return Math.Sin(x) / x; }

        static double[] Resample(double[] x, int from, int to)
        {
            int n = (int)Math.Ceiling(x.Length * (double)to / from);
            var y = new double[n];
            const int A = 16;
            double ratio = (double)from / to;
            for (int i = 0; i < n; i++)
            {
                double pos = i * ratio; int c = (int)Math.Floor(pos);
                double acc = 0;
                for (int k = c - A + 1; k <= c + A; k++)
                {
                    if (k < 0 || k >= x.Length) continue;
                    double t = pos - k;
                    acc += x[k] * Sinc(t) * Sinc(t / A);
                }
                y[i] = acc;
            }
            return y;
        }

        // ---------------- speaker layouts ----------------
        public class Speaker { public string Name; public double Az, El; public Speaker(string n, double a, double e) { Name = n; Az = a; El = e; } }

        /// 7.1.4 room as used by Dolby Atmos music mixes (ITU-R BS.2051 / Dolby home placement).
        public static readonly Speaker[] Room714 = {
            new Speaker("FL", 30, 0), new Speaker("FR", -30, 0), new Speaker("FC", 0, 0), new Speaker("LFE", 0, 0),
            new Speaker("BL", 150, 0), new Speaker("BR", -150, 0), new Speaker("SL", 105, 0), new Speaker("SR", -105, 0),
            new Speaker("TFL", 45, 45), new Speaker("TFR", -45, 45), new Speaker("TBL", 135, 45), new Speaker("TBR", -135, 45)
        };

        // ---------------- random ----------------
        class Rng
        {
            uint s;
            public Rng(int seed) { s = (uint)(seed * 2654435761u) ^ 0x9E3779B9u; if (s == 0) s = 1; }
            public double Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s / 4294967296.0 * 2 - 1; }
            public double Gauss() { double a = 0; for (int i = 0; i < 4; i++) a += Next(); return a * 0.866; }
        }

        /// Exponentially decaying, high-frequency-damped noise (one decorrelated reverb tail).
        static double[] Tail(int len, double rt60, double onset, int seed, double damp0, double damp1)
        {
            var r = new Rng(seed);
            var y = new double[len];
            double lp = 0;
            for (int i = 0; i < len; i++)
            {
                double t = (double)i / Rate;
                double env = Math.Exp(-6.907755 * t / rt60);
                double ramp = onset <= 0 ? 1 : Math.Min(1, t / onset);
                double a = damp0 + (damp1 - damp0) * Math.Min(1, t / Math.Max(0.05, rt60));   // more damping later
                lp = lp + (1 - a) * (r.Gauss() - lp);
                y[i] = lp * env * ramp;
            }
            return y;
        }

        static double[] HighPass(double[] x, double fc)
        {
            var y = new double[x.Length];
            double hp = 0, prev = 0, k = Math.Exp(-2 * Math.PI * fc / Rate);
            for (int i = 0; i < x.Length; i++) { hp = k * (hp + x[i] - prev); prev = x[i]; y[i] = hp; }
            return y;
        }

        static double Energy(double[] x) { double e = 0; foreach (var v in x) e += v * v; return e; }

        // ---------------- binaural room (BRIR) ----------------
        /// Writes the BRIR set for a speaker layout (headphone filter, hrir=multich: L,R per speaker).
        public static string BinauralFile(Speaker[] spk)
        {
            const double R = 2.0, C = 343.0;
            int len = (int)(0.42 * Rate);
            var chans = new List<double[]>();
            // room: x forward, y left, z up; listener at the origin
            double xF = 4.0, xB = -3.0, yL = 3.2, yR = -3.2, zC = 1.6, zF = -1.2;
            int idx = 0;
            foreach (var s in spk)
            {
                var L = new double[len]; var Rt = new double[len];
                if (s.Name == "LFE")
                {
                    chans.Add(L); chans.Add(Rt); idx++; continue;   // LFE is mixed directly by the filter
                }
                double az = s.Az * Math.PI / 180, el = s.El * Math.PI / 180;
                double px = R * Math.Cos(el) * Math.Cos(az), py = R * Math.Cos(el) * Math.Sin(az), pz = R * Math.Sin(el);

                // direct sound
                var d = Hrir(s.Az, s.El);
                Add(L, d[0], 0, 1); Add(Rt, d[1], 0, 1);
                double eDirect = Energy(d[0]) + Energy(d[1]);

                // first-order early reflections (image sources)
                var imgs = new[] {
                    new[] { 2 * xF - px, py, pz, 0.38 }, new[] { 2 * xB - px, py, pz, 0.38 },
                    new[] { px, 2 * yL - py, pz, 0.42 },  new[] { px, 2 * yR - py, pz, 0.42 },
                    new[] { px, py, 2 * zC - pz, 0.32 }, new[] { px, py, 2 * zF - pz, 0.28 } };
                foreach (var im in imgs)
                {
                    double dist = Math.Sqrt(im[0] * im[0] + im[1] * im[1] + im[2] * im[2]);
                    int delay = (int)Math.Round((dist - R) / C * Rate);
                    double g = im[3] * R / dist;
                    double iaz = Math.Atan2(im[1], im[0]) * 180 / Math.PI, iel = Math.Asin(im[2] / dist) * 180 / Math.PI;
                    var h = Hrir(iaz, iel);
                    Add(L, Soften(h[0]), delay, g); Add(Rt, Soften(h[1]), delay, g);
                }

                // diffuse tail (small, well damped room): externalization without audible reverb
                // decorrelated between the ears above 300 Hz only: the bass stays centred and stable
                var tl = HighPass(HighPass(Tail(len, 0.30, 0.012, 1000 + idx * 2, 0.35, 0.8), 300), 300);
                var tr = HighPass(HighPass(Tail(len, 0.30, 0.012, 1001 + idx * 2, 0.35, 0.8), 300), 300);
                int pre = (int)(0.014 * Rate);
                double eTail = Energy(tl) + Energy(tr);
                double gt = Math.Sqrt(eDirect * Math.Pow(10, -12.0 / 10) / Math.Max(1e-12, eTail));   // direct-to-reverb ≈ +12 dB
                Add(L, tl, pre, gt); Add(Rt, tr, pre, gt);
                chans.Add(L); chans.Add(Rt);
                idx++;
            }
            // level: the frontal speaker's direct sound has unit energy per ear
            var front = Hrir(0, 0);
            double k = 1.0 / Math.Sqrt((Energy(front[0]) + Energy(front[1])) / 2);
            for (int c = 0; c < chans.Count; c++) for (int i = 0; i < len; i++) chans[c][i] *= k;
            return Save("brir_" + string.Join("_", spk.Select(x => x.Name + (int)x.Az + "." + (int)x.El).ToArray()), chans);
        }

        static double[] Soften(double[] h)
        {
            var y = new double[h.Length];
            for (int i = 0; i < h.Length; i++)
                y[i] = 0.25 * (i > 0 ? h[i - 1] : 0) + 0.5 * h[i] + 0.25 * (i + 1 < h.Length ? h[i + 1] : 0);
            return y;
        }

        static void Add(double[] dst, double[] src, int off, double g)
        {
            for (int i = 0; i < src.Length && i + off < dst.Length; i++) if (i + off >= 0) dst[i + off] += src[i] * g;
        }

        // ---------------- envelopment reverb ----------------
        /// IR set for one FFmpeg afir pass: the first dryCh channels are a unit impulse (dry signal passes
        /// untouched), the next ones are decorrelated reverb tails of unit energy × sendGain (one per send).
        /// Used with afir's IR normalization turned off, so the levels are exact.
        public static string SendIr(int dryCh, double[] sendGain, double rt60, int seed, int mask, double dryHighPass = 0)
        {
            int len = (int)(Math.Min(3.0, rt60 * 1.15) * Rate);
            var chans = new List<double[]>();
            for (int c = 0; c < dryCh; c++)
            {
                var d = new double[len]; d[0] = 1;
                if (dryHighPass > 0) d = HighPass(HighPass(d, dryHighPass), dryHighPass);    // same 2nd-order low cut as FFmpeg's highpass
                chans.Add(d);
            }
            for (int c = 0; c < sendGain.Length; c++)
            {
                var t = Tail(len, rt60, 0.02, seed + c * 7, 0.15, 0.75);
                // low cut (2nd order, 200 Hz): no muddy bass in the reverb field
                t = HighPass(HighPass(t, 200), 200);
                double g = sendGain[c] / Math.Sqrt(Math.Max(1e-12, Energy(t)));
                for (int i = 0; i < len; i++) t[i] *= g;
                chans.Add(t);
            }
            return Save("rev_" + dryCh + "_" + (int)dryHighPass + "_" + string.Join("-", sendGain.Select(x => ((int)(x * 1000)).ToString()).ToArray()) + "_" + (int)(rt60 * 1000) + "_" + seed, chans, mask);
        }

        // ---------------- WAV writer (32-bit float) ----------------
        static string Save(string name, List<double[]> chans, int mask = 0)
        {
            string dir = Path.Combine(Path.GetTempPath(), "FFmpegForge");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + "_v5.wav");
            if (File.Exists(path)) return path;
            int n = chans.Count, len = chans[0].Length;
            string tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                int data = len * n * 4;
                bool ext = mask != 0;   // WAVE_FORMAT_EXTENSIBLE + the exact channel mask of the stream it is convolved with (no remix by FFmpeg)
                bw.Write(Encoding.ASCII.GetBytes("RIFF")); bw.Write((ext ? 60 : 36) + data);
                bw.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); bw.Write(ext ? 40 : 16);
                bw.Write((short)(ext ? -2 : 3)); bw.Write((short)n); bw.Write(Rate); bw.Write(Rate * 4 * n); bw.Write((short)(4 * n)); bw.Write((short)32);
                if (ext)
                {
                    bw.Write((short)22); bw.Write((short)32); bw.Write(mask);
                    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT {00000003-0000-0010-8000-00aa00389b71}
                    bw.Write(new byte[] { 3, 0, 0, 0, 0, 0, 0x10, 0, 0x80, 0, 0, 0xaa, 0, 0x38, 0x9b, 0x71 });
                }
                bw.Write(Encoding.ASCII.GetBytes("data")); bw.Write(data);
                for (int i = 0; i < len; i++) for (int c = 0; c < n; c++) bw.Write((float)chans[c][i]);
            }
            if (File.Exists(path)) File.Delete(tmp); else File.Move(tmp, path);
            return path;
        }
    }
}
