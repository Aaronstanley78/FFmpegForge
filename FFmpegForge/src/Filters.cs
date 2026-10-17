using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace FFmpegForge
{
    // =====================================================================
    //  Data model
    // =====================================================================
    public class FilterInfo
    {
        public string Name, Desc, In, Out;
        public bool Timeline, Slice, Command;

        public bool IsVideo { get { return In == "V" && Out == "V"; } }
        public bool IsAudio { get { return In == "A" && Out == "A"; } }
        public bool IsSimple { get { return IsVideo || IsAudio; } }

        public string Category
        {
            get
            {
                if (In == "|") return "Source";
                if (Out == "|") return "Sink";
                if (IsVideo) return "Video";
                if (IsAudio) return "Audio";
                return "Multi / conversion";
            }
        }
        public string TypeLabel { get { return (In == "|" ? "src" : In) + " → " + (Out == "|" ? "sink" : Out); } }
    }

    public class FilterOption
    {
        public string Name, Alias, Type, Desc = "", Default, Range;
        public List<string[]> Consts = new List<string[]>(); // { name, description }
    }

    public class FilterHelp
    {
        public string Name, Desc = "";
        public List<string> Inputs = new List<string>();   // "main (video)"
        public List<string> Outputs = new List<string>();
        public bool DynInputs, DynOutputs, NoInputs, NoOutputs, Timeline;
        public List<FilterOption> Options = new List<FilterOption>();
    }

    public class FilterEntry
    {
        public char Kind;          // 'V' video chain, 'A' audio chain
        public string Expr;        // e.g. eq=contrast=1.2:saturation=1.3
        public bool Enabled = true;
        public Dictionary<string, string> Values; // raw editor values (for re-editing)
        public string Name { get { int i = Expr.IndexOf('='); return (i < 0 ? Expr : Expr.Substring(0, i)).Trim(); } }
        public override string ToString() { return (Kind == 'V' ? "[VIDEO]  " : "[AUDIO]  ") + Expr; }

        public string Serialize() { return Kind + "\u001E" + (Enabled ? "1" : "0") + "\u001E" + Expr; }
        public static FilterEntry Parse(string s)
        {
            var p = s.Split('\u001E');
            if (p.Length < 3 || p[2].Trim().Length == 0) return null;
            return new FilterEntry { Kind = p[0] == "A" ? 'A' : 'V', Enabled = p[1] == "1", Expr = p[2] };
        }
    }

    // =====================================================================
    //  Catalog: reads every filter from the installed ffmpeg.exe
    // =====================================================================
    public static class FilterCatalog
    {
        public static List<FilterInfo> All = new List<FilterInfo>();
        public static bool Loaded;
        public static string LoadError;
        static readonly Dictionary<string, FilterHelp> cache = new Dictionary<string, FilterHelp>();
        static readonly object sync = new object();

        static readonly Regex ListRx = new Regex(@"^\s*([T.])([S.])([C.])\s+(\S+)\s+(\S+)->(\S+)\s+(.*)$");
        static readonly Regex OptRx = new Regex(@"^\s{1,3}(\S+)\s+<([^>]+)>\s+([A-Z.]{9,12})\s?(.*)$");
        static readonly Regex ConstRx = new Regex(@"^\s{4,}(\S+)(?:\s+(\S+))?\s+([A-Z.]{9,12})\s?(.*)$");
        static readonly Regex IoRx = new Regex(@"^\s*#\d+:\s*(.+)$");
        static readonly Regex RangeRx = new Regex(@"\(from (.+?) to (.+?)\)");
        static readonly Regex DefRx = new Regex(@"\(default (.*)\)\s*$");

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

        public static void Load()
        {
            try
            {
                var list = new List<FilterInfo>();
                foreach (var raw in Run("-hide_banner -filters").Split('\n'))
                {
                    var m = ListRx.Match(raw.TrimEnd('\r'));
                    if (!m.Success) continue;
                    list.Add(new FilterInfo
                    {
                        Timeline = m.Groups[1].Value == "T", Slice = m.Groups[2].Value == "S", Command = m.Groups[3].Value == "C",
                        Name = m.Groups[4].Value, In = m.Groups[5].Value, Out = m.Groups[6].Value, Desc = m.Groups[7].Value.Trim()
                    });
                }
                All = list.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
                Loaded = true;
                LoadError = All.Count == 0 ? "No filter found in ffmpeg -filters output." : null;
            }
            catch (Exception ex) { LoadError = ex.Message; Loaded = true; }
        }

        public static FilterInfo Find(string name)
        {
            return All.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static FilterHelp GetHelp(string name)
        {
            lock (sync) { FilterHelp h; if (cache.TryGetValue(name, out h)) return h; }
            var help = Parse(name, Run("-hide_banner -h filter=" + name));
            lock (sync) cache[name] = help;
            return help;
        }

        public static FilterHelp Parse(string name, string text)
        {
            var h = new FilterHelp { Name = name };
            int mode = 0; // 0 header, 1 inputs, 2 outputs, 3 options
            FilterOption last = null;
            var seen = new HashSet<string>();
            bool skipConsts = false;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0) continue;
                if (line.StartsWith("Filter ")) continue;
                if (line.Contains("support for timeline")) { h.Timeline = true; continue; }
                if (line.TrimEnd().EndsWith("AVOptions:")) { mode = 3; last = null; continue; }
                if (mode < 3)
                {
                    string t = line.Trim();
                    if (t == "Inputs:") { mode = 1; continue; }
                    if (t == "Outputs:") { mode = 2; continue; }
                    if (mode == 0) { if (h.Desc.Length == 0) h.Desc = t; continue; }
                    var io = IoRx.Match(line);
                    if (io.Success) { (mode == 1 ? h.Inputs : h.Outputs).Add(io.Groups[1].Value.Trim()); continue; }
                    if (t.StartsWith("dynamic")) { if (mode == 1) h.DynInputs = true; else h.DynOutputs = true; }
                    if (t.StartsWith("none")) { if (mode == 1) h.NoInputs = true; else h.NoOutputs = true; }
                    continue;
                }
                var om = OptRx.Match(line);
                if (om.Success)
                {
                    string desc = om.Groups[4].Value.Trim();
                    var o = new FilterOption { Name = om.Groups[1].Value, Type = om.Groups[2].Value };
                    var dm = DefRx.Match(desc);
                    if (dm.Success) { o.Default = dm.Groups[1].Value.Trim().Trim('"'); desc = desc.Substring(0, dm.Index).Trim(); }
                    var rm = RangeRx.Match(desc);
                    if (rm.Success) { o.Range = rm.Groups[1].Value + " … " + rm.Groups[2].Value; desc = desc.Remove(rm.Index, rm.Length).Trim(); }
                    o.Desc = desc;
                    if (seen.Contains(o.Name)) { skipConsts = true; last = null; continue; }   // duplicate (framesync…)
                    skipConsts = false;
                    seen.Add(o.Name);
                    if (last != null && last.Desc == o.Desc && last.Type == o.Type && o.Desc.Length > 0)
                    {
                        // alias: keep the longest name as main
                        if (o.Name.Length > last.Name.Length) { last.Alias = last.Name; last.Name = o.Name; }
                        else last.Alias = o.Name;
                        skipConsts = true;
                        continue;
                    }
                    h.Options.Add(o);
                    last = o;
                    continue;
                }
                var cm = ConstRx.Match(line);
                if (cm.Success && last != null && !skipConsts && !line.Contains("<"))
                {
                    string cname = cm.Groups[1].Value;
                    if (!last.Consts.Any(c => c[0] == cname))
                        last.Consts.Add(new[] { cname, cm.Groups[4].Value.Trim() });
                }
            }
            if (h.Desc.Length == 0) { var fi = Find(name); if (fi != null) h.Desc = fi.Desc; }
            return h;
        }

        // ---------------- escaping ----------------
        /// Escapes an option value for use inside a filtergraph (both escaping levels).
        public static string EscapeValue(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            // Windows paths: C:\dir\file -> C\:/dir/file
            if (Regex.IsMatch(v, @"^[A-Za-z]:[\\/]") || v.StartsWith(@"\\"))
                v = v.Replace('\\', '/');
            bool special = v.IndexOfAny(new[] { ':', ',', ';', '[', ']', '\'', '\\', '=' }) >= 0;
            if (!special) return v;
            // level 1 (option parser): escape \ ' :
            string l1 = v.Replace("\\", "\\\\").Replace("'", "\\'").Replace(":", "\\:");
            // level 2 (graph parser): single-quote, with ' -> '\''
            return "'" + l1.Replace("'", "'\\''") + "'";
        }

        public static string BuildExpr(string name, IEnumerable<KeyValuePair<string, string>> values, double enableFrom, double enableTo)
        {
            var parts = new List<string>();
            foreach (var kv in values)
                if (!string.IsNullOrEmpty(kv.Value)) parts.Add(kv.Key + "=" + EscapeValue(kv.Value));
            if (enableFrom >= 0 || enableTo >= 0)
            {
                string a = Formats.Inv(Math.Max(0, enableFrom));
                string en = enableTo >= 0 ? "between(t," + a + "," + Formats.Inv(enableTo) + ")" : "gte(t," + a + ")";
                parts.Add("enable='" + en + "'");
            }
            return parts.Count == 0 ? name : name + "=" + string.Join(":", parts.ToArray());
        }

        /// Template for filters that cannot sit in a simple chain (multi-input, sources, sinks…).
        public static string GraphTemplate(FilterInfo fi, FilterHelp h, string expr)
        {
            var sb = new StringBuilder();
            int vi = 0, ai = 0;
            Func<string, string> label = io => io.Contains("(audio)") ? "[" + (ai++) + ":a]" : "[" + (vi++) + ":v]";
            if (!h.NoInputs && fi.In != "|")
            {
                if (h.Inputs.Count > 0) foreach (var i in h.Inputs) sb.Append(label(i));
                else
                {
                    // dynamic inputs: guess from the In type letters
                    bool audio = fi.In.Contains("A") || (!fi.In.Contains("V") && fi.Out.Contains("A") && !fi.Out.Contains("V"));
                    char k = audio ? 'a' : 'v';
                    sb.Append("[0:" + k + "][1:" + k + "]");
                }
            }
            sb.Append(expr);
            if (!h.NoOutputs && fi.Out != "|")
            {
                int v = 0, a = 0;
                var outs = h.Outputs.Count > 0 ? h.Outputs : new List<string> { fi.Out.Contains("A") ? "(audio)" : "(video)" };
                foreach (var o in outs)
                {
                    if (o.Contains("(audio)")) sb.Append(a++ == 0 ? "[aout]" : "[aout" + a + "]");
                    else sb.Append(v++ == 0 ? "[vout]" : "[vout" + v + "]");
                }
            }
            return sb.ToString();
        }
    }

    // =====================================================================
    //  Quick presets
    // =====================================================================
    public static class FilterPresets
    {
        public class Preset
        {
            public string Group, Label; public string V, A;
            public Preset(string g, string l, string v, string a) { Group = g; Label = l; V = v; A = a; }
        }

        public static readonly List<Preset> All = new List<Preset> {
            new Preset("Video — Color", "Brighter & punchier", "eq=brightness=0.04:contrast=1.12:saturation=1.25", null),
            new Preset("Video — Color", "Black & white", "hue=s=0", null),
            new Preset("Video — Color", "Sepia", "colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131", null),
            new Preset("Video — Color", "Negative", "negate", null),
            new Preset("Video — Color", "Vintage film", "curves=preset=vintage,vignette=PI/5", null),
            new Preset("Video — Color", "Cinematic teal & orange", "colorbalance=rs=0.1:gs=-0.02:bs=-0.12:rh=-0.05:bh=0.12", null),
            new Preset("Video — Color", "Auto levels", "normalize=smoothing=24", null),
            new Preset("Video — Color", "HDR10 → SDR (tone-map)", "setparams=color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc,zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p", null),
            new Preset("Video — Clean-up", "Sharpen", "unsharp=5:5:1.0", null),
            new Preset("Video — Clean-up", "Denoise (fast)", "hqdn3d=4:3:6:4.5", null),
            new Preset("Video — Clean-up", "Denoise (high quality, slow)", "nlmeans=s=3", null),
            new Preset("Video — Clean-up", "Deinterlace", "bwdif", null),
            new Preset("Video — Clean-up", "Stabilize (deshake)", "deshake", null),
            new Preset("Video — Clean-up", "Remove banding", "deband", null),
            new Preset("Video — Effects", "Blur", "gblur=sigma=6", null),
            new Preset("Video — Effects", "Vignette", "vignette=PI/4", null),
            new Preset("Video — Effects", "Pixelate", "pixelize=w=16:h=16", null),
            new Preset("Video — Effects", "Edge detect", "edgedetect", null),
            new Preset("Video — Effects", "Mirror (horizontal flip)", "hflip", null),
            new Preset("Video — Effects", "Text watermark (bottom right)", "drawtext=fontfile='C\\:/Windows/Fonts/arial.ttf':text='FFmpeg Forge':x=w-tw-24:y=h-th-24:fontsize=h/22:fontcolor=white@0.75:shadowx=2:shadowy=2", null),
            new Preset("Video — Effects", "Timecode overlay", "drawtext=fontfile='C\\:/Windows/Fonts/consola.ttf':text='%{pts\\:hms}':x=24:y=24:fontsize=h/24:fontcolor=white:box=1:boxcolor=black@0.5:boxborderw=8", null),
            new Preset("Video — Framing", "Crop to 16:9 (center)", "crop='min(iw,ih*16/9)':'min(ih,iw*9/16)'", null),
            new Preset("Video — Framing", "Crop to square 1:1", "crop='min(iw,ih)':'min(iw,ih)'", null),
            new Preset("Video — Framing", "Crop to 9:16 (vertical / Shorts)", "crop='min(iw,ih*9/16)':'min(ih,iw*16/9)'", null),
            new Preset("Video — Framing", "Letterbox to 16:9 (black bars)", "pad='max(iw,ceil(ih*16/9/2)*2)':'max(ih,ceil(iw*9/16/2)*2)':(ow-iw)/2:(oh-ih)/2:black", null),
            new Preset("Video — Time", "Fade in (1 s)", "fade=t=in:st=0:d=1", "afade=t=in:st=0:d=1"),
            new Preset("Video — Time", "Speed ×2", "setpts=0.5*PTS", "atempo=2.0"),
            new Preset("Video — Time", "Speed ×4 (timelapse)", "setpts=0.25*PTS", "atempo=2.0,atempo=2.0"),
            new Preset("Video — Time", "Slow motion ×0.5", "setpts=2.0*PTS", "atempo=0.5"),
            new Preset("Video — Time", "Smooth 60 fps (interpolation, slow)", "minterpolate=fps=60:mi_mode=mci:mc_mode=aobmc:vsbmc=1", null),
            new Preset("Video — Time", "Reverse (short clips only)", "reverse", "areverse"),
            new Preset("Audio", "Bass boost", null, "bass=g=8"),
            new Preset("Audio", "Treble boost", null, "treble=g=5"),
            new Preset("Audio", "Remove background noise", null, "afftdn=nf=-25"),
            new Preset("Audio", "Voice clarity", null, "highpass=f=90,lowpass=f=9000,acompressor=threshold=-18dB:ratio=3"),
            new Preset("Audio", "Compressor", null, "acompressor"),
            new Preset("Audio", "Echo", null, "aecho=0.8:0.88:60:0.4"),
            new Preset("Audio", "Reverb (hall)", null, "aecho=0.8:0.9:40|80|120:0.4|0.3|0.2"),
            new Preset("Audio", "8D rotating audio", null, "apulsator=hz=0.08"),
            new Preset("Audio", "Stereo widening", null, "extrastereo=m=2"),
            new Preset("Audio", "Remove vocals (karaoke)", null, "pan='stereo|c0=c0-c1|c1=c1-c0'"),
            new Preset("Audio", "Pitch up (+2 semitones)", null, "rubberband=pitch=1.12246"),
            new Preset("Audio", "Pitch down (−2 semitones)", null, "rubberband=pitch=0.89090"),
            new Preset("Audio", "Trim silence at start", null, "silenceremove=start_periods=1:start_threshold=-50dB"),
            new Preset("Audio", "Fade out (last part, 3 s at 60 s)", null, "afade=t=out:st=57:d=3"),
            new Preset("Audio", "Speed ×1.5 (same pitch)", null, "atempo=1.5"),
            new Preset("Audio", "Reverse", null, "areverse"),
        };
    }

    // =====================================================================
    //  Filter browser dialog
    // =====================================================================
    public class FilterBrowser : Form
    {
        TextBox search, exprBox;
        ComboBox cbCat, cbKind;
        DarkList lv;
        Label countLbl, titleLbl, descLbl, ioLbl, loadingLbl, kindLbl;
        Panel optHost;
        TableLayoutPanel optTable;
        NumericUpDown nuFrom, nuTo;
        CheckBox chkEnable;
        Panel timelineRow;
        FlatBtn btnOk, btnDocs;
        FilterInfo current;
        FilterHelp currentHelp;
        readonly Dictionary<string, Control> editors = new Dictionary<string, Control>();
        bool suppress;
        Font fUI = new Font("Segoe UI", 9.5f), fSmall = new Font("Segoe UI", 8.5f), fSec = new Font("Segoe UI Semibold", 8.5f), fMono = new Font("Consolas", 10f);
        int loadToken;

        /// Result: entry to add to the chain, or GraphText to append to the -filter_complex box.
        public FilterEntry Result;
        public string GraphText;
        readonly FilterEntry editing;

        public FilterBrowser(FilterEntry edit)
        {
            editing = edit;
            Text = edit == null ? "FFmpeg Forge — Filter library" : "FFmpeg Forge — Edit filter";
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = fUI;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(Theme.S(1180), Theme.S(780));
            MinimumSize = new Size(Theme.S(900), Theme.S(600));
            ShowInTaskbar = false;
            MinimizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Build();
            Fill();
            if (edit != null)
            {
                var fi = FilterCatalog.Find(edit.Name);
                if (fi != null) SelectFilter(fi, edit);
                else { exprBox.Text = edit.Expr; cbKind.SelectedIndex = edit.Kind == 'A' ? 1 : 0; }
            }
        }

        void Build()
        {
            // ---------------- left: list ----------------
            var left = new Panel { Dock = DockStyle.Left, Width = Theme.S(520), BackColor = Theme.Panel, Padding = new Padding(Theme.S(14)) };
            var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(40), ColumnCount = 2, BackColor = Theme.Panel };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            search = new TextBox { Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10.5f), Margin = new Padding(0, Theme.S(4), Theme.S(8), 0) };
            search.TextChanged += (s, e) => Fill();
            search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down && lv.Items.Count > 0) { lv.Focus(); lv.Items[0].Selected = true; } };
            Cue(search, "Search 500+ filters… (name or description)");
            cbCat = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Card, ForeColor = Theme.Text, Font = fUI, Margin = new Padding(0, Theme.S(4), 0, 0) };
            cbCat.Items.AddRange(new object[] { "All filters", "Video", "Audio", "Multi / conversion", "Source", "Sink", "Timeline (enable) support" });
            cbCat.SelectedIndex = 0;
            cbCat.SelectedIndexChanged += (s, e) => Fill();
            top.Controls.Add(search, 0, 0); top.Controls.Add(cbCat, 1, 0);
            countLbl = new Label { Dock = DockStyle.Top, Height = Theme.S(26), ForeColor = Theme.Muted, Font = fSmall, TextAlign = ContentAlignment.MiddleLeft };
            lv = new DarkList { Dock = DockStyle.Fill, MultiSelect = false, HideSelection = false };
            lv.Columns.Add("Filter", Theme.S(140));
            lv.Columns.Add("Type", Theme.S(72));
            lv.Columns.Add("Description", Theme.S(270));
            lv.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                var r = e.Bounds; r.X += Theme.S(6);
                TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), fSec, r, Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            lv.DrawItem += (s, e) => { };
            lv.DrawSubItem += (s, e) =>
            {
                var fi = (FilterInfo)e.Item.Tag;
                Color bg = e.Item.Selected ? Theme.Selection : (e.ItemIndex % 2 == 0 ? Theme.Panel : Color.FromArgb(26, 29, 37));
                using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
                if (e.Item.Selected && e.ColumnIndex == 0)
                    using (var a = Theme.AccentBrush(new RectangleF(e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height))) e.Graphics.FillRectangle(a, e.Bounds.X, e.Bounds.Y, Theme.S(3), e.Bounds.Height);
                var r = e.Bounds; r.X += Theme.S(6); r.Width -= Theme.S(8);
                Color c = e.ColumnIndex == 0 ? Theme.Text : Theme.Muted;
                Font f = e.ColumnIndex == 0 ? fSec : fUI;
                if (e.ColumnIndex == 1)
                    c = fi.IsVideo ? Color.FromArgb(120, 170, 255) : fi.IsAudio ? Color.FromArgb(190, 140, 255) : fi.In == "|" ? Color.FromArgb(90, 220, 200) : Theme.Accent1;
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, f, r, c, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            lv.SelectedIndexChanged += (s, e) => { if (lv.SelectedItems.Count > 0) SelectFilter((FilterInfo)lv.SelectedItems[0].Tag, null); };
            lv.DoubleClick += (s, e) => Accept();
            lv.Resize += (s, e) => { int w = lv.ClientSize.Width - lv.Columns[0].Width - lv.Columns[1].Width - 2; if (w > 60) lv.Columns[2].Width = w; };
            var lvHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, Theme.S(4), 0, 0), BackColor = Theme.Panel };
            lvHost.Controls.Add(lv);
            left.Controls.Add(lvHost); left.Controls.Add(countLbl); left.Controls.Add(top);
            lvHost.BringToFront();

            // ---------------- right: details ----------------
            var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(Theme.S(20), Theme.S(14), Theme.S(20), Theme.S(10)) };
            var head = new Panel { Dock = DockStyle.Top, Height = Theme.S(96), BackColor = Theme.Bg };
            titleLbl = new Label { Text = "Choose a filter", Font = new Font("Segoe UI", 18f, FontStyle.Bold), ForeColor = Color.White, AutoSize = true, Location = new Point(0, 0) };
            descLbl = new Label { Text = "Every filter of your FFmpeg build is listed on the left. Pick one to set its parameters.", ForeColor = Theme.Text, AutoSize = false, Location = new Point(0, Theme.S(40)), Size = new Size(Theme.S(560), Theme.S(36)), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            ioLbl = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, Location = new Point(0, Theme.S(76)) };
            btnDocs = new FlatBtn("Documentation ↗") { Width = Theme.S(140), Height = Theme.S(30), Font = fSmall, Anchor = AnchorStyles.Top | AnchorStyles.Right, Visible = false };
            btnDocs.Click += (s, e) => { if (current != null) try { Process.Start("https://ffmpeg.org/ffmpeg-filters.html#" + current.Name); } catch { } };
            head.Controls.AddRange(new Control[] { titleLbl, descLbl, ioLbl, btnDocs });
            head.Resize += (s, e) => { btnDocs.Location = new Point(head.Width - btnDocs.Width, 0); descLbl.Width = head.Width - btnDocs.Width - Theme.S(10); };

            var optTitle = new Label { Text = "PARAMETERS  (leave empty = FFmpeg default · hover a name for help · expressions allowed)", Dock = DockStyle.Top, Height = Theme.S(26), ForeColor = Theme.Accent1, Font = fSec, TextAlign = ContentAlignment.BottomLeft };
            optHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Panel, Padding = new Padding(Theme.S(12), Theme.S(8), Theme.S(12), Theme.S(8)) };
            loadingLbl = new Label { Text = "", ForeColor = Theme.Muted, AutoSize = true, Location = new Point(Theme.S(12), Theme.S(12)) };
            optHost.Controls.Add(loadingLbl);

            // timeline row
            timelineRow = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(40), BackColor = Theme.Bg, Visible = false, Padding = new Padding(0, Theme.S(8), 0, 0) };
            chkEnable = new CheckBox { Text = "Apply only between", ForeColor = Theme.Text, AutoSize = true, FlatStyle = FlatStyle.Flat, Location = new Point(0, Theme.S(10)) };
            nuFrom = new NumericUpDown { Minimum = 0, Maximum = 360000, DecimalPlaces = 2, Width = Theme.S(90), BackColor = Theme.Card, ForeColor = Theme.Text, Location = new Point(Theme.S(170), Theme.S(8)) };
            var andLbl = new Label { Text = "s   and", ForeColor = Theme.Muted, AutoSize = true, Location = new Point(Theme.S(266), Theme.S(11)) };
            nuTo = new NumericUpDown { Minimum = 0, Maximum = 360000, DecimalPlaces = 2, Value = 5, Width = Theme.S(90), BackColor = Theme.Card, ForeColor = Theme.Text, Location = new Point(Theme.S(330), Theme.S(8)) };
            var sLbl = new Label { Text = "s  (timeline)", ForeColor = Theme.Muted, AutoSize = true, Location = new Point(Theme.S(426), Theme.S(11)) };
            timelineRow.Controls.AddRange(new Control[] { chkEnable, nuFrom, andLbl, nuTo, sLbl });
            EventHandler regen = (s, e) => Regenerate();
            chkEnable.CheckedChanged += regen; nuFrom.ValueChanged += regen; nuTo.ValueChanged += regen;

            // expression row
            var exprRow = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(92), BackColor = Theme.Bg, Padding = new Padding(0, Theme.S(8), 0, 0) };
            var exprTitle = new Label { Text = "FILTER EXPRESSION  (editable — you can also type any raw filter here)", Dock = DockStyle.Top, Height = Theme.S(22), ForeColor = Theme.Accent1, Font = fSec };
            exprBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, BackColor = Color.FromArgb(12, 13, 17), ForeColor = Color.FromArgb(255, 200, 140), BorderStyle = BorderStyle.FixedSingle, Font = fMono, ScrollBars = ScrollBars.Vertical };
            var kindHost = new Panel { Dock = DockStyle.Right, Width = Theme.S(170), BackColor = Theme.Bg, Padding = new Padding(Theme.S(10), 0, 0, 0) };
            kindLbl = new Label { Text = "Apply to", Dock = DockStyle.Top, Height = Theme.S(20), ForeColor = Theme.Muted, Font = fSmall };
            cbKind = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Card, ForeColor = Theme.Text };
            cbKind.Items.AddRange(new object[] { "Video stream", "Audio stream", "Filtergraph (advanced)" });
            cbKind.SelectedIndex = 0;
            kindHost.Controls.Add(cbKind); kindHost.Controls.Add(kindLbl);
            exprRow.Controls.Add(exprBox); exprRow.Controls.Add(kindHost); exprRow.Controls.Add(exprTitle);
            exprBox.BringToFront();

            // buttons
            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(56), FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Bg, Padding = new Padding(0, Theme.S(10), 0, 0) };
            btnOk = new FlatBtn(editing == null ? "ADD TO CHAIN" : "SAVE CHANGES") { Primary = true, Width = Theme.S(200), Height = Theme.S(40), Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
            btnOk.Click += (s, e) => Accept();
            var btnCancel = new FlatBtn("Cancel") { Width = Theme.S(110), Height = Theme.S(40) };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            var btnReset = new FlatBtn("Reset parameters") { Width = Theme.S(160), Height = Theme.S(40) };
            btnReset.Click += (s, e) => { if (current != null) SelectFilter(current, null); };
            btnOk.Margin = btnCancel.Margin = btnReset.Margin = new Padding(Theme.S(10), 0, 0, 0);
            btnRow.Controls.AddRange(new Control[] { btnOk, btnCancel, btnReset });
            cbKind.SelectedIndexChanged += (s, e) => btnOk.Text = cbKind.SelectedIndex == 2 ? "INSERT IN FILTERGRAPH" : (editing == null ? "ADD TO CHAIN" : "SAVE CHANGES");

            right.Controls.Add(optHost);
            right.Controls.Add(optTitle);
            right.Controls.Add(timelineRow);
            right.Controls.Add(exprRow);
            right.Controls.Add(btnRow);
            right.Controls.Add(head);
            optHost.BringToFront();

            Controls.Add(right);
            Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.Border });
            Controls.Add(left);
            right.BringToFront();
            AcceptButton = null;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
            Shown += (s, e) => search.Focus();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, string lp);
        static void Cue(TextBox t, string cue) { t.HandleCreated += (s, e) => { try { SendMessage(t.Handle, 0x1501, (IntPtr)1, cue); } catch { } }; }

        void Fill()
        {
            string q = search.Text.Trim().ToLowerInvariant();
            int cat = cbCat.SelectedIndex;
            var items = FilterCatalog.All.Where(f =>
            {
                if (cat == 1 && !f.IsVideo) return false;
                if (cat == 2 && !f.IsAudio) return false;
                if (cat == 3 && f.Category != "Multi / conversion") return false;
                if (cat == 4 && f.In != "|") return false;
                if (cat == 5 && f.Out != "|") return false;
                if (cat == 6 && !f.Timeline) return false;
                if (q.Length == 0) return true;
                return f.Name.ToLowerInvariant().Contains(q) || f.Desc.ToLowerInvariant().Contains(q);
            }).OrderBy(f => q.Length > 0 && !f.Name.ToLowerInvariant().StartsWith(q) ? 1 : 0).ThenBy(f => f.Name).ToList();
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var f in items)
                lv.Items.Add(new ListViewItem(new[] { f.Name, f.TypeLabel, f.Desc }) { Tag = f, ToolTipText = f.Desc });
            lv.EndUpdate();
            countLbl.Text = FilterCatalog.All.Count == 0
                ? (FilterCatalog.Loaded ? "Could not read the filter list from FFmpeg. " + (FilterCatalog.LoadError ?? "") : "Loading filters from FFmpeg…")
                : items.Count + " of " + FilterCatalog.All.Count + " filters available in your FFmpeg build";
        }

        void SelectFilter(FilterInfo fi, FilterEntry prefill)
        {
            current = fi;
            titleLbl.Text = fi.Name;
            descLbl.Text = fi.Desc;
            ioLbl.Text = fi.Category + "  ·  " + fi.TypeLabel + (fi.Timeline ? "  ·  timeline" : "") + (fi.Slice ? "  ·  multithreaded" : "") + (fi.Command ? "  ·  runtime commands" : "");
            btnDocs.Visible = true;
            cbKind.SelectedIndex = fi.IsVideo ? 0 : fi.IsAudio ? 1 : 2;
            timelineRow.Visible = fi.Timeline;
            chkEnable.Checked = false;
            suppress = true;
            optHost.SuspendLayout();
            foreach (Control c in optHost.Controls.Cast<Control>().ToList()) if (c != loadingLbl) { optHost.Controls.Remove(c); c.Dispose(); }
            optHost.ResumeLayout();
            editors.Clear();
            loadingLbl.Text = "Reading parameters of “" + fi.Name + "”…";
            loadingLbl.Visible = true;
            exprBox.Text = prefill != null ? prefill.Expr : fi.Name;
            if (prefill != null) cbKind.SelectedIndex = prefill.Kind == 'A' ? 1 : 0;
            int token = ++loadToken;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                FilterHelp h;
                try { h = FilterCatalog.GetHelp(fi.Name); } catch (Exception ex) { h = new FilterHelp { Name = fi.Name, Desc = ex.Message }; }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (token != loadToken) return;
                        currentHelp = h;
                        BuildEditors(h, prefill);
                        suppress = false;
                        if (prefill == null) Regenerate();
                    }));
                }
                catch { }
            });
        }

        void BuildEditors(FilterHelp h, FilterEntry prefill)
        {
            loadingLbl.Visible = false;
            string io = "";
            if (h.NoInputs) io += "Inputs: none (source)"; else if (h.DynInputs) io += "Inputs: dynamic"; else if (h.Inputs.Count > 0) io += "Inputs: " + string.Join(", ", h.Inputs.ToArray());
            if (h.NoOutputs) io += "   Outputs: none (sink)"; else if (h.DynOutputs) io += "   Outputs: dynamic"; else if (h.Outputs.Count > 0) io += "   Outputs: " + string.Join(", ", h.Outputs.ToArray());
            if (io.Length > 0) ioLbl.Text = ioLbl.Text + "     " + io;

            optTable = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, BackColor = Theme.Panel };
            optTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(170)));
            optTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(230)));
            optTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };
            if (h.Options.Count == 0)
            {
                var l = new Label { Text = "This filter has no parameters — it works as is.", ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(0, Theme.S(6), 0, 0) };
                optTable.Controls.Add(l, 0, 0); optTable.SetColumnSpan(l, 3);
            }
            int row = 0;
            Dictionary<string, string> pre = prefill != null ? (prefill.Values ?? ParseExpr(prefill.Expr)) : null;
            foreach (var o in h.Options)
            {
                var name = new Label { Text = o.Name + (o.Alias != null ? "  (" + o.Alias + ")" : ""), ForeColor = Theme.Text, Font = fSec, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, Theme.S(3), Theme.S(6), Theme.S(3)), Height = Theme.S(28) };
                string help = o.Desc + (o.Range != null ? "\nRange: " + o.Range : "") + (o.Default != null ? "\nDefault: " + o.Default : "") + "\nType: " + o.Type;
                if (o.Consts.Count > 0) help += "\nValues: " + string.Join(", ", o.Consts.Select(c => c[0]).Take(40).ToArray());
                tip.SetToolTip(name, help);
                Control ed = MakeEditor(o);
                tip.SetToolTip(ed, help);
                string hint = o.Desc;
                if (o.Default != null && o.Default.Length > 0) hint += "   [default: " + o.Default + "]";
                if (o.Range != null) hint += "   [" + o.Range + "]";
                var info = new Label { Text = hint, ForeColor = Theme.Muted, Font = fSmall, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(Theme.S(8), Theme.S(3), 0, Theme.S(3)), Height = Theme.S(28) };
                tip.SetToolTip(info, help);
                optTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                optTable.Controls.Add(name, 0, row);
                optTable.Controls.Add(ed, 1, row);
                optTable.Controls.Add(info, 2, row);
                editors[o.Name] = ed;
                if (pre != null)
                {
                    string v = null;
                    if (!pre.TryGetValue(o.Name, out v) && o.Alias != null) pre.TryGetValue(o.Alias, out v);
                    if (v != null) SetValue(ed, v);
                }
                row++;
            }
            optHost.Controls.Add(optTable);
            optTable.Width = optHost.ClientSize.Width - Theme.S(26);
            optHost.Resize += (s, e) => { if (optTable != null && !optTable.IsDisposed) optTable.Width = optHost.ClientSize.Width - Theme.S(26); };
        }

        Control MakeEditor(FilterOption o)
        {
            string t = o.Type.ToLowerInvariant();
            Control c;
            if (t == "boolean" || t == "bool")
            {
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                cb.Items.AddRange(new object[] { "", "true", "false" });
                cb.SelectedIndex = 0;
                cb.SelectedIndexChanged += (s, e) => Regenerate();
                c = cb;
            }
            else if (o.Consts.Count > 0)
            {
                var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, MaxDropDownItems = 20 };
                cb.Items.Add("");
                foreach (var k in o.Consts) cb.Items.Add(k[0]);
                cb.TextChanged += (s, e) => Regenerate();
                c = cb;
            }
            else if (t == "color")
            {
                var host = new Panel { Height = Theme.S(28) };
                var tb = new TextBox { Dock = DockStyle.Fill };
                var b = new FlatBtn("🎨") { Dock = DockStyle.Right, Width = Theme.S(34), Height = Theme.S(26), Font = fSmall, Text = "…" };
                b.Click += (s, e) =>
                {
                    using (var d = new ColorDialog { FullOpen = true })
                        if (d.ShowDialog(this) == DialogResult.OK) tb.Text = "0x" + d.Color.R.ToString("X2") + d.Color.G.ToString("X2") + d.Color.B.ToString("X2");
                };
                Style(tb);
                tb.TextChanged += (s, e) => Regenerate();
                host.Controls.Add(tb); host.Controls.Add(b);
                host.Tag = tb;
                host.Dock = DockStyle.Fill; host.Margin = new Padding(0, Theme.S(3), 0, Theme.S(3));
                return host;
            }
            else if (t == "string" && Regex.IsMatch(o.Name, "file|path|filename|fontfile|lut|model|dict", RegexOptions.IgnoreCase))
            {
                var host = new Panel { Height = Theme.S(28) };
                var tb = new TextBox { Dock = DockStyle.Fill };
                var b = new FlatBtn("…") { Dock = DockStyle.Right, Width = Theme.S(34), Height = Theme.S(26) };
                b.Click += (s, e) =>
                {
                    using (var d = new OpenFileDialog { Title = o.Name, Filter = "All files (*.*)|*.*" })
                        if (d.ShowDialog(this) == DialogResult.OK) tb.Text = d.FileName;
                };
                Style(tb);
                tb.TextChanged += (s, e) => Regenerate();
                host.Controls.Add(tb); host.Controls.Add(b);
                host.Tag = tb;
                host.Dock = DockStyle.Fill; host.Margin = new Padding(0, Theme.S(3), 0, Theme.S(3));
                return host;
            }
            else
            {
                var tb = new TextBox();
                tb.TextChanged += (s, e) => Regenerate();
                c = tb;
            }
            Style(c);
            c.Dock = DockStyle.Fill;
            c.Margin = new Padding(0, Theme.S(3), 0, Theme.S(3));
            return c;
        }

        void Style(Control c)
        {
            c.BackColor = Theme.Card; c.ForeColor = Theme.Text; c.Font = fUI;
            var tb = c as TextBox; if (tb != null) tb.BorderStyle = BorderStyle.FixedSingle;
            var cb = c as ComboBox; if (cb != null) cb.FlatStyle = FlatStyle.Flat;
        }

        static string GetValue(Control c)
        {
            if (c.Tag is TextBox) return ((TextBox)c.Tag).Text.Trim();
            return (c.Text ?? "").Trim();
        }

        static void SetValue(Control c, string v)
        {
            if (c.Tag is TextBox) { ((TextBox)c.Tag).Text = v; return; }
            var cb = c as ComboBox;
            if (cb != null && cb.DropDownStyle == ComboBoxStyle.DropDownList) { int i = cb.Items.IndexOf(v); if (i >= 0) cb.SelectedIndex = i; return; }
            c.Text = v;
        }

        /// Naive parser of "name=k=v:k2=v2" (handles quotes) used to pre-fill editors.
        static Dictionary<string, string> ParseExpr(string expr)
        {
            var d = new Dictionary<string, string>();
            int eq = expr.IndexOf('=');
            if (eq < 0) return d;
            string args = expr.Substring(eq + 1);
            var parts = new List<string>();
            var sb = new StringBuilder(); bool q = false;
            for (int i = 0; i < args.Length; i++)
            {
                char ch = args[i];
                if (ch == '\\' && i + 1 < args.Length) { sb.Append(args[i + 1]); i++; continue; }
                if (ch == '\'') { q = !q; continue; }
                if (ch == ':' && !q) { parts.Add(sb.ToString()); sb.Clear(); continue; }
                sb.Append(ch);
            }
            parts.Add(sb.ToString());
            foreach (var p in parts)
            {
                int i = p.IndexOf('=');
                if (i > 0) d[p.Substring(0, i)] = p.Substring(i + 1);
            }
            return d;
        }

        Dictionary<string, string> CurrentValues()
        {
            var vals = new Dictionary<string, string>();
            foreach (var kv in editors) { string v = GetValue(kv.Value); if (v.Length > 0) vals[kv.Key] = v; }
            return vals;
        }

        void Regenerate()
        {
            if (suppress || current == null) return;
            var vals = CurrentValues();
            double a = -1, b = -1;
            if (chkEnable.Checked && current.Timeline) { a = (double)nuFrom.Value; b = (double)nuTo.Value; if (b <= a) b = -1; }
            string expr = FilterCatalog.BuildExpr(current.Name, vals, a, b);
            if (!current.IsSimple && currentHelp != null) expr = FilterCatalog.GraphTemplate(current, currentHelp, expr);
            exprBox.Text = expr;
        }

        void Accept()
        {
            string expr = exprBox.Text.Replace("\r", "").Replace("\n", "").Trim();
            if (expr.Length == 0) { if (current == null) return; expr = current.Name; }
            if (cbKind.SelectedIndex == 2) { GraphText = expr; DialogResult = DialogResult.OK; Close(); return; }
            Result = new FilterEntry { Kind = cbKind.SelectedIndex == 1 ? 'A' : 'V', Expr = expr, Enabled = editing == null || editing.Enabled, Values = current != null ? CurrentValues() : null };
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
