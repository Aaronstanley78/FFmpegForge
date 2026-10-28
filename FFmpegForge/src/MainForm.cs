using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FFmpegForge
{
    public class MainForm : Form
    {
        // ---- controls ----
        Header header;
        DarkList list;
        DropHint dropHint;
        RichTextBox log;
        GradientBar overall;
        Label statusLbl;
        FlatBtn btnConvert, btnCancel, btnOpenOut;
        FlatBtn catVideo, catAudio, catImage;
        ComboBox cbFormat;
        Label formatHint;
        TableLayoutPanel secVideo, secAudio, secImage, secFilters, secTrim, secOut;
        // video
        ComboBox cbVCodec, cbQuality, cbPreset, cbFps, cbRotate, cbACodec;
        NumericUpDown nuNvCrf; Label lblNvCrf, lblNvCrfHint;
        ResPicker resVideo, resImage;
        NumericUpDown nuVBitrate, nuStill;
        CheckBox chkSubs;
        // Scale (Échelle)
        TableLayoutPanel secScale, secOpti, secDn;
        // SeedVR2
        TableLayoutPanel secSeed;
        CheckBox chkSeed, chkSeedTiled, chkSeedOffload, chkSeedCompile;
        ComboBox cbSeedModel, cbSeedSize, cbSeedBatch, cbSeedColor, cbSeedAttn;
        TrackBar tbSeedInNoise, tbSeedLatNoise;
        Label lblSeedInNoise, lblSeedLatNoise, lblSeedStatus, lblSeedInfo;
        NumericUpDown nuSeedSeed, nuSeedChunk, nuSeedOverlap, nuSeedSwap;
        TextBox txtSeedGpu;
        readonly List<Control> seedRows = new List<Control>(), seedVidRows = new List<Control>();
        CheckBox chkDn, chkDnTemporal, chkDnAuto;
        ComboBox cbDnPreset, cbDnEngine;
        TrackBar tbDnStrength, tbDnSharp;
        Label lblDnStrengthVal, lblDnSharpVal, lblDnInfo;
        readonly List<Control> dnRows = new List<Control>();
        CheckBox chkOpti, chkOptiContrast;
        ComboBox cbOptiMult, cbOptiKernel;
        TrackBar tbOptiSharp;
        Label lblOptiSharpVal, lblOptiInfo;
        readonly List<Control> optiRows = new List<Control>();
        static readonly double[] OptiMults = { 1.25, 1.5, 2, 2.5, 3 };
        CheckBox chkScale, chkScaleOpt, chkScaleFid, chkScale444;
        ComboBox cbScaleType, cbScaleFactor;
        TrackBar tbScaleSharp;
        Label lblScaleSharpVal, lblScaleInfo;
        readonly List<Control> scaleRows = new List<Control>();
        // NVIDIA RTX AI
        TableLayoutPanel secRtx;
        CheckBox chkVsr, chkHdr, chkDlss;
        ComboBox cbVsrQ, cbVsrScale, cbDlssMode, cbRtxEngine;
        NumericUpDown nuHdrContrast, nuHdrSat, nuHdrGray, nuHdrLum;
        Label lblRtxStatus;
        FlatBtn btnVsrParams, btnHdrParams, btnDlssParams;
        string vsrExpr, hdrExpr, dlssExpr;
        readonly List<Control> vsrRows = new List<Control>(), hdrRows = new List<Control>(), dlssRows = new List<Control>();
        Label lblACodec;
        // audio
        ComboBox cbAKbps, cbRate, cbCh, cbUpmix;
        // combo order -> ChannelLayouts style code (0 auto, 5 spatial, 1 music, 2 cinema, 3 live, 4 off)
        static readonly int[] UpmixCodes = { 0, ChannelLayouts.StyleSpatial, 1, 2, 3, 4 };
        Label lblUpmix;
        NumericUpDown nuVolume;
        CheckBox chkNorm;
        // image
        ComboBox cbExtract, cbImgRotate;
        TrackBar tbImgQ;
        Label lblImgQ;
        NumericUpDown nuExtractVal;
        Label lblExtractVal;
        // filters
        CheckedListBox filterList;
        Label filterTitle;
        TextBox txtGraph, txtExtraInputs;
        FlatBtn btnPresets;
        // trim / output
        TextBox txtStart, txtEnd, txtOutDir, txtCustom;
        CheckBox chkSameDir, chkOverwrite;
        FlatBtn btnBrowseOut;

        MediaKind category = MediaKind.Video;
        volatile bool running, cancelRequested;
        string lastOutput;
        readonly List<FilterEntry> filters = new List<FilterEntry>();
        readonly Dictionary<string, string> settings = new Dictionary<string, string>();
        readonly string settingsPath = Path.Combine(AppPaths.DataDir, "settings.ini");
        Font fUI, fSmall, fSection, fMono;
        ToolTip tips;

        const string StQueued = "Queued", StConverting = "Converting…", StDone = "Done", StError = "Error", StCancelled = "Cancelled";

        public MainForm()
        {
            using (var g = CreateGraphics()) Theme.Scale = g.DpiX / 96f;
            fUI = new Font("Segoe UI", 9.5f);
            fSmall = new Font("Segoe UI", 8.5f);
            fSection = new Font("Segoe UI Semibold", 8.5f);
            fMono = new Font("Consolas", 9f);
            tips = new ToolTip { AutoPopDelay = 15000 };

            Text = "FFmpeg Forge 1.1.3a — Universal Video • Audio • Image converter" + (AppPaths.Portable ? "  (portable)" : "");
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = fUI;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Theme.S(1340), Theme.S(880));
            MinimumSize = new Size(Theme.S(1060), Theme.S(700));
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            LoadSettings();
            BuildUI();
            ApplySettings();

            DragEnter += OnDragEnter; DragDrop += OnDragDrop; DragLeave += (s, e) => { dropHint.Highlight = false; dropHint.Invalidate(); };
            FormClosing += OnClosing;
            Shown += (s, e) => InitFFmpeg();
        }

        // =====================================================================
        //  UI construction
        // =====================================================================
        void BuildUI()
        {
            SuspendLayout();
            // ---------- center ----------
            var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(Theme.S(16), Theme.S(12), Theme.S(8), Theme.S(8)) };

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(46), BackColor = Theme.Bg, WrapContents = false, Padding = new Padding(0, 0, 0, Theme.S(6)) };
            var bAddF = Btn("+  Add files", 130); bAddF.Primary = true; bAddF.Click += (s, e) => AddFilesDialog();
            var bAddD = Btn("+  Add folder", 130); bAddD.Click += (s, e) => AddFolderDialog();
            var bRem = Btn("Remove", 90); bRem.Click += (s, e) => RemoveSelected();
            var bClr = Btn("Clear all", 95); bClr.Click += (s, e) => ClearAll();
            var bCmd = Btn("Show command", 135); bCmd.Click += (s, e) => PreviewCommand();
            var bFl = Btn("Filter library", 130); bFl.Click += (s, e) => AddFilter();
            foreach (var b in new[] { bAddF, bAddD, bRem, bClr, bCmd, bFl }) { b.Margin = new Padding(0, 0, Theme.S(8), 0); toolbar.Controls.Add(b); }

            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(1) };
            list = new DarkList { Dock = DockStyle.Fill, Font = fUI, AllowDrop = true, ShowItemToolTips = true };
            list.Columns.Add("File", Theme.S(300));
            list.Columns.Add("Type", Theme.S(70));
            list.Columns.Add("Info", Theme.S(150));
            list.Columns.Add("Duration", Theme.S(80));
            list.Columns.Add("Size", Theme.S(80));
            list.Columns.Add("Status", Theme.S(170));
            list.DrawColumnHeader += List_DrawColumnHeader;
            list.DrawItem += (s, e) => { };
            list.DrawSubItem += List_DrawSubItem;
            list.DragEnter += OnDragEnter; list.DragDrop += OnDragDrop;
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); if (e.Control && e.KeyCode == Keys.A) foreach (ListViewItem it in list.Items) it.Selected = true; };
            list.DoubleClick += (s, e) => OpenSelectedOutput();
            list.Resize += (s, e) => FitColumns();
            list.ContextMenuStrip = BuildContextMenu();

            dropHint = new DropHint { Dock = DockStyle.Fill, AllowDrop = true };
            dropHint.Click += (s, e) => AddFilesDialog();
            dropHint.DragEnter += OnDragEnter; dropHint.DragDrop += OnDragDrop;
            dropHint.DragLeave += (s, e) => { dropHint.Highlight = false; dropHint.Invalidate(); };
            listHost.Controls.Add(list);
            listHost.Controls.Add(dropHint);
            dropHint.BringToFront();

            var logHost = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(150), BackColor = Theme.Bg, Padding = new Padding(0, Theme.S(10), 0, 0) };
            var logTitle = new Label { Text = "LOG", Dock = DockStyle.Top, Height = Theme.S(20), ForeColor = Theme.Muted, Font = fSection };
            log = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 13, 17), ForeColor = Theme.Muted, BorderStyle = BorderStyle.None, Font = fMono, ReadOnly = true, WordWrap = false, DetectUrls = false };
            logHost.Controls.Add(log); logHost.Controls.Add(logTitle);

            center.Controls.Add(listHost);
            center.Controls.Add(logHost);
            center.Controls.Add(toolbar);
            listHost.BringToFront();

            // ---------- options panel ----------
            var right = new Panel { Dock = DockStyle.Right, Width = Theme.S(450), BackColor = Theme.Panel, AutoScroll = true, Padding = new Padding(Theme.S(18), Theme.S(14), Theme.S(18), Theme.S(14)) };
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Theme.Panel };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Format
            stack.Controls.Add(SectionTitle("OUTPUT FORMAT"));
            var cats = new TableLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(42), ColumnCount = 3, Margin = new Padding(0, 0, 0, Theme.S(8)) };
            for (int i = 0; i < 3; i++) cats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            catVideo = Btn("VIDEO", 0); catAudio = Btn("AUDIO", 0); catImage = Btn("IMAGE", 0);
            foreach (var b in new[] { catVideo, catAudio, catImage }) { b.Dock = DockStyle.Fill; b.Height = Theme.S(38); b.Margin = new Padding(0, 0, Theme.S(6), 0); cats.Controls.Add(b); }
            catImage.Margin = new Padding(0);
            catVideo.Click += (s, e) => SetCategory(MediaKind.Video);
            catAudio.Click += (s, e) => SetCategory(MediaKind.Audio);
            catImage.Click += (s, e) => SetCategory(MediaKind.Image);
            stack.Controls.Add(cats);
            cbFormat = Combo(); cbFormat.Dock = DockStyle.Top; cbFormat.Margin = new Padding(0, 0, 0, Theme.S(2));
            cbFormat.Font = new Font("Segoe UI Semibold", 10.5f);
            cbFormat.SelectedIndexChanged += (s, e) => OnFormatChanged();
            stack.Controls.Add(cbFormat);
            formatHint = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = fSmall, Margin = new Padding(0, Theme.S(2), 0, Theme.S(4)) };
            stack.Controls.Add(formatHint);

            // Video
            secVideo = Section("VIDEO SETTINGS");
            cbVCodec = Combo(Formats.VideoCodecs.Cast<object>().ToArray());
            cbQuality = Combo("Maximum", "Very high (recommended)", "Balanced", "Compact", "Very compact");
            SkipSeparator(cbVCodec);
            nuVBitrate = Num(0, 10000000, 0, 0); nuVBitrate.Increment = 500;
            cbPreset = Combo("Ultra fast", "Very fast", "Fast", "Medium", "Slow (better)", "Very slow (best)");
            cbFps = Combo("Original", "12", "15", "23.976", "24", "25", "29.97", "30", "48", "50", "59.94", "60", "120");
            cbRotate = Combo("None", "90° clockwise", "180°", "90° counter-clockwise", "Mirror horizontal", "Mirror vertical");
            nuStill = Num(1, 36000, 5, 1);
            chkSubs = Check("Keep all tracks + subtitles (MKV)");
            AddRow(secVideo, "Video codec", cbVCodec);
            AddRow(secVideo, "Quality", cbQuality);
            nuNvCrf = Num(0, 51, 0, 0);
            lblNvCrf = AddRow(secVideo, "CRF NVENC (0 = auto)", nuNvCrf);
            lblNvCrfHint = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(400), 0), Margin = new Padding(0, 0, 0, Theme.S(4)) };
            AddRow(secVideo, null, lblNvCrfHint);
            tips.SetToolTip(nuNvCrf, "Constant quality for H.264 / H.265 NVIDIA NVENC (FFmpeg -rc vbr -cq), like x264 / x265 CRF:\n0 = automatic (from the Quality setting)  ·  1 = best / biggest  ·  51 = worst / smallest.\nGood values: 18–23 for H.264, 20–26 for H.265. A bitrate above 0 overrides it.");
            nuNvCrf.ValueChanged += (s2, e2) => UpdateNvCrfRow();
            nuVBitrate.ValueChanged += (s2, e2) => UpdateNvCrfRow();
            cbVCodec.SelectedIndexChanged += (s2, e2) => UpdateNvCrfRow();
            AddRow(secVideo, "Bitrate (kbps, 0 = auto)", nuVBitrate);
            AddRow(secVideo, "Encoding speed", cbPreset);
            resVideo = MakeRes(secVideo, new[] {
                "Original", "8K UHD — 7680×4320", "DCI 4K — 4096×2160", "4K UHD — 3840×2160", "1440p — 2560×1440", "DCI 2K — 2048×1080",
                "Full HD — 1920×1080", "HD — 1280×720", "PAL SD — 720×576", "NTSC SD — 720×480",
                "Height 2160 (keep ratio)", "Height 1080 (keep ratio)", "Height 720 (keep ratio)", "Height 480 (keep ratio)", "Height 360 (keep ratio)", "Height 240 (keep ratio)",
                "Vertical 1080×1920 (Reels / Shorts / TikTok)", "Vertical 720×1280", "Square 1080×1080", "Portrait 4:5 — 1080×1350", "Cinema 2.39:1 — 1920×804", "Custom…" },
                new[] { 0, 7680, 4096, 3840, 2560, 2048, 1920, 1280, 720, 720, 0, 0, 0, 0, 0, 0, 1080, 720, 1080, 1080, 1920, -1 },
                new[] { 0, 4320, 2160, 2160, 1440, 1080, 1080, 720, 576, 480, 2160, 1080, 720, 480, 360, 240, 1920, 1280, 1080, 1350, 804, -1 });
            AddRow(secVideo, "Frame rate", cbFps);
            AddRow(secVideo, "Rotation", cbRotate);
            AddRow(secVideo, "Image → video length (s)", nuStill);
            AddRow(secVideo, null, chkSubs);
            stack.Controls.Add(secVideo);

            // Scale (Échelle)
            secScale = Section("SCALE  (ÉCHELLE)");
            chkScale = Check("Scale — upscale with a game-style upscaler");
            AddRow(secScale, null, chkScale);
            cbScaleType = Combo(Scaling.Types.Cast<object>().ToArray());
            cbScaleType.DropDownWidth = Theme.S(380);
            cbScaleFactor = Combo("×1.5", "×2", "×2.5", "×3", "×4", "Use the Resolution setting");
            cbScaleFactor.SelectedIndex = 1;
            tbScaleSharp = new TrackBar { Minimum = 0, Maximum = 100, Value = 100, TickStyle = TickStyle.None, BackColor = Theme.Panel, Dock = DockStyle.Fill, AutoSize = false, Height = Theme.S(28) };
            lblScaleSharpVal = new Label { Text = "100 %", AutoSize = false, Width = Theme.S(48), ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right };
            tbScaleSharp.ValueChanged += (s2, e2) => lblScaleSharpVal.Text = tbScaleSharp.Value + " %";
            var shHost = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            shHost.Controls.Add(tbScaleSharp); shHost.Controls.Add(lblScaleSharpVal);
            chkScaleOpt = Check("Optimized (GPU, high precision)");
            chkScaleOpt.Checked = true;
            chkScaleFid = Check("Screen quality encode (10-bit, finer CRF)");
            chkScaleFid.Checked = true;
            chkScale444 = Check("Full chroma 4:4:4 (same colour detail as the screen)");
            chkScale444.Checked = true;
            lblScaleInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(400), 0), Margin = new Padding(0, Theme.S(4), 0, 0) };
            scaleRows.Add(AddRow(secScale, "   Type", cbScaleType)); scaleRows.Add(cbScaleType);
            scaleRows.Add(AddRow(secScale, "   Factor", cbScaleFactor)); scaleRows.Add(cbScaleFactor);
            scaleRows.Add(AddRow(secScale, "   Sharpness", shHost)); scaleRows.Add(shHost);
            AddRow(secScale, null, chkScaleOpt); scaleRows.Add(chkScaleOpt);
            AddRow(secScale, null, chkScaleFid); scaleRows.Add(chkScaleFid);
            AddRow(secScale, null, chkScale444); scaleRows.Add(chkScale444);
            tips.SetToolTip(chkScaleFid, "Keeps what the upscaler adds: H.265 / AV1 / VP9 in 10-bit, CRF 4 steps finer,\nedge-preserving encoder tuning, and the final pixel conversion done by libplacebo.\nThis is what brings the file closest to a live Lossless Scaling picture.");
            tips.SetToolTip(chkScale444, "4:4:4 keeps full colour resolution (like the screen) for H.265 and VP9. Larger files;\nsome TVs and phones cannot play 4:4:4 — VLC, mpv and PCs can.");
            AddRow(secScale, null, lblScaleInfo); scaleRows.Add(lblScaleInfo);
            tips.SetToolTip(chkScaleOpt, "Optimized: GPU processing through FFmpeg's libplacebo (Vulkan) with blue-noise dithering —\nfaster and more precise. FSR, NIS, SGSR, Anime4K and EWA always run on the GPU.");
            tips.SetToolTip(cbScaleType, "FSR 1 (AMD), NIS (NVIDIA) and SGSR (Qualcomm) are open-source game upscalers ported to GLSL;\nAnime4K is made for anime. xBR / hqx / Integer / Nearest suit pixel art.");
            chkScale.CheckedChanged += (s2, e2) => UpdateScaleRows();
            cbScaleType.SelectedIndexChanged += (s2, e2) => UpdateScaleRows();
            chkScaleOpt.CheckedChanged += (s2, e2) => UpdateScaleRows();
            UpdateScaleRows();
            stack.Controls.Add(secScale);
            BuildSeedSection(stack);

            // Denoiser (Neat Video-style)
            secDn = Section("DENOISER  (NOISE REDUCTION + SHARPNESS)");
            chkDn = Check("Denoiser — remove noise and grain, keep the picture sharp");
            AddRow(secDn, null, chkDn);
            cbDnPreset = Combo(Denoise.Presets.Cast<object>().ToArray());
            cbDnPreset.DropDownWidth = Theme.S(380);
            cbDnPreset.SelectedIndex = 0;
            cbDnEngine = Combo(Denoise.Engines.Cast<object>().ToArray());
            cbDnEngine.DropDownWidth = Theme.S(380);
            cbDnEngine.SelectedIndex = 0;
            Panel dsHost, shpHost;
            tbDnStrength = SliderRow(0, 200, 100, out lblDnStrengthVal, out dsHost);
            tbDnSharp = SliderRow(0, 100, 20, out lblDnSharpVal, out shpHost);
            chkDnTemporal = Check("Temporal (uses the previous and next frames)");
            chkDnTemporal.Checked = true;
            chkDnAuto = Check("Auto noise profile (measures the noise of each video)");
            chkDnAuto.Checked = true;
            lblDnInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(400), 0), Margin = new Padding(0, Theme.S(4), 0, 0) };
            dnRows.Add(AddRow(secDn, "   Preset", cbDnPreset)); dnRows.Add(cbDnPreset);
            dnRows.Add(AddRow(secDn, "   Quality", cbDnEngine)); dnRows.Add(cbDnEngine);
            dnRows.Add(AddRow(secDn, "   Strength", dsHost)); dnRows.Add(dsHost);
            dnRows.Add(AddRow(secDn, "   Sharpness", shpHost)); dnRows.Add(shpHost);
            AddRow(secDn, null, chkDnTemporal); dnRows.Add(chkDnTemporal);
            AddRow(secDn, null, chkDnAuto); dnRows.Add(chkDnAuto);
            AddRow(secDn, null, lblDnInfo); dnRows.Add(lblDnInfo);
            tips.SetToolTip(chkDn, "Noise reduction in the style of Neat Video: the noise of each video is measured (luma and colour),\nthen a temporal 3D FFT filter removes it, a little texture is put back so faces and skin stay natural,\nand luma sharpening restores the crispness. Runs before Scale.");
            tips.SetToolTip(cbDnPreset, "Light keeps a natural texture; Strong / Very strong for very noisy footage;\nFilm grain cleans but keeps the grain; Low light and VHS add a strong colour-noise pass.");
            tips.SetToolTip(cbDnEngine, "Balanced: 3D FFT over 3 frames (good quality, moderate speed).\nHigh quality: + non-local means (slower, a bit cleaner). Fast: hqdn3d, real time.");
            tips.SetToolTip(chkDnAuto, "Measures the noise of each file on 4 frames, like Neat Video's noise profile,\nso the same preset works on clean and very noisy videos.");
            chkDn.CheckedChanged += (s2, e2) => UpdateDnRows();
            cbDnPreset.SelectedIndexChanged += (s2, e2) => { var pr = cbDnPreset.SelectedItem as Denoise.Preset; if (pr != null && !loadingSettings) tbDnSharp.Value = pr.Sharpen; UpdateDnRows(); };
            UpdateDnRows();
            stack.Controls.Add(secDn);

            // OptiScaler (open source, GPL-3.0): output scaling + RCAS
            secOpti = Section("OPTISCALER  (OUTPUT SCALING + RCAS)");
            chkOpti = Check("OptiScaler — supersampled upscaling for a cleaner render");
            AddRow(secOpti, null, chkOpti);
            cbOptiMult = Combo("×1.25", "×1.5  (OptiScaler default)", "×2", "×2.5", "×3");
            cbOptiMult.SelectedIndex = 1;
            cbOptiKernel = Combo(Scaling.OptiKernels.Cast<object>().ToArray());
            cbOptiKernel.SelectedIndex = 2;
            tbOptiSharp = new TrackBar { Minimum = 0, Maximum = 100, Value = 40, TickStyle = TickStyle.None, BackColor = Theme.Panel, Dock = DockStyle.Fill, AutoSize = false, Height = Theme.S(28) };
            lblOptiSharpVal = new Label { Text = "40 %", AutoSize = false, Width = Theme.S(48), ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right };
            tbOptiSharp.ValueChanged += (s2, e2) => lblOptiSharpVal.Text = tbOptiSharp.Value + " %";
            var osHost = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            osHost.Controls.Add(tbOptiSharp); osHost.Controls.Add(lblOptiSharpVal);
            chkOptiContrast = Check("Contrast adaptive RCAS");
            lblOptiInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(400), 0), Margin = new Padding(0, Theme.S(4), 0, 0) };
            optiRows.Add(AddRow(secOpti, "   Output scaling", cbOptiMult)); optiRows.Add(cbOptiMult);
            optiRows.Add(AddRow(secOpti, "   Downscaler", cbOptiKernel)); optiRows.Add(cbOptiKernel);
            optiRows.Add(AddRow(secOpti, "   RCAS sharpness", osHost)); optiRows.Add(osHost);
            AddRow(secOpti, null, chkOptiContrast); optiRows.Add(chkOptiContrast);
            AddRow(secOpti, null, lblOptiInfo); optiRows.Add(lblOptiInfo);
            tips.SetToolTip(chkOpti, "OptiScaler's Output Scaling: the upscaler (Scale type, or EWA Lanczos Sharp when Scale is off)\nrenders the picture larger than the output, RCAS sharpens it, then a high-quality\ndownscaler brings it to the output size — finer edges, less aliasing and ringing.\nGPU (libplacebo / Vulkan). Works for videos and images.");
            tips.SetToolTip(cbOptiMult, "Supersampling multiplier: the upscaler renders at output size × this value (OptiScaler: 0.5–3.0, default 1.5).");
            tips.SetToolTip(cbOptiKernel, "Downscaler used by OptiScaler's Output Scaling.\nLanczos 2 / Bicubic: sharpest.  Kaiser: less ringing.  Magic Kernel: smoothest, no aliasing.");
            tips.SetToolTip(chkOptiContrast, "OptiScaler's contrast adaptation: sharpens less on already high-contrast edges.");
            chkOpti.CheckedChanged += (s2, e2) => UpdateOptiRows();
            UpdateOptiRows();
            stack.Controls.Add(secOpti);

            // NVIDIA RTX AI
            secRtx = Section("NVIDIA RTX AI  (RTX GPU)");
            lblRtxStatus = new Label { Text = "Detecting RTX engines…", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(400), 0), Margin = new Padding(0, 0, 0, Theme.S(6)) };
            AddRow(secRtx, null, lblRtxStatus);
            cbRtxEngine = Combo("Auto (best available)", "FFmpeg RTX filters only", "NVIDIA driver (built-in)", "RTXVideoProcessor.exe only");
            cbRtxEngine.SelectedIndexChanged += (s2, e2) => UpdateRtxStatus();
            AddRow(secRtx, "Engine", cbRtxEngine);
            var bProc = Btn("Locate RTXVideoProcessor.exe…", 0); bProc.Dock = DockStyle.Fill; bProc.Height = Theme.S(30); bProc.Font = fSmall;
            bProc.Click += (s2, e2) => LocateRtxProcessor();
            AddRow(secRtx, null, bProc);

            chkVsr = Check("RTX Video Super Resolution (AI upscale + cleanup)");
            AddRow(secRtx, null, chkVsr);
            cbVsrQ = Combo("1 — Low", "2 — Medium", "3 — High", "4 — Ultra");
            cbVsrQ.SelectedIndex = 3;
            cbVsrScale = Combo("×2", "×3", "×4");
            btnVsrParams = Btn("All VSR filter parameters…", 0); btnVsrParams.Dock = DockStyle.Fill; btnVsrParams.Height = Theme.S(30); btnVsrParams.Font = fSmall;
            btnVsrParams.Click += (s2, e2) => EditRtxParams(Rtx.VsrFilters, ref vsrExpr, btnVsrParams, "VSR");
            vsrRows.Add(AddRow(secRtx, "   VSR quality", cbVsrQ)); vsrRows.Add(cbVsrQ);
            vsrRows.Add(AddRow(secRtx, "   Upscale factor", cbVsrScale)); vsrRows.Add(cbVsrScale);
            AddRow(secRtx, null, btnVsrParams); vsrRows.Add(btnVsrParams);

            chkHdr = Check("RTX TrueHDR (SDR → HDR10)");
            AddRow(secRtx, null, chkHdr);
            nuHdrContrast = Num(0, 200, 125, 0); nuHdrSat = Num(0, 200, 100, 0); nuHdrGray = Num(10, 100, 25, 0); nuHdrLum = Num(400, 10000, 1000, 0);
            nuHdrLum.Increment = 50;
            btnHdrParams = Btn("All TrueHDR filter parameters…", 0); btnHdrParams.Dock = DockStyle.Fill; btnHdrParams.Height = Theme.S(30); btnHdrParams.Font = fSmall;
            btnHdrParams.Click += (s2, e2) => EditRtxParams(Rtx.HdrFilters, ref hdrExpr, btnHdrParams, "TrueHDR");
            hdrRows.Add(AddRow(secRtx, "   Contrast", nuHdrContrast)); hdrRows.Add(nuHdrContrast);
            hdrRows.Add(AddRow(secRtx, "   Saturation", nuHdrSat)); hdrRows.Add(nuHdrSat);
            hdrRows.Add(AddRow(secRtx, "   Middle gray", nuHdrGray)); hdrRows.Add(nuHdrGray);
            hdrRows.Add(AddRow(secRtx, "   Peak brightness (nits)", nuHdrLum)); hdrRows.Add(nuHdrLum);
            AddRow(secRtx, null, btnHdrParams); hdrRows.Add(btnHdrParams);

            chkDlss = Check("DLSS Super Resolution (NVIDIA NGX AI)");
            AddRow(secRtx, null, chkDlss);
            cbDlssMode = Combo("DLAA — native size, AI clean-up (×1)", "Quality (×1.5)", "Balanced (×1.72)", "Performance (×2)", "Ultra Performance (×3)");
            cbDlssMode.SelectedIndex = 3;
            btnDlssParams = Btn("All DLSS / NGX filter parameters…", 0); btnDlssParams.Dock = DockStyle.Fill; btnDlssParams.Height = Theme.S(30); btnDlssParams.Font = fSmall;
            btnDlssParams.Click += (s2, e2) => EditRtxParams(Rtx.DlssFilters, ref dlssExpr, btnDlssParams, "DLSS");
            dlssRows.Add(AddRow(secRtx, "   DLSS mode", cbDlssMode)); dlssRows.Add(cbDlssMode);
            AddRow(secRtx, null, btnDlssParams); dlssRows.Add(btnDlssParams);

            var rtxHint = new Label { Text = "Needs an NVIDIA RTX GPU and a recent driver. TrueHDR needs a 10-bit codec (H.265, AV1, VP9, ProRes…). RTXVideoProcessor: VSR ×2 + TrueHDR, NVENC output, MP4/MKV/MOV.", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(400), 0), Margin = new Padding(0, Theme.S(6), 0, 0) };
            AddRow(secRtx, null, rtxHint);
            EventHandler rtxToggle = (s2, e2) => UpdateRtxRows();
            chkVsr.CheckedChanged += rtxToggle; chkHdr.CheckedChanged += rtxToggle; chkDlss.CheckedChanged += rtxToggle;
            UpdateRtxRows();
            stack.Controls.Add(secRtx);

            // Audio
            secAudio = Section("AUDIO SETTINGS");
            cbACodec = Combo(Formats.AudioCodecsForVideo.Cast<object>().ToArray());
            SkipSeparator(cbACodec);
            cbAKbps = Combo("Auto", "64", "96", "128", "160", "192", "256", "320", "448", "640");
            cbRate = Combo("Original", "8000 Hz", "16000 Hz", "22050 Hz", "32000 Hz", "44100 Hz", "48000 Hz", "88200 Hz", "96000 Hz", "192000 Hz");
            cbCh = Combo(ChannelLayouts.All.Select(l => (object)new Choice(l.Key, l.Label)).ToArray());
            cbCh.DropDownWidth = Theme.S(380);
            tips.SetToolTip(cbCh, "Immersive layouts (5.1.2 … 9.1.6) are written as discrete channel beds: FFmpeg cannot encode Dolby Atmos objects or DTS:X metadata.\nUse PCM 24-bit (WAV/W64/CAF/MOV/MKV) or Opus for more than 8 channels.\n\"Passthrough\" copies an existing Atmos / DTS:X track bit-for-bit.\n\"Spatial Audio — headphones\" renders the immersive 7.1.4 mix in 3D for any headphones (stereo file), like Apple Music's Spatial Audio.");
            nuVolume = Num(0, 1000, 100, 0); nuVolume.Increment = 10;
            chkNorm = Check("Normalize loudness (EBU R128)");
            lblACodec = AddRow(secAudio, "Audio codec", cbACodec);
            AddRow(secAudio, "Bitrate (kbps)", cbAKbps);
            AddRow(secAudio, "Sample rate", cbRate);
            AddRow(secAudio, "Channels", cbCh);
            cbUpmix = Combo("Auto — Spatial Music (audio) / Cinema (video)",
                            "Spatial Music — Dolby Atmos music mix (Apple Music style)",
                            "Music (classic) — vocals forward, instruments spread, air above",
                            "Cinema — dialogue locked to center, ambience overhead",
                            "Live concert — big room, audience all around",
                            "Off — keep the extra channels silent");
            cbUpmix.DropDownWidth = Theme.S(380);
            tips.SetToolTip(cbUpmix, "Immersive upmix (when the source has fewer channels than the chosen layout):\n" +
                "• lead vocal / dialogue (centered sound) is steered to the center speaker, closer to you\n" +
                "• instruments keep their stereo position across front, wides and sides\n" +
                "• bass and kick drum feed the LFE (subwoofer)\n" +
                "• stereo width, reverb, room and cymbals feed the wides and the height speakers (the voice never goes up)\n" +
                "Spatial Music: dry lead vocal in the center, instruments opened up to the sides and wides,\n" +
                "  cymbals and air above, and a separate (decorrelated) reverb in every surround / height speaker — the\n" +
                "  enveloping sound of the Dolby Atmos music mixes on Apple Music.\n" +
                "Channel-based mix: not Dolby Atmos objects.\n" +
                "On headphones, choose Channels → \"Spatial Audio — headphones\" to hear it in 3D.");
            lblUpmix = AddRow(secAudio, "Immersive upmix", cbUpmix);
            cbCh.SelectedIndexChanged += (s2, e2) => UpdateUpmixRow();
            AddRow(secAudio, "Volume (%)", nuVolume);
            AddRow(secAudio, null, chkNorm);
            stack.Controls.Add(secAudio);

            // Image
            secImage = Section("IMAGE SETTINGS");
            tbImgQ = new TrackBar { Minimum = 1, Maximum = 100, Value = 90, TickStyle = TickStyle.None, BackColor = Theme.Panel, Dock = DockStyle.Fill, AutoSize = false, Height = Theme.S(28) };
            lblImgQ = new Label { Text = "90", AutoSize = false, Width = Theme.S(36), ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right };
            tbImgQ.ValueChanged += (s, e) => lblImgQ.Text = tbImgQ.Value.ToString();
            var qHost = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            qHost.Controls.Add(tbImgQ); qHost.Controls.Add(lblImgQ);
            cbImgRotate = Combo("None", "90° clockwise", "180°", "90° counter-clockwise", "Mirror horizontal", "Mirror vertical");
            cbExtract = Combo("Single frame", "Every frame", "One frame every N s");
            cbExtract.SelectedIndexChanged += (s, e) => UpdateExtractLabel();
            nuExtractVal = Num(0, 360000, 1, 2);
            resImage = MakeRes(secImage, new[] {
                "Original", "Width 7680", "Width 3840", "Width 2560", "Width 1920", "Width 1280", "Width 1024", "Width 800", "Width 640", "Width 512",
                "Width 256", "Width 128", "Width 64", "Width 32", "Width 16", "1920×1080", "1080×1920", "1080×1080", "512×512 (icons)", "256×256 (icons)", "Custom…" },
                new[] { 0, 7680, 3840, 2560, 1920, 1280, 1024, 800, 640, 512, 256, 128, 64, 32, 16, 1920, 1080, 1080, 512, 256, -1 },
                new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1080, 1920, 1080, 512, 256, -1 });
            AddRow(secImage, "Quality", qHost);
            AddRow(secImage, "Rotation", cbImgRotate);
            AddRow(secImage, "From a video", cbExtract);
            lblExtractVal = AddRow(secImage, "Position (s)", nuExtractVal);
            stack.Controls.Add(secImage);

            // Filters
            secFilters = Section("FILTERS");
            filterTitle = (Label)secFilters.Controls[0];
            filterList = new CheckedListBox { Dock = DockStyle.Fill, Height = Theme.S(118), BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = fMono, CheckOnClick = false, IntegralHeight = false, HorizontalScrollbar = true, Margin = new Padding(0, Theme.S(2), 0, Theme.S(4)) };
            filterList.ItemCheck += (s, e) => { if (e.Index >= 0 && e.Index < filters.Count) filters[e.Index].Enabled = e.NewValue == CheckState.Checked; };
            filterList.DoubleClick += (s, e) => EditFilter();
            filterList.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveFilter(); };
            AddRow(secFilters, null, filterList);
            var fb = new TableLayoutPanel { Dock = DockStyle.Fill, Height = Theme.S(36), ColumnCount = 6, Margin = new Padding(0, 0, 0, Theme.S(4)) };
            foreach (var w in new[] { 34f, 26f, 13f, 9f, 9f, 9f }) fb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, w));
            var bAdd = Btn("+ Add filter…", 0); bAdd.Primary = true; bAdd.Click += (s, e) => AddFilter();
            btnPresets = Btn("Presets ▾", 0); btnPresets.Click += (s, e) => ShowPresets();
            var bEdit = Btn("Edit", 0); bEdit.Click += (s, e) => EditFilter();
            var bDel = Btn("✕", 0); bDel.Click += (s, e) => RemoveFilter();
            var bUp = Btn("▲", 0); bUp.Click += (s, e) => MoveFilter(-1);
            var bDn = Btn("▼", 0); bDn.Click += (s, e) => MoveFilter(1);
            tips.SetToolTip(bDel, "Remove the selected filter"); tips.SetToolTip(bUp, "Move up"); tips.SetToolTip(bDn, "Move down");
            int col = 0;
            foreach (var b in new[] { bAdd, btnPresets, bEdit, bDel, bUp, bDn }) { b.Dock = DockStyle.Fill; b.Height = Theme.S(32); b.Margin = new Padding(0, 0, Theme.S(4), 0); fb.Controls.Add(b, col++, 0); }
            AddRow(secFilters, null, fb);
            var hint = new Label { Text = "Filters run in list order. Uncheck to disable temporarily. Double-click to edit.", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(400), 0), Margin = new Padding(0, 0, 0, Theme.S(6)) };
            AddRow(secFilters, null, hint);
            txtExtraInputs = Txt("");
            SetCue(txtExtraInputs, "logo.png ; music.mp3 …  ([1:v], [2:a]…)");
            var bExtra = Btn("…", 40); bExtra.Dock = DockStyle.Right; bExtra.Height = Theme.S(28);
            bExtra.Click += (s, e) => BrowseExtraInputs();
            var exHost = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            txtExtraInputs.Dock = DockStyle.Fill; exHost.Controls.Add(txtExtraInputs); exHost.Controls.Add(bExtra);
            AddRow(secFilters, "Extra inputs", exHost);
            tips.SetToolTip(txtExtraInputs, "Additional files given to FFmpeg as inputs #1, #2… (separate with ;).\nNeeded by multi-input filters such as overlay (watermark), amix, hstack, concat…");
            var gl = new Label { Text = "Filtergraph  (-filter_complex, advanced)", ForeColor = Theme.Muted, Font = fUI, AutoSize = true, Margin = new Padding(0, Theme.S(6), 0, Theme.S(2)) };
            AddRow(secFilters, null, gl);
            txtGraph = new TextBox { Multiline = true, Height = Theme.S(64), Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(12, 13, 17), ForeColor = Color.FromArgb(255, 200, 140), BorderStyle = BorderStyle.FixedSingle, Font = fMono, Margin = new Padding(0, 0, 0, Theme.S(2)) };
            tips.SetToolTip(txtGraph, "Full FFmpeg filtergraph. Inputs: [0:v] [0:a] (main file), [1:v] [1:a] (extra inputs)…\nName your final outputs [vout] and/or [aout] — they are mapped automatically.\nExample (watermark):  [0:v][1:v]overlay=W-w-20:H-h-20[vout]");
            AddRow(secFilters, null, txtGraph);
            var gh = new Label { Text = "Example: [0:v][1:v]overlay=W-w-20:H-h-20[vout]   — end outputs with [vout] / [aout].", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(400), 0) };
            AddRow(secFilters, null, gh);
            stack.Controls.Add(secFilters);

            // Trim
            secTrim = Section("TRIM (OPTIONAL)");
            txtStart = Txt(""); txtEnd = Txt("");
            SetCue(txtStart, "e.g. 00:01:30 or 90"); SetCue(txtEnd, "empty = until the end");
            AddRow(secTrim, "Start", txtStart);
            AddRow(secTrim, "End", txtEnd);
            stack.Controls.Add(secTrim);

            // Output
            secOut = Section("DESTINATION");
            chkSameDir = Check("Same folder as the source file");
            chkSameDir.CheckedChanged += (s, e) => { txtOutDir.Enabled = btnBrowseOut.Enabled = !chkSameDir.Checked; };
            txtOutDir = Txt("");
            btnBrowseOut = Btn("…", 40); btnBrowseOut.Dock = DockStyle.Right; btnBrowseOut.Height = Theme.S(28);
            btnBrowseOut.Click += (s, e) => BrowseOutDir();
            var outHost = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            txtOutDir.Dock = DockStyle.Fill; outHost.Controls.Add(txtOutDir); outHost.Controls.Add(btnBrowseOut);
            chkOverwrite = Check("Overwrite existing files");
            txtCustom = Txt(""); SetCue(txtCustom, "e.g. -metadata title=\"My movie\"");
            AddRow(secOut, null, chkSameDir);
            AddRow(secOut, "Folder", outHost);
            AddRow(secOut, null, chkOverwrite);
            AddRow(secOut, "Extra FFmpeg arguments", txtCustom);
            stack.Controls.Add(secOut);

            right.Controls.Add(stack);
            var sep = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = Theme.Border };

            // ---------- footer ----------
            var footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(74), BackColor = Theme.Panel, Padding = new Padding(Theme.S(18), Theme.S(14), Theme.S(18), Theme.S(14)) };
            var fRight = new FlowLayoutPanel { Dock = DockStyle.Right, Width = Theme.S(520), FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Theme.Panel };
            btnConvert = Btn("CONVERT", 210); btnConvert.Primary = true; btnConvert.Height = Theme.S(46);
            btnConvert.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
            btnConvert.Click += (s, e) => StartConversion();
            btnCancel = Btn("Cancel", 110); btnCancel.Height = Theme.S(46); btnCancel.Enabled = false;
            btnCancel.Click += (s, e) => { cancelRequested = true; Log("⏹  Cancelling…", Theme.Error); };
            btnOpenOut = Btn("Open folder", 150); btnOpenOut.Height = Theme.S(46);
            btnOpenOut.Click += (s, e) => OpenOutputFolder();
            foreach (var b in new[] { btnConvert, btnCancel, btnOpenOut }) { b.Margin = new Padding(Theme.S(10), 0, 0, 0); fRight.Controls.Add(b); }
            var fLeft = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(0, 0, Theme.S(20), 0) };
            statusLbl = new Label { Dock = DockStyle.Top, Height = Theme.S(24), ForeColor = Theme.Text, Text = "Ready.", Font = fUI };
            overall = new GradientBar { Dock = DockStyle.Top, Height = Theme.S(10) };
            fLeft.Controls.Add(overall); fLeft.Controls.Add(statusLbl);
            footer.Controls.Add(fLeft); footer.Controls.Add(fRight);
            fLeft.BringToFront();

            // ---------- header ----------
            header = new Header { Dock = DockStyle.Top };
            var bFF = Btn("FFmpeg location…", 0); bFF.Width = Theme.S(140); bFF.Height = Theme.S(32);
            bFF.Font = fSmall;
            bFF.Location = new Point(header.Width - bFF.Width - Theme.S(18), (header.Height - bFF.Height) / 2);
            header.Resize += (s, e) => bFF.Location = new Point(header.Width - bFF.Width - Theme.S(18), (header.Height - bFF.Height) / 2);
            bFF.Click += (s, e) => ChooseFFmpeg();
            header.Controls.Add(bFF);

            Controls.Add(center);
            Controls.Add(sep);
            Controls.Add(right);
            Controls.Add(footer);
            Controls.Add(header);
            center.BringToFront();
            ResumeLayout(true);
        }

        // ---------- control factories ----------
        FlatBtn Btn(string text, int w)
        {
            var b = new FlatBtn(text);
            if (w > 0) b.Width = Theme.S(w);
            b.Height = Theme.S(36);
            return b;
        }

        ComboBox Combo(params object[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Card, ForeColor = Theme.Text, Font = fUI, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)), IntegralHeight = false, MaxDropDownItems = 18 };
            c.Items.AddRange(items);
            if (c.Items.Count > 0) c.SelectedIndex = 0;
            return c;
        }

        NumericUpDown Num(decimal min, decimal max, decimal val, int dec)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = val, DecimalPlaces = dec, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)), Font = fUI };
        }

        TextBox Txt(string t)
        {
            return new TextBox { Text = t, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)), Font = fUI };
        }

        CheckBox Check(string t)
        {
            return new CheckBox { Text = t, ForeColor = Theme.Text, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, Theme.S(5), 0, Theme.S(3)), Font = fUI };
        }

        Label SectionTitle(string t)
        {
            return new Label { Text = t, AutoSize = true, ForeColor = Theme.Accent1, Font = fSection, Margin = new Padding(0, Theme.S(6), 0, Theme.S(8)) };
        }

        TableLayoutPanel Section(string title)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, BackColor = Theme.Panel, Margin = new Padding(0, Theme.S(14), 0, 0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            var l = SectionTitle(title);
            t.Controls.Add(l, 0, 0);
            t.SetColumnSpan(l, 2);
            var line = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Theme.Border, Margin = new Padding(0, 0, 0, Theme.S(6)) };
            t.Controls.Add(line, 0, 1);
            t.SetColumnSpan(line, 2);
            t.RowCount = 2;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return t;
        }

        Label AddRow(TableLayoutPanel t, string label, Control c)
        {
            int row = t.RowCount;
            t.RowCount = row + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (label == null)
            {
                t.Controls.Add(c, 0, row);
                t.SetColumnSpan(c, 2);
                return null;
            }
            var l = new Label { Text = label, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Font = fUI, Margin = new Padding(0, Theme.S(3), Theme.S(8), Theme.S(3)), Height = Theme.S(30) };
            t.Controls.Add(l, 0, row);
            t.Controls.Add(c, 1, row);
            return l;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, string lp);
        static void SetCue(TextBox t, string cue)
        {
            t.HandleCreated += (s, e) => { try { SendMessage(t.Handle, 0x1501, (IntPtr)1, cue); } catch { } };
        }

        ContextMenuStrip DarkMenu()
        {
            var m = new ContextMenuStrip { BackColor = Theme.Card, ForeColor = Theme.Text, ShowImageMargin = false, Font = fUI };
            m.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            return m;
        }

        ContextMenuStrip BuildContextMenu()
        {
            var m = DarkMenu();
            m.Items.Add("Open converted file", null, (s, e) => OpenSelectedOutput());
            m.Items.Add("Open source file", null, (s, e) => { var j = SelectedJob(); if (j != null) Shell(j.Path); });
            m.Items.Add("Show in Explorer", null, (s, e) => { var j = SelectedJob(); if (j != null) Shell("explorer.exe", "/select,\"" + (File.Exists(j.Output ?? "") ? j.Output : j.Path) + "\""); });
            m.Items.Add("Show FFmpeg command", null, (s, e) => PreviewCommand());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Remove from list", null, (s, e) => RemoveSelected());
            return m;
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Theme.CardHover; } }
            public override Color MenuItemBorder { get { return Theme.Border; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Border; } }
        }

        // =====================================================================
        //  List drawing
        // =====================================================================
        void List_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var r = e.Bounds; r.X += Theme.S(8); r.Width -= Theme.S(8);
            TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), fSection, r, Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        void List_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            var job = e.Item.Tag as Job;
            var g = e.Graphics;
            Color bg = e.Item.Selected ? Theme.Selection : (e.ItemIndex % 2 == 0 ? Theme.Panel : Color.FromArgb(26, 29, 37));
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, e.Bounds);
            if (e.Item.Selected && e.ColumnIndex == 0)
                using (var a = Theme.AccentBrush(new RectangleF(e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height))) g.FillRectangle(a, e.Bounds.X, e.Bounds.Y, Theme.S(3), e.Bounds.Height);
            var r = e.Bounds; r.X += Theme.S(8); r.Width -= Theme.S(10);

            if (e.ColumnIndex == 5 && job != null)
            {
                if (job.Status == StConverting)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var br = new RectangleF(r.X, r.Y + r.Height / 2f - Theme.S(4), r.Width - Theme.S(46), Theme.S(8));
                    using (var p = Theme.Round(br, Theme.S(4))) using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                    if (job.Progress > 0)
                    {
                        var fr = new RectangleF(br.X, br.Y, Math.Max(br.Height, (float)(br.Width * job.Progress)), br.Height);
                        using (var p = Theme.Round(fr, Theme.S(4))) using (var b = Theme.AccentBrush(br)) g.FillPath(b, p);
                    }
                    var tr = new Rectangle((int)br.Right + Theme.S(4), r.Y, Theme.S(44), r.Height);
                    TextRenderer.DrawText(g, (int)(job.Progress * 100) + " %", fSmall, tr, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                    return;
                }
                Color c = Theme.Muted; string t = job.Status;
                if (t == StDone) { c = Theme.Success; t = "✓  Done"; }
                else if (t == StError) { c = Theme.Error; t = "✗  Error"; }
                else if (t == StCancelled) { c = Theme.Accent1; }
                TextRenderer.DrawText(g, t, fUI, r, c, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                return;
            }
            if (e.ColumnIndex == 1 && job != null)
            {
                Color kc = job.Kind == MediaKind.Video ? Color.FromArgb(120, 170, 255) : job.Kind == MediaKind.Audio ? Color.FromArgb(190, 140, 255) : job.Kind == MediaKind.Image ? Color.FromArgb(90, 220, 200) : Theme.Muted;
                TextRenderer.DrawText(g, e.SubItem.Text, fSection, r, kc, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                return;
            }
            TextRenderer.DrawText(g, e.SubItem.Text, fUI, r, e.ColumnIndex == 0 ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        void FitColumns()
        {
            if (list.Columns.Count < 6) return;
            int others = 0;
            for (int i = 1; i < 6; i++) others += list.Columns[i].Width;
            int w = list.ClientSize.Width - others - 2;
            if (w > Theme.S(160)) list.Columns[0].Width = w;
        }

        // =====================================================================
        //  Category & format
        // =====================================================================
        void SetCategory(MediaKind k)
        {
            category = k;
            catVideo.Toggled = k == MediaKind.Video;
            catAudio.Toggled = k == MediaKind.Audio;
            catImage.Toggled = k == MediaKind.Image;
            cbFormat.BeginUpdate();
            cbFormat.Items.Clear();
            var fl = Formats.Of(k);
            var dyn = Formats.DynamicOf(k);
            foreach (var f in fl) cbFormat.Items.Add(f);
            if (dyn.Count > 0)
            {
                cbFormat.Items.Add(new Separator("──────  ALL " + dyn.Count + " FORMATS OF YOUR FFMPEG BUILD  ──────"));
                foreach (var f in dyn) cbFormat.Items.Add(f);
            }
            cbFormat.EndUpdate();
            string last;
            int idx = 0;
            if (settings.TryGetValue("format_" + k, out last))
                for (int i = 0; i < cbFormat.Items.Count; i++) { var of = cbFormat.Items[i] as OutFormat; if (of != null && of.Label == last) idx = i; }
            cbFormat.SelectedIndex = idx;
            string kn = k == MediaKind.Video ? "video" : k == MediaKind.Audio ? "audio" : "image";
            formatHint.Text = dyn.Count > 0
                ? (fl.Count + dyn.Count) + " " + kn + " formats  (" + fl.Count + " tuned presets + " + dyn.Count + " from your FFmpeg build)"
                : fl.Count + " " + kn + " formats" + (FormatCatalog.Loaded ? "" : "  — reading every FFmpeg format…");
            secVideo.Visible = k == MediaKind.Video;
            secRtx.Visible = k == MediaKind.Video;
            secScale.Visible = k != MediaKind.Audio;
            secSeed.Visible = k != MediaKind.Audio;
            UpdateSeedRows();
            secOpti.Visible = k != MediaKind.Audio;
            secDn.Visible = k != MediaKind.Audio;
            secAudio.Visible = k != MediaKind.Image;
            secImage.Visible = k == MediaKind.Image;
            OnFormatChanged();
        }

        OutFormat CurrentFormat { get { return cbFormat.SelectedItem as OutFormat; } }

        void OnFormatChanged()
        {
            if (cbFormat.SelectedItem is Separator)
            {
                int i = cbFormat.SelectedIndex;
                cbFormat.SelectedIndex = i + 1 < cbFormat.Items.Count ? i + 1 : Math.Max(0, i - 1);
                return;
            }
            var f = CurrentFormat;
            if (f == null) return;
            settings["format_" + category] = f.Label;
            if (f.Kind == MediaKind.Video)
            {
                bool special = f.VCodec == "gif" || f.VCodec == "apng" || f.VCodec == "webpanim";
                cbVCodec.Enabled = !special;
                cbACodec.Enabled = !special;
                nuVBitrate.Enabled = !special;
                cbPreset.Enabled = !special;
                cbVCodec.SelectedIndex = 0;
                cbACodec.SelectedIndex = 0;
            }
            if (f.Kind == MediaKind.Audio)
            {
                bool lossless = f.ACodec == "flac" || f.ACodec == "alac" || f.ACodec.StartsWith("pcm") || f.ACodec.StartsWith("codec:pcm") || f.ACodec == "wavpack" || f.ACodec == "tta" || f.ACodec == "amr";
                cbAKbps.Enabled = !lossless;
                cbACodec.Enabled = true;
                cbACodec.SelectedIndex = 0;
            }
            else cbAKbps.Enabled = true;
            if (f.Kind == MediaKind.Image)
            {
                bool q = f.VCodec == "mjpeg" || f.VCodec == "webp" || f.VCodec == "avif" || f.VCodec == "jxl" || f.Dynamic;
                tbImgQ.Enabled = q;
            }
            UpdateNvCrfRow();
            btnConvert.Text = "CONVERT TO " + f.Ext.ToUpperInvariant();
        }

        void UpdateExtractLabel()
        {
            int m = cbExtract.SelectedIndex;
            lblExtractVal.Text = m == 0 ? "Position (s)" : m == 2 ? "Interval (s)" : "—";
            nuExtractVal.Enabled = m != 1;
        }

        // =====================================================================
        //  Filters
        // =====================================================================
        void RefreshFilterList(int select)
        {
            filterList.BeginUpdate();
            filterList.Items.Clear();
            foreach (var f in filters) filterList.Items.Add(f, f.Enabled);
            filterList.EndUpdate();
            if (select >= 0 && select < filterList.Items.Count) filterList.SelectedIndex = select;
            int on = filters.Count(f => f.Enabled);
            filterTitle.Text = "FILTERS" + (filters.Count > 0 ? "   (" + on + " active)" : "") +
                (FilterCatalog.All.Count > 0 ? "   ·   " + FilterCatalog.All.Count + " available" : "");
        }

        bool EnsureCatalog()
        {
            if (string.IsNullOrEmpty(Runner.FFmpeg)) { ChooseFFmpeg(); if (string.IsNullOrEmpty(Runner.FFmpeg)) return false; }
            if (!FilterCatalog.Loaded || FilterCatalog.All.Count == 0)
            {
                Cursor = Cursors.WaitCursor;
                FilterCatalog.Load();
                Cursor = Cursors.Default;
                OnCatalogLoaded();
            }
            return true;
        }

        void OnCatalogLoaded()
        {
            string rp; settings.TryGetValue("rtxproc", out rp);
            Rtx.FindProcessor(rp);
            UpdateRtxStatus();
            UpdateScaleRows(); UpdateOptiRows();
            header.FilterCount = FilterCatalog.All.Count;
            header.Invalidate();
            RefreshFilterList(filterList.SelectedIndex);
            if (FilterCatalog.All.Count > 0) Log("Filter library: " + FilterCatalog.All.Count + " filters found in your FFmpeg build.", Theme.Muted);
            else Log("Could not read the filter list: " + FilterCatalog.LoadError, Theme.Error);
        }

        void AddFilter()
        {
            if (!EnsureCatalog()) return;
            using (var d = new FilterBrowser(null))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (d.GraphText != null) { AppendGraph(d.GraphText); return; }
                if (d.Result == null) return;
                filters.Add(d.Result);
                RefreshFilterList(filters.Count - 1);
                Log("+ " + d.Result, Theme.Text);
            }
        }

        void EditFilter()
        {
            int i = filterList.SelectedIndex;
            if (i < 0 || i >= filters.Count) { AddFilter(); return; }
            if (!EnsureCatalog()) return;
            using (var d = new FilterBrowser(filters[i]))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (d.GraphText != null) { AppendGraph(d.GraphText); return; }
                if (d.Result == null) return;
                filters[i] = d.Result;
                RefreshFilterList(i);
            }
        }

        void AppendGraph(string g)
        {
            string cur = txtGraph.Text.Trim().TrimEnd(';');
            txtGraph.Text = cur.Length == 0 ? g : cur + ";\r\n" + g;
            Log("Inserted into filtergraph: " + g, Theme.Text);
            if (!g.Contains("[vout]") && !g.Contains("[aout]"))
                Log("   Tip: label the final output of your filtergraph [vout] (video) or [aout] (audio).", Theme.Accent1);
            if (g.Contains("[1:") && txtExtraInputs.Text.Trim().Length == 0)
                Log("   Tip: this filter needs a second input — add a file in “Extra inputs”.", Theme.Accent1);
        }

        void RemoveFilter()
        {
            int i = filterList.SelectedIndex;
            if (i < 0 || i >= filters.Count) return;
            filters.RemoveAt(i);
            RefreshFilterList(Math.Min(i, filters.Count - 1));
        }

        void MoveFilter(int d)
        {
            int i = filterList.SelectedIndex, j = i + d;
            if (i < 0 || j < 0 || j >= filters.Count) return;
            var t = filters[i]; filters[i] = filters[j]; filters[j] = t;
            RefreshFilterList(j);
        }

        void ShowPresets()
        {
            var m = DarkMenu();
            m.ShowImageMargin = false;
            foreach (var grp in FilterPresets.All.GroupBy(p => p.Group))
            {
                var sub = new ToolStripMenuItem(grp.Key) { ForeColor = Theme.Text };
                sub.DropDown.BackColor = Theme.Card;
                ((ToolStripDropDownMenu)sub.DropDown).ShowImageMargin = false;
                sub.DropDown.Renderer = m.Renderer;
                foreach (var p in grp)
                {
                    var pp = p;
                    var it = new ToolStripMenuItem(p.Label) { ForeColor = Theme.Text, ToolTipText = (p.V != null ? "Video: " + p.V : "") + (p.A != null ? (p.V != null ? "\n" : "") + "Audio: " + p.A : "") };
                    it.Click += (s, e) =>
                    {
                        if (pp.V != null) filters.Add(new FilterEntry { Kind = 'V', Expr = pp.V });
                        if (pp.A != null) filters.Add(new FilterEntry { Kind = 'A', Expr = pp.A });
                        RefreshFilterList(filters.Count - 1);
                        Log("+ Preset “" + pp.Label + "”", Theme.Text);
                    };
                    sub.DropDownItems.Add(it);
                }
                m.Items.Add(sub);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Remove all filters", null, (s, e) => { filters.Clear(); txtGraph.Text = ""; RefreshFilterList(-1); });
            m.Show(btnPresets, new Point(0, btnPresets.Height));
        }

        void BrowseExtraInputs()
        {
            using (var d = new OpenFileDialog { Multiselect = true, Title = "Extra inputs for filters (input #1, #2…)", Filter = Formats.OpenDialogFilter() })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var cur = ExtraInputs();
                cur.AddRange(d.FileNames);
                txtExtraInputs.Text = string.Join(" ; ", cur.ToArray());
            }
        }

        List<string> ExtraInputs()
        {
            return txtExtraInputs.Text.Split(';').Select(x => x.Trim().Trim('"')).Where(x => x.Length > 0).ToList();
        }

        // =====================================================================
        //  FFmpeg
        // =====================================================================
        void InitFFmpeg()
        {
            string saved; settings.TryGetValue("ffmpeg", out saved);
            if (Runner.AutoDetect(saved))
            {
                header.Right1 = "Looking for FFmpeg…"; header.Invalidate();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { InitFFmpegWork(); }
                    catch (Exception ex) { UI(() => Log("Could not read your FFmpeg build: " + ex.Message, Theme.Error)); }
                });
            }
            else
            {
                header.Right1 = "▲  FFmpeg not found"; header.Right1Color = Theme.Error; header.Invalidate();
                Log("FFmpeg not found. Click “FFmpeg location…” and select ffmpeg.exe (e.g. C:\\FFmpeg\\bin\\ffmpeg.exe).", Theme.Error);
            }
        }

        void InitFFmpegWork()
        {
                {
                    string v = Runner.Version();
                    FormatCatalog.Load();
                    UI(() => OnFormatsLoaded());
                    FilterCatalog.Load();
                    UI(() =>
                    {
                        bool ok = !v.StartsWith("Error");
                        header.Right1 = (ok ? "●  " : "▲  ") + v;
                        header.Right1Color = ok ? Theme.Success : Theme.Error;
                        header.Invalidate();
                        Log("FFmpeg: " + Runner.FFmpeg, Theme.Muted);
                        if (string.IsNullOrEmpty(Runner.FFprobe)) Log("ffprobe.exe not found: durations and progress will be unavailable.", Theme.Accent1);
                        OnCatalogLoaded();
                    });
                }
        }

        void OnFormatsLoaded()
        {
            if (FormatCatalog.LoadError != null) { Log("Could not read the format list: " + FormatCatalog.LoadError, Theme.Error); return; }
            FillCodecCombos();
            SetCategory(category);
            header.FormatCount = new[] { MediaKind.Video, MediaKind.Audio, MediaKind.Image }.Sum(k => Formats.Of(k).Count + Formats.DynamicOf(k).Count);
            header.Invalidate();
            Log("Format library: " + FormatCatalog.Dynamic.Count + " output formats (muxers + image types) and " + FormatCatalog.Encoders.Count(e => e.Type != 'S') + " encoders found in your FFmpeg build.", Theme.Muted);
        }

        void FillCodecCombos()
        {
            Action<ComboBox, IEnumerable<Choice>, char> fill = (cb, curated, type) =>
            {
                string keep = cb.SelectedItem is Choice ? ((Choice)cb.SelectedItem).Key : "auto";
                cb.BeginUpdate();
                cb.Items.Clear();
                foreach (var c in curated) cb.Items.Add(c);
                var encs = FormatCatalog.Of(type).ToList();
                if (encs.Count > 0)
                {
                    cb.Items.Add(new Separator("──  ALL " + encs.Count + " " + (type == 'V' ? "VIDEO" : "AUDIO") + " ENCODERS  ──"));
                    foreach (var e in encs) cb.Items.Add(new Choice("enc:" + e.Name, e.Name + "  —  " + e.Desc + (e.Experimental ? "  (experimental)" : "")));
                }
                cb.EndUpdate();
                int idx = 0;
                for (int i = 0; i < cb.Items.Count; i++) { var c = cb.Items[i] as Choice; if (c != null && c.Key == keep) { idx = i; break; } }
                cb.SelectedIndex = idx;
            };
            fill(cbVCodec, Formats.VideoCodecs, 'V');
            fill(cbACodec, Formats.AudioCodecsForVideo, 'A');
        }

        class Separator
        {
            public string Text; public Separator(string t) { Text = t; }
            public override string ToString() { return Text; }
        }

        void SkipSeparator(ComboBox cb)
        {
            cb.SelectedIndexChanged += (s, e) =>
            {
                if (cb.SelectedItem is Separator)
                {
                    int i = cb.SelectedIndex;
                    cb.SelectedIndex = i + 1 < cb.Items.Count ? i + 1 : Math.Max(0, i - 1);
                }
            };
        }

        // ---------- resolution picker (presets + unlimited custom size) ----------
        public class ResPicker
        {
            public ComboBox Preset, Mode;
            public NumericUpDown W, H;
            public int[] PW, PH;
            public bool Busy;
        }

        ResPicker MakeRes(TableLayoutPanel sec, string[] labels, int[] pw, int[] ph)
        {
            var r = new ResPicker { PW = pw, PH = ph };
            r.Preset = Combo(labels);
            r.Preset.MaxDropDownItems = 24;
            r.W = Num(0, 2000000000, 0, 0); r.H = Num(0, 2000000000, 0, 0);
            r.W.Increment = r.H.Increment = 2;
            r.W.Dock = r.H.Dock = DockStyle.None;
            r.W.Width = r.H.Width = Theme.S(92);
            r.W.Location = new Point(0, 0);
            var x = new Label { Text = "×", ForeColor = Theme.Muted, AutoSize = false, Width = Theme.S(22), Height = Theme.S(26), TextAlign = ContentAlignment.MiddleCenter, Location = new Point(Theme.S(94), 0) };
            r.H.Location = new Point(Theme.S(118), 0);
            var host = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            host.Controls.AddRange(new Control[] { r.W, x, r.H });
            r.Mode = Combo("Stretch to exact size", "Fit — keep ratio, black bars", "Fill — keep ratio, crop");
            r.Mode.SelectedIndex = 1;
            tips.SetToolTip(r.W, "Output width in pixels — no limit. 0 = automatic (keeps the aspect ratio).");
            tips.SetToolTip(r.H, "Output height in pixels — no limit. 0 = automatic (keeps the aspect ratio).");
            tips.SetToolTip(r.Mode, "Used when both width and height are set and the source has a different aspect ratio.");
            AddRow(sec, "Resolution", r.Preset);
            AddRow(sec, "Size  W × H  (0 = auto)", host);
            AddRow(sec, "Aspect handling", r.Mode);
            Action sync = () => { };
            r.Preset.SelectedIndexChanged += (s, e) =>
            {
                int i = r.Preset.SelectedIndex;
                if (i < 0 || r.PW[i] < 0) { r.W.Focus(); return; }   // "Custom…"
                r.Busy = true;
                r.W.Value = r.PW[i]; r.H.Value = r.PH[i];
                r.Busy = false;
                sync();
            };
            EventHandler toCustom = (s, e) =>
            {
                sync();
                if (r.Busy) return;
                int i = r.Preset.SelectedIndex;
                if (i >= 0 && r.PW[i] >= 0 && (r.PW[i] != r.W.Value || r.PH[i] != r.H.Value))
                {
                    r.Busy = true; r.Preset.SelectedIndex = r.PW.Length - 1; r.Busy = false;
                }
            };
            r.W.ValueChanged += toCustom; r.H.ValueChanged += toCustom;
            r.W.KeyUp += (s, e) => toCustom(s, e); r.H.KeyUp += (s, e) => toCustom(s, e);
            sync();
            return r;
        }

        // ---------- NVIDIA RTX AI ----------
        bool loadingSettings;

        TrackBar SliderRow(int min, int max, int val, out Label valLabel, out Panel host)
        {
            var tb = new TrackBar { Minimum = min, Maximum = max, Value = val, TickStyle = TickStyle.None, BackColor = Theme.Panel, Dock = DockStyle.Fill, AutoSize = false, Height = Theme.S(28) };
            var lb = new Label { Text = val + " %", AutoSize = false, Width = Theme.S(48), ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right };
            tb.ValueChanged += (s2, e2) => lb.Text = tb.Value + " %";
            host = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            host.Controls.Add(tb); host.Controls.Add(lb);
            valLabel = lb;
            return tb;
        }

        void UpdateDnRows()
        {
            foreach (var c in dnRows) if (c != null) c.Visible = chkDn.Checked;
            lblDnInfo.Text = "Runs on: CPU (FFmpeg fftdnoiz / nlmeans / hqdn3d + luma sharpening), before Scale. Balanced ≈ 2–4× slower than without it." +
                             "\nNot a neural network: FFmpeg has no built-in AI denoiser; this follows Neat Video's method (noise profile + temporal filtering).";
        }

        void UpdateOptiRows()
        {
            foreach (var c in optiRows) if (c != null) c.Visible = chkOpti.Checked;
            bool lp = Scaling.HasLibplacebo;
            lblOptiInfo.Text = (FilterCatalog.Loaded && !lp)
                ? "⚠ Your FFmpeg build has no libplacebo (Vulkan) filter: OptiScaler needs a \"full\" FFmpeg build."
                : "Runs on: GPU (libplacebo / Vulkan)  ·  applied after the Scale type" +
                  "\nPort of OptiScaler's open-source shaders (GPL-3.0). DLSS, XeSS, FSR 2+ and MAS need a game's motion vectors and depth, so they cannot run on video files.";
            lblOptiInfo.ForeColor = lblOptiInfo.Text.StartsWith("⚠") ? Theme.Accent1 : Theme.Muted;
        }

        void UpdateScaleRows()
        {
            foreach (var c in scaleRows) if (c != null) c.Visible = chkScale.Checked;
            var t = cbScaleType.SelectedItem as Scaling.ScaleType;
            if (t == null) return;
            tbScaleSharp.Enabled = t.Sharpness;
            lblScaleSharpVal.ForeColor = t.Sharpness ? Theme.Text : Theme.Muted;
            bool lp = Scaling.HasLibplacebo;
            string where = t.GpuOnly ? "GPU (libplacebo / Vulkan)" : (chkScaleOpt.Checked && lp && (t.Key == "nearest" || t.Key == "bicubic" || t.Key == "lanczos" || t.Key == "spline" || t.Key == "bcas") ? "GPU (libplacebo / Vulkan)" : "CPU");
            string text = "Runs on: " + where + (t.Sharpness ? "" : "  ·  sharpness not used by this type");
            if (t.Key == "fsr2" || t.Key == "fsr3")
                text += "\nExperimental: AMD's FSR 2/3 need a game's motion vectors, so FFmpeg Forge estimates the motion from the video" +
                        (t.Key == "fsr3" ? " and generates in-between frames (×2 fps, CPU, slow)." : ".") + " FSR 4.1 is not possible: closed AI model, Radeon only.";
            if (FilterCatalog.Loaded && !lp && t.GpuOnly) text = "⚠ Your FFmpeg build has no libplacebo (Vulkan) filter: this type needs a \"full\" FFmpeg build.";
            lblScaleInfo.Text = text + "\nLS1 (Lossless Scaling's own algorithm) is proprietary and cannot be included.";
            lblScaleInfo.ForeColor = text.StartsWith("⚠") ? Theme.Accent1 : Theme.Muted;
        }

        void UpdateUpmixRow()
        {
            var c = cbCh.SelectedItem as Choice;
            var l = c != null ? ChannelLayouts.Get(c.Key) : null;
            bool on = l != null && new[] { "5.1", "7.1", "5.1.2", "5.1.4", "7.1.2", "7.1.4", "7.1.6", "9.1.4", "9.1.6", "binaural" }.Contains(l.Key);
            cbUpmix.Visible = lblUpmix.Visible = on;
        }

        void UpdateRtxRows()
        {
            foreach (var c in vsrRows) if (c != null) c.Visible = chkVsr.Checked;
            foreach (var c in hdrRows) if (c != null) c.Visible = chkHdr.Checked;
            foreach (var c in dlssRows) if (c != null) c.Visible = chkDlss.Checked;
        }

        void UpdateRtxStatus()
        {
            if (lblRtxStatus == null) return;
            lblRtxStatus.Text = Rtx.StatusText();
            btnVsrParams.Enabled = Rtx.NativeVsr;
            btnHdrParams.Enabled = Rtx.NativeHdr;
            btnDlssParams.Enabled = Rtx.NativeDlss;
            bool any = Rtx.AnyNative || Rtx.HasProcessor;
            lblRtxStatus.ForeColor = any ? Theme.Success : Theme.Muted;
        }

        void LocateRtxProcessor()
        {
            using (var d = new OpenFileDialog { Title = "Select RTXVideoProcessor.exe (NVIDIA RTX Video SDK)", Filter = "RTXVideoProcessor.exe|RTXVideoProcessor.exe;*.exe" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Rtx.ProcessorPath = d.FileName;
                settings["rtxproc"] = d.FileName;
                SaveSettings();
                UpdateRtxStatus();
                Log("RTXVideoProcessor: " + d.FileName, Theme.Muted);
            }
        }

        void EditRtxParams(string[] names, ref string expr, FlatBtn btn, string what)
        {
            if (!EnsureCatalog()) return;
            string f = Rtx.Pick(names);
            if (f == null) { Log(what + ": your FFmpeg build has no " + string.Join(" / ", names) + " filter.", Theme.Accent1); return; }
            using (var d = new FilterBrowser(new FilterEntry { Kind = 'V', Expr = string.IsNullOrWhiteSpace(expr) ? f : expr }))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null) return;
                string e = d.Result.Expr.Trim();
                expr = e == f ? null : e;
                btn.Text = "All " + what + " filter parameters…" + (expr != null ? "  (custom ✓)" : "");
                Log(what + " parameters: " + (expr ?? "automatic"), Theme.Text);
            }
        }

        bool BuildJobCommand(Job job, ConvSettings s, string outPath, out string exe, out string args, out double expected, out string err)
        {
            Rtx.DriverPlan plan;
            return BuildJobCommand(job, s, outPath, out exe, out args, out expected, out err, out plan);
        }

        bool BuildJobCommand(Job job, ConvSettings s, string outPath, out string exe, out string args, out double expected, out string err, out Rtx.DriverPlan plan)
        {
            exe = Runner.FFmpeg;
            plan = null;
            string why;
            if (s.Seed != null && s.Seed.Enabled && s.Format.Kind != MediaKind.Audio)
            {
                var sp = SeedEngine.Build(job, s, outPath, out err);
                expected = sp != null ? sp.Expected : 0;
                args = sp != null ? sp.Text() : null;
                return sp != null;
            }
            string engine = s.Format.Kind == MediaKind.Video && s.RtxActive ? Rtx.Engine(s, out why) : null;
            if (engine == "driver")
            {
                plan = Rtx.PlanDriver(job, s, outPath, out err);
                expected = plan != null ? plan.Expected : 0;
                args = plan != null ? plan.Text() : null;
                exe = Rtx.DriverPath;
                return plan != null;
            }
            if (engine == "processor")
            {
                exe = Rtx.ProcessorPath;
                args = Rtx.ProcessorArgs(job, s, outPath, out expected, out err);
            }
            else args = CommandBuilder.Build(job, s, outPath, out expected, out err);
            return args != null;
        }

        void ChooseFFmpeg()
        {
            using (var d = new OpenFileDialog { Title = "Select ffmpeg.exe", Filter = "ffmpeg.exe|ffmpeg.exe|Programs (*.exe)|*.exe", InitialDirectory = Directory.Exists(@"C:\FFmpeg\bin") ? @"C:\FFmpeg\bin" : "" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (Runner.SetFFmpeg(d.FileName)) { settings["ffmpeg"] = d.FileName; SaveSettings(); FilterCatalog.All.Clear(); FilterCatalog.Loaded = false; FormatCatalog.Dynamic.Clear(); FormatCatalog.Loaded = false; InitFFmpeg(); }
            }
        }

        // =====================================================================
        //  File management
        // =====================================================================
        void AddFilesDialog()
        {
            using (var d = new OpenFileDialog { Multiselect = true, Title = "Add files to convert", Filter = Formats.OpenDialogFilter() })
                if (d.ShowDialog(this) == DialogResult.OK) AddPaths(d.FileNames);
        }

        void AddFolderDialog()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose a folder: every media file inside (subfolders included) will be added." })
                if (d.ShowDialog(this) == DialogResult.OK) AddPaths(new[] { d.SelectedPath });
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && !running)
            {
                e.Effect = DragDropEffects.Copy;
                dropHint.Highlight = true; dropHint.Invalidate();
            }
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            dropHint.Highlight = false; dropHint.Invalidate();
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) AddPaths(files);
        }

        public void AddFromCommandLine(string[] a) { AddPaths(a); }

        void AddPaths(IEnumerable<string> paths)
        {
            if (running) return;
            var existing = new HashSet<string>(list.Items.Cast<ListViewItem>().Select(i => ((Job)i.Tag).Path), StringComparer.OrdinalIgnoreCase);
            var toAdd = new List<string>();
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                    {
                        foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                            if (Formats.IsMediaFile(f)) toAdd.Add(f);
                    }
                    else if (File.Exists(p)) toAdd.Add(p);
                }
                catch (Exception ex) { Log("Cannot access " + p + " (" + ex.Message + ")", Theme.Error); }
            }
            var newJobs = new List<Job>();
            list.BeginUpdate();
            foreach (var f in toAdd)
            {
                if (!existing.Add(f)) continue;
                var job = new Job { Path = f, Kind = Formats.KindFromExt(f) };
                try { job.Size = new FileInfo(f).Length; } catch { }
                var it = new ListViewItem(new[] { Path.GetFileName(f), Formats.KindName(job.Kind), "analyzing…", "", FmtSize(job.Size), job.Status }) { Tag = job, ToolTipText = f };
                list.Items.Add(it);
                newJobs.Add(job);
            }
            list.EndUpdate();
            UpdateEmpty();
            if (newJobs.Count > 0)
            {
                statusLbl.Text = list.Items.Count + " file(s) in the list.";
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    foreach (var j in newJobs)
                    {
                        Runner.Probe(j);
                        var jj = j;
                        UI(() => RefreshRow(jj));
                    }
                });
            }
        }

        void RefreshRow(Job j)
        {
            foreach (ListViewItem it in list.Items)
            {
                if (it.Tag != j) continue;
                it.SubItems[1].Text = Formats.KindName(j.Kind);
                it.SubItems[2].Text = j.Info;
                it.SubItems[3].Text = j.Kind == MediaKind.Image && !CommandBuilder.IsAnimatedImageSource(j) ? "—" : FmtDur(j.Duration);
                it.SubItems[4].Text = FmtSize(j.Size);
                it.SubItems[5].Text = j.Status;
                list.Invalidate(it.Bounds);
                break;
            }
        }

        void RemoveSelected()
        {
            if (running) return;
            foreach (ListViewItem it in list.SelectedItems) list.Items.Remove(it);
            UpdateEmpty();
        }

        void ClearAll()
        {
            if (running) return;
            list.Items.Clear();
            overall.Value = 0;
            statusLbl.Text = "Ready.";
            UpdateEmpty();
        }

        void UpdateEmpty() { dropHint.Visible = list.Items.Count == 0; FitColumns(); }

        Job SelectedJob()
        {
            if (list.SelectedItems.Count > 0) return list.SelectedItems[0].Tag as Job;
            if (list.Items.Count > 0) return list.Items[0].Tag as Job;
            return null;
        }

        void OpenSelectedOutput()
        {
            var j = SelectedJob();
            if (j == null) return;
            if (!string.IsNullOrEmpty(j.Output) && File.Exists(j.Output)) Shell(j.Output);
            else if (!string.IsNullOrEmpty(j.Output)) Shell(Path.GetDirectoryName(j.Output));
        }

        void OpenOutputFolder()
        {
            string dir = null;
            if (!string.IsNullOrEmpty(lastOutput)) dir = Path.GetDirectoryName(lastOutput);
            else if (!chkSameDir.Checked && Directory.Exists(txtOutDir.Text)) dir = txtOutDir.Text;
            else { var j = SelectedJob(); if (j != null) dir = Path.GetDirectoryName(j.Path); }
            if (dir != null && Directory.Exists(dir)) Shell(dir);
        }

        void BrowseOutDir()
        {
            using (var d = new FolderBrowserDialog { Description = "Destination folder for converted files", SelectedPath = txtOutDir.Text })
                if (d.ShowDialog(this) == DialogResult.OK) txtOutDir.Text = d.SelectedPath;
        }

        static void Shell(string file, string args = null)
        {
            try { Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); } catch { }
        }

        // =====================================================================
        //  Conversion
        // =====================================================================
        static readonly int[] RateValues = { 0, 8000, 16000, 22050, 32000, 44100, 48000, 88200, 96000, 192000 };

        ConvSettings Snapshot()
        {
            var s = new ConvSettings();
            s.Format = CurrentFormat;
            s.VCodec = cbVCodec.SelectedItem is Choice ? ((Choice)cbVCodec.SelectedItem).Key : "auto";
            s.QualityIdx = cbQuality.SelectedIndex;
            s.NvCrf = (int)nuNvCrf.Value;
            s.VBitrate = (int)nuVBitrate.Value;
            s.PresetIdx = cbPreset.SelectedIndex;
            var rp = s.Format.Kind == MediaKind.Image ? resImage : resVideo;
            s.ResW = (int)rp.W.Value; s.ResH = (int)rp.H.Value; s.ScaleMode = rp.Mode.SelectedIndex;
            double fps = 0;
            if (cbFps.SelectedIndex > 0) double.TryParse(cbFps.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out fps);
            s.Fps = fps;
            s.Rotate = s.Format.Kind == MediaKind.Image ? cbImgRotate.SelectedIndex : cbRotate.SelectedIndex;
            s.KeepSubs = chkSubs.Checked;
            s.RtxVsr = chkVsr.Checked && s.Format.Kind == MediaKind.Video;
            s.RtxHdr = chkHdr.Checked && s.Format.Kind == MediaKind.Video;
            s.Dlss = chkDlss.Checked && s.Format.Kind == MediaKind.Video;
            s.VsrQuality = cbVsrQ.SelectedIndex + 1;
            s.VsrScale = cbVsrScale.SelectedIndex + 2;
            s.HdrContrast = (int)nuHdrContrast.Value; s.HdrSaturation = (int)nuHdrSat.Value;
            s.HdrMiddleGray = (int)nuHdrGray.Value; s.HdrMaxLum = (int)nuHdrLum.Value;
            s.DlssScale = new[] { 1.0, 1.5, 1.72, 2.0, 3.0 }[cbDlssMode.SelectedIndex];
            s.DlssQuality = new[] { 4, 4, 3, 2, 1 }[cbDlssMode.SelectedIndex];
            s.VsrExpr = vsrExpr; s.HdrExpr = hdrExpr; s.DlssExpr = dlssExpr;
            s.RtxEnginePref = new[] { "auto", "native", "driver", "processor" }[cbRtxEngine.SelectedIndex];
            s.ScaleEnabled = chkScale.Checked && s.Format.Kind != MediaKind.Audio;
            s.ScaleType = cbScaleType.SelectedItem is Scaling.ScaleType ? ((Scaling.ScaleType)cbScaleType.SelectedItem).Key : "fsr";
            s.ScaleFactor = new[] { 1.5, 2.0, 2.5, 3.0, 4.0, 0.0 }[Math.Max(0, cbScaleFactor.SelectedIndex)];
            if (s.ScaleFactor == 0 && s.ResW <= 0 && s.ResH <= 0) s.ScaleFactor = 2.0;
            s.ScaleSharpness = tbScaleSharp.Value;
            s.ScaleOptimized = chkScaleOpt.Checked;
            s.ScaleFidelity = chkScaleFid.Checked;
            s.ScaleFullChroma = chkScale444.Checked;
            s.DnEnabled = chkDn.Checked && s.Format.Kind != MediaKind.Audio;
            s.DnPreset = Math.Max(0, cbDnPreset.SelectedIndex);
            s.DnEngine = Math.Max(0, cbDnEngine.SelectedIndex);
            s.DnStrength = tbDnStrength.Value;
            s.DnSharpen = tbDnSharp.Value;
            s.DnTemporal = chkDnTemporal.Checked;
            s.DnAutoProfile = chkDnAuto.Checked;
            s.OptiEnabled = chkOpti.Checked && s.Format.Kind != MediaKind.Audio;
            s.OptiMult = OptiMults[Math.Max(0, cbOptiMult.SelectedIndex)];
            s.OptiKernel = Math.Max(0, cbOptiKernel.SelectedIndex) + 1;
            s.OptiSharpness = tbOptiSharp.Value;
            s.OptiContrast = chkOptiContrast.Checked;
            s.StillDuration = (double)nuStill.Value;
            s.Seed = SeedSnapshot(s.Format.Kind);
            s.ACodec = cbACodec.SelectedItem is Choice ? ((Choice)cbACodec.SelectedItem).Key : "auto";
            int kb = 0; if (cbAKbps.SelectedIndex > 0) int.TryParse(cbAKbps.Text, out kb);
            s.AKbps = cbAKbps.Enabled ? kb : 0;
            s.SampleRate = RateValues[cbRate.SelectedIndex];
            s.ChLayout = cbCh.SelectedItem is Choice ? ((Choice)cbCh.SelectedItem).Key : "";
            s.UpmixStyle = UpmixCodes[Math.Max(0, cbUpmix.SelectedIndex)];
            s.Normalize = chkNorm.Checked;
            s.VolumePct = (int)nuVolume.Value;
            s.ImgQuality = tbImgQ.Value;
            s.ExtractMode = cbExtract.SelectedIndex;
            s.ExtractAt = (double)nuExtractVal.Value;
            s.ExtractEvery = (double)nuExtractVal.Value;
            s.TrimStart = ParseTime(txtStart.Text);
            s.TrimEnd = ParseTime(txtEnd.Text);
            s.CustomArgs = txtCustom.Text;
            s.OutDir = chkSameDir.Checked ? null : txtOutDir.Text.Trim();
            s.Overwrite = chkOverwrite.Checked;
            s.Filters = filters.Select(f => new FilterEntry { Kind = f.Kind, Expr = f.Expr, Enabled = f.Enabled }).ToList();
            s.FilterComplex = txtGraph.Text;
            s.ExtraInputs = ExtraInputs();
            return s;
        }

        static double ParseTime(string t)
        {
            t = (t ?? "").Trim().Replace(',', '.');
            if (t.Length == 0) return -1;
            double total = 0;
            foreach (var part in t.Split(':'))
            {
                double v;
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return -1;
                total = total * 60 + v;
            }
            return total;
        }

        static string MakeOutPath(Job job, ConvSettings s, bool sequence)
        {
            string dir = string.IsNullOrEmpty(s.OutDir) ? Path.GetDirectoryName(job.Path) : s.OutDir;
            string name = Path.GetFileNameWithoutExtension(job.Path);
            string ext = "." + s.Format.Ext;
            Func<string, string> build = b => Path.Combine(dir, sequence ? b + "_%05d" + ext : b + ext);
            Func<string, bool> taken = b =>
            {
                string p = sequence ? Path.Combine(dir, b + "_00001" + ext) : Path.Combine(dir, b + ext);
                if (string.Equals(Path.GetFullPath(p), Path.GetFullPath(job.Path), StringComparison.OrdinalIgnoreCase)) return true;
                return !s.Overwrite && File.Exists(p);
            };
            string bn = name;
            int n = 1;
            while (taken(bn)) { n++; bn = name + " (" + n + ")"; }
            return build(bn);
        }

        static bool IsSequence(Job j, ConvSettings s)
        {
            bool srcVideo = j.Kind == MediaKind.Video || CommandBuilder.IsAnimatedImageSource(j);
            if (s.Seed != null && s.Seed.Enabled) return false;
            return s.Format.Kind == MediaKind.Image && srcVideo && s.ExtractMode != 0;
        }

        void PreviewCommand()
        {
            var j = SelectedJob();
            if (j == null) { Log("Add a file first.", Theme.Accent1); return; }
            var s = Snapshot();
            double exp; string err;
            string exe, args;
            BuildJobCommand(j, s, MakeOutPath(j, s, IsSequence(j, s)), out exe, out args, out exp, out err);
            if (args == null) { Log("Impossible: " + err, Theme.Error); return; }
            if (exe != Runner.FFmpeg)
            {
                Log("\"" + exe + "\" " + args, Theme.Text);
                try { Clipboard.SetText("\"" + exe + "\" " + args); Log("(command copied to clipboard)", Theme.Muted); } catch { }
                return;
            }
            string shown = args.Replace("-hide_banner -nostdin -y -loglevel warning -progress pipe:1 -nostats ", "-y ");
            Log("ffmpeg " + shown, Theme.Text);
            try { Clipboard.SetText("\"" + (Runner.FFmpeg.Length > 0 ? Runner.FFmpeg : "ffmpeg") + "\" " + shown); Log("(command copied to clipboard)", Theme.Muted); } catch { }
        }

        void StartConversion()
        {
            if (running) return;
            if (string.IsNullOrEmpty(Runner.FFmpeg)) { ChooseFFmpeg(); if (string.IsNullOrEmpty(Runner.FFmpeg)) return; }
            if (list.Items.Count == 0) { AddFilesDialog(); if (list.Items.Count == 0) return; }
            var s = Snapshot();
            if (s.Format == null) return;
            if (s.OutDir != null)
            {
                if (s.OutDir.Length == 0) { MessageBox.Show(this, "Please choose a destination folder.", "FFmpeg Forge", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                try { Directory.CreateDirectory(s.OutDir); }
                catch (Exception ex) { MessageBox.Show(this, "Invalid destination folder:\n" + ex.Message, "FFmpeg Forge", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            foreach (var x in s.ExtraInputs)
                if (!File.Exists(x)) { MessageBox.Show(this, "Extra input not found:\n" + x, "FFmpeg Forge", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            SaveSettings();

            var jobs = new List<Job>();
            var targets = list.SelectedItems.Count > 1 ? list.SelectedItems.Cast<ListViewItem>() : list.Items.Cast<ListViewItem>();
            foreach (var it in targets) { var j = (Job)it.Tag; j.Status = StQueued; j.Progress = 0; j.Error = null; jobs.Add(j); }
            list.Invalidate();

            running = true; cancelRequested = false;
            btnConvert.Enabled = false; btnCancel.Enabled = true;
            overall.Value = 0;
            Log("", Theme.Muted);
            int nf = s.Filters.Count(f => f.Enabled) + (string.IsNullOrWhiteSpace(s.FilterComplex) ? 0 : 1);
            Log("━━━  Converting " + jobs.Count + " file(s) to " + s.Format.Label + (nf > 0 ? "  ·  " + nf + " filter(s)" : "") + "  ━━━", Theme.Accent1);

            var t = new Thread(() => Worker(jobs, s)) { IsBackground = true };
            t.Start();
        }

        void Worker(List<Job> jobs, ConvSettings s)
        {
            int ok = 0, fail = 0;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < jobs.Count; i++)
            {
                if (cancelRequested) { for (int k = i; k < jobs.Count; k++) jobs[k].Status = StCancelled; break; }
                var job = jobs[i];
                int index = i;
                if (!job.Probed && !string.IsNullOrEmpty(Runner.FFprobe)) Runner.Probe(job);

                bool seq = IsSequence(job, s);
                string outPath = MakeOutPath(job, s, seq);
                double expected; string err;
                string exe, args;
                Rtx.DriverPlan plan;
                BuildJobCommand(job, s, outPath, out exe, out args, out expected, out err, out plan);
                job.Output = outPath;
                if (s.DlssSkipped)
                    UI(() => Log("   ⚠ DLSS skipped: NVIDIA offers no DLSS for video files (it needs a game engine). RTX Video Super Resolution is applied instead.", Theme.Accent1));
                UI(() =>
                {
                    statusLbl.Text = "Converting " + (index + 1) + " / " + jobs.Count + "  —  " + Path.GetFileName(job.Path);
                    RefreshRow(job);
                });
                if (args == null)
                {
                    job.Status = StError; job.Error = err; fail++;
                    UI(() => { Log("✗ " + Path.GetFileName(job.Path) + ": " + err, Theme.Error); RefreshRow(job); });
                    continue;
                }
                job.Status = StConverting;
                UI(() => { Log("▶ " + Path.GetFileName(job.Path) + "  →  " + Path.GetFileName(outPath), Theme.Text); RefreshRow(job); });
                if (s.DnEnabled) { string np = Denoise.Describe(job); if (np != null) UI(() => Log("   Denoiser " + np, Theme.Muted)); }

                long lastUi = 0;
                var jsw = Stopwatch.StartNew();
                string lastErr;
                int code;
                try
                {
                    if (s.Seed != null && s.Seed.Enabled && s.Format.Kind != MediaKind.Audio)
                    {
                        string serr;
                        var sp = SeedEngine.Build(job, s, outPath, out serr);
                        UI(() => Log("   SeedVR2: " + SeedEngine.Short(sp.Script == null ? "" : s.Seed.Model) + "  ·  " + sp.InW + "×" + sp.InH + " → short side " + sp.OutShort, Color.FromArgb(118, 185, 0)));
                        code = SeedEngine.Run(sp,
                            p =>
                            {
                                job.Progress = p;
                                long now = jsw.ElapsedMilliseconds;
                                if (now - lastUi < 120 && p < 1) return;
                                lastUi = now;
                                double ov = (index + p) / jobs.Count;
                                UI(() => { overall.Value = ov; RefreshRow(job); });
                            },
                            line => UI(() => Log("   " + line, line.StartsWith("SeedVR2 ") ? Color.FromArgb(118, 185, 0) : Color.FromArgb(200, 170, 110))),
                            () => cancelRequested, out lastErr);
                    }
                    else if (plan != null)
                    {
                        var pl = plan;
                        UI(() => Log("   RTX driver engine: " + pl.W + "×" + pl.H + " → " + pl.OW + "×" + pl.OH + (s.RtxHdr ? "  ·  HDR10" : ""), Theme.Muted));
                        code = Rtx.RunDriver(plan,
                            p =>
                            {
                                job.Progress = p;
                                long now = jsw.ElapsedMilliseconds;
                                if (now - lastUi < 120 && p < 1) return;
                                lastUi = now;
                                double ov = (index + p) / jobs.Count;
                                UI(() => { overall.Value = ov; RefreshRow(job); });
                            },
                            line => UI(() => Log("   " + line, line.StartsWith("ForgeRTX") ? Color.FromArgb(118, 185, 0) : Color.FromArgb(200, 170, 110))),
                            () => cancelRequested, out lastErr);
                    }
                    else
                    code = Runner.Run(exe, args, expected,
                        p =>
                        {
                            job.Progress = p;
                            long now = jsw.ElapsedMilliseconds;
                            if (now - lastUi < 120 && p < 1) return;
                            lastUi = now;
                            double ov = (index + p) / jobs.Count;
                            UI(() => { overall.Value = ov; RefreshRow(job); });
                        },
                        line => { if (line.Contains("Guessed Channel Layout")) return; UI(() => Log("   " + line, Color.FromArgb(200, 170, 110))); },
                        () => cancelRequested, out lastErr);
                }
                catch (Exception ex) { code = -2; lastErr = ex.Message; }

                if (code == 0)
                {
                    job.Status = StDone; job.Progress = 1; ok++;
                    lastOutput = seq ? outPath.Replace("%05d", "00001") : outPath;
                    if (seq) job.Output = lastOutput;
                    string size = "";
                    try { if (File.Exists(job.Output)) size = "  (" + FmtSize(new FileInfo(job.Output).Length) + ")"; } catch { }
                    double secs = jsw.Elapsed.TotalSeconds;
                    UI(() => Log("✓ Done in " + secs.ToString("0.0", CultureInfo.InvariantCulture) + " s" + size, Theme.Success));
                }
                else if (code == -1 || cancelRequested)
                {
                    job.Status = StCancelled;
                    try { if (!seq && File.Exists(outPath)) File.Delete(outPath); } catch { }
                }
                else
                {
                    job.Status = StError; job.Error = lastErr; fail++;
                    try { if (!seq && File.Exists(outPath) && new FileInfo(outPath).Length == 0) File.Delete(outPath); } catch { }
                    string hint = Hint(lastErr);
                    UI(() =>
                    {
                        Log("✗ Failed (code " + code + ")" + (string.IsNullOrEmpty(lastErr) ? "" : ":"), Theme.Error);
                        if (!string.IsNullOrEmpty(lastErr)) foreach (var l in lastErr.Split('\n').Reverse().Take(5).Reverse()) Log("   " + l.TrimEnd(), Theme.Error);
                        if (hint != null) Log("   → " + hint, Theme.Accent1);
                    });
                }
                UI(() => RefreshRow(job));
            }
            double total = sw.Elapsed.TotalSeconds;
            UI(() =>
            {
                running = false;
                btnConvert.Enabled = true; btnCancel.Enabled = false;
                overall.Value = cancelRequested ? overall.Value : 1;
                string msg = (cancelRequested ? "Conversion cancelled. " : "Finished! ") + ok + " succeeded" + (fail > 0 ? ", " + fail + " failed" : "") + " — " + FmtDur(total);
                statusLbl.Text = msg;
                Log(msg, fail > 0 ? Theme.Accent1 : Theme.Success);
                list.Invalidate();
                if (!cancelRequested) System.Media.SystemSounds.Asterisk.Play();
            });
        }

        static string Hint(string err)
        {
            if (string.IsNullOrEmpty(err)) return null;
            string e = err.ToLowerInvariant();
            if (e.Contains("out of memory") || e.Contains("outofmemory"))
                return "SeedVR2 ran out of VRAM: pick a lighter model (FP8 / GGUF), a smaller batch size, raise BlockSwap or tick VAE tiling / Offload.";
            if (e.Contains("no module named") || e.Contains("modulenotfounderror"))
                return "The SeedVR2 Python environment is incomplete: click \"Install SeedVR2…\" again.";
            if (e.Contains("cuda") && (e.Contains("not available") || e.Contains("no cuda gpus") || e.Contains("no kernel image")))
                return "PyTorch cannot use your NVIDIA GPU: update the NVIDIA driver, then reinstall SeedVR2.";
            if (e.Contains("vulkan") || e.Contains("libplacebo") || e.Contains("failed creating vulkan") || e.Contains("device creation failed"))
                return "The GPU path (Vulkan / libplacebo) could not start. Update your graphics driver, or untick \"Optimized\" / pick a CPU scale type (xBR, Integer, Sharp bilinear, BCAS, Lanczos).";
            if (e.Contains("no such filter")) return "This filter is not included in your FFmpeg build. Pick it from the Filter library to see what is available.";
            if (e.Contains("error parsing") || e.Contains("error initializing filter") || e.Contains("option not found") || e.Contains("error applying option")) return "A filter parameter is invalid. Double-click the filter to edit it, or check its documentation.";
            if (e.Contains("cannot find a matching stream") || e.Contains("invalid stream specifier")) return "The filtergraph refers to a stream that does not exist (e.g. [1:v] without an extra input).";
            if (e.Contains("filtergraph") && e.Contains("was specified through")) return "A filter was used both in the chain and the filtergraph for the same stream. Use one or the other.";
            if (e.Contains("unknown encoder") || e.Contains("encoder not found")) return "This codec is not included in your FFmpeg build. Choose another codec (or use a \"full\" build).";
            if (e.Contains("nvenc") || e.Contains("cuda") || e.Contains("no capable devices")) return "NVIDIA GPU encoder unavailable: choose a CPU codec (H.264 / H.265).";
            if (e.Contains("qsv") || e.Contains("mfx")) return "Intel QuickSync is unavailable on this PC: choose a CPU codec.";
            if (e.Contains("amf")) return "AMD encoder unavailable on this PC: choose a CPU codec.";
            if (e.Contains("codec tag") || e.Contains("tag found for codec")) return "This encoder is not supported by this container. Pick a container that accepts it (MKV, MOV, NUT, AVI…) or its native format in the list.";
            if (e.Contains("could not find tag") || e.Contains("not currently supported in container") || e.Contains("codec not currently supported")) return "This codec is not compatible with this container. Set the codec back to “Auto”.";
            if (e.Contains("permission denied")) return "Access denied: check the destination folder or close the file if it is open.";
            if (e.Contains("does not contain any stream") || e.Contains("matches no streams")) return "The file does not contain the requested stream type.";
            return null;
        }

        void UI(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { }
        }

        void Log(string text, Color c)
        {
            if (log.TextLength > 400000) { log.Clear(); }
            log.SelectionStart = log.TextLength;
            log.SelectionLength = 0;
            log.SelectionColor = c;
            log.AppendText((log.TextLength > 0 ? Environment.NewLine : "") + text);
            log.SelectionStart = log.TextLength;
            log.ScrollToCaret();
        }

        static string FmtSize(long b)
        {
            if (b <= 0) return "";
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = b; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + u[i];
        }

        static string FmtDur(double s)
        {
            if (s <= 0) return "—";
            var t = TimeSpan.FromSeconds(s);
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        // ---------- SeedVR2 ----------
        void BuildSeedSection(Control stack)
        {
            secSeed = Section("SEEDVR2  (AI RESTORATION + UPSCALING)");
            chkSeed = Check("SeedVR2 — AI video and image restoration / upscaling");
            AddRow(secSeed, null, chkSeed);
            lblSeedStatus = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(400), 0), Margin = new Padding(0, Theme.S(2), 0, Theme.S(4)) };
            var bInst = Btn("Install SeedVR2…", 0); bInst.Dock = DockStyle.Fill; bInst.Height = Theme.S(30); bInst.Font = fSmall;
            bInst.Click += (s2, e2) => InstallSeed();
            var bDir = Btn("SeedVR2 folder…", 0); bDir.Dock = DockStyle.Fill; bDir.Height = Theme.S(30); bDir.Font = fSmall;
            bDir.Click += (s2, e2) => ChooseSeedDir();
            var bModels = Btn("Models folder", 0); bModels.Dock = DockStyle.Fill; bModels.Height = Theme.S(30); bModels.Font = fSmall;
            bModels.Click += (s2, e2) =>
            {
                string md = SeedEngine.ModelDir(SeedEngine.Script);
                if (md == null) { Log("Install SeedVR2 first.", Theme.Accent1); return; }
                try { Directory.CreateDirectory(md); Process.Start(new ProcessStartInfo(md) { UseShellExecute = true }); } catch (Exception ex) { Log(ex.Message, Theme.Error); }
            };
            var bar = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            for (int i = 0; i < 3; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            bar.Controls.Add(bInst, 0, 0); bar.Controls.Add(bDir, 1, 0); bar.Controls.Add(bModels, 2, 0);
            AddRow(secSeed, null, bar); seedRows.Add(bar);
            AddRow(secSeed, null, lblSeedStatus); seedRows.Add(lblSeedStatus);

            cbSeedModel = Combo(SeedEngine.Models.Cast<object>().ToArray());
            cbSeedModel.DropDownWidth = Theme.S(440);
            cbSeedModel.SelectedIndex = SeedEngine.Models.FindIndex(m => m.File == SeedEngine.DefaultModel);
            seedRows.Add(AddRow(secSeed, "   Model", cbSeedModel)); seedRows.Add(cbSeedModel);
            cbSeedSize = Combo(SeedEngine.Sizes.Cast<object>().ToArray());
            cbSeedSize.SelectedIndex = 1;
            seedRows.Add(AddRow(secSeed, "   Output size", cbSeedSize)); seedRows.Add(cbSeedSize);
            cbSeedBatch = Combo(SeedEngine.Batches.Select(b => (object)(b + " frames")).ToArray());
            cbSeedBatch.SelectedIndex = Array.IndexOf(SeedEngine.Batches, 13);
            seedRows.Add(AddRow(secSeed, "   Batch size", cbSeedBatch)); seedRows.Add(cbSeedBatch); seedVidRows.Add(cbSeedBatch);
            cbSeedColor = Combo(SeedEngine.ColorLabels.Cast<object>().ToArray());
            seedRows.Add(AddRow(secSeed, "   Colour correction", cbSeedColor)); seedRows.Add(cbSeedColor);
            Panel inHost, latHost;
            tbSeedInNoise = SliderRow(0, 100, 0, out lblSeedInNoise, out inHost);
            tbSeedLatNoise = SliderRow(0, 100, 0, out lblSeedLatNoise, out latHost);
            seedRows.Add(AddRow(secSeed, "   Input noise", inHost)); seedRows.Add(inHost);
            seedRows.Add(AddRow(secSeed, "   Latent noise", latHost)); seedRows.Add(latHost);
            nuSeedSeed = Num(0, 999999999, 42, 0);
            seedRows.Add(AddRow(secSeed, "   Seed", nuSeedSeed)); seedRows.Add(nuSeedSeed);
            nuSeedChunk = Num(0, 2000, 165, 0); nuSeedChunk.Increment = 15;
            seedRows.Add(AddRow(secSeed, "   Chunk (frames, 0 = all)", nuSeedChunk)); seedRows.Add(nuSeedChunk); seedVidRows.Add(nuSeedChunk);
            nuSeedOverlap = Num(0, 16, 3, 0);
            seedRows.Add(AddRow(secSeed, "   Temporal overlap", nuSeedOverlap)); seedRows.Add(nuSeedOverlap); seedVidRows.Add(nuSeedOverlap);
            nuSeedSwap = Num(0, 36, 0, 0);
            seedRows.Add(AddRow(secSeed, "   BlockSwap", nuSeedSwap)); seedRows.Add(nuSeedSwap);
            cbSeedAttn = Combo(SeedEngine.AttentionLabels.Cast<object>().ToArray());
            seedRows.Add(AddRow(secSeed, "   Attention", cbSeedAttn)); seedRows.Add(cbSeedAttn);
            txtSeedGpu = Txt("0");
            seedRows.Add(AddRow(secSeed, "   CUDA GPU", txtSeedGpu)); seedRows.Add(txtSeedGpu);
            chkSeedTiled = Check("VAE tiling (high resolutions with less VRAM)");
            AddRow(secSeed, null, chkSeedTiled); seedRows.Add(chkSeedTiled);
            chkSeedOffload = Check("Offload models to RAM between phases (less VRAM)");
            AddRow(secSeed, null, chkSeedOffload); seedRows.Add(chkSeedOffload);
            chkSeedCompile = Check("torch.compile (faster, needs Triton; slow first run)");
            AddRow(secSeed, null, chkSeedCompile); seedRows.Add(chkSeedCompile);
            lblSeedInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(400), 0), Margin = new Padding(0, Theme.S(4), 0, 0) };
            AddRow(secSeed, null, lblSeedInfo); seedRows.Add(lblSeedInfo);

            tips.SetToolTip(chkSeed, "SeedVR2 (ByteDance-Seed): one-step diffusion model that restores and upscales videos and pictures —\nremoves blur, noise and compression artefacts and rebuilds fine detail, with temporal consistency.\nRuns locally on your NVIDIA GPU through the official SeedVR2 command line (Python / PyTorch).\nYour format, codec, audio, trim, Denoiser and filters are kept.");
            tips.SetToolTip(cbSeedModel, "3B: faster, less VRAM.  7B: better quality.  Sharp: more fine detail.\nFP16: full precision.  FP8 / GGUF: smaller and lighter (GGUF Q4 for 8–12 GB cards).\nThe chosen model downloads automatically on the first conversion (2 to 16 GB).");
            tips.SetToolTip(cbSeedSize, "Size of the SHORT side of the result (×2 on 1920×1080 → 3840×2160).\n\"Restore only\" keeps the size and only cleans / sharpens.");
            tips.SetToolTip(cbSeedBatch, "Frames processed together (4n+1). Larger = better temporal consistency, more VRAM.\n13–21 suits 24 GB with the 7B model at 1080p; lower it if you get \"out of memory\".");
            tips.SetToolTip(cbSeedColor, "Brings the colours of the result back to the source. LAB is recommended.");
            tips.SetToolTip(tbSeedInNoise, "Adds noise to the input before restoration: can reduce artefacts on very high-resolution sources. 0 = off.");
            tips.SetToolTip(tbSeedLatNoise, "Adds noise in the latent space: more generated detail, less faithful. 0 = off.");
            tips.SetToolTip(nuSeedChunk, "Long videos are processed in chunks of this many frames to limit RAM use. 0 = whole video at once.");
            tips.SetToolTip(nuSeedOverlap, "Frames shared between batches / chunks and blended, to hide seams.");
            tips.SetToolTip(nuSeedSwap, "Moves this many transformer blocks to RAM to save VRAM (0–32 for 3B, 0–36 for 7B). Slower. 0 = off.");
            tips.SetToolTip(cbSeedAttn, "Attention backend. SDPA always works; Flash / Sage are faster but must be installed in the SeedVR2 environment.");
            tips.SetToolTip(txtSeedGpu, "CUDA device index (0 = first GPU). Several GPUs: 0,1");

            EventHandler upd = (s2, e2) => UpdateSeedRows();
            chkSeed.CheckedChanged += upd; cbSeedModel.SelectedIndexChanged += upd; cbSeedSize.SelectedIndexChanged += upd;
            stack.Controls.Add(secSeed);
            UpdateSeedRows();
        }

        /// The CRF row is shown when the video codec is H.264 / H.265 NVENC (chosen, or the format's own codec).
        void UpdateNvCrfRow()
        {
            if (nuNvCrf == null) return;
            var f = CurrentFormat;
            string key = cbVCodec.SelectedItem is Choice ? ((Choice)cbVCodec.SelectedItem).Key : "auto";
            if (key == "auto" && f != null) key = f.VCodec;
            bool on = f != null && f.Kind == MediaKind.Video && CommandBuilder.IsNvencCrf(key) && cbVCodec.Enabled;
            nuNvCrf.Visible = lblNvCrf.Visible = lblNvCrfHint.Visible = on;
            if (!on) return;
            int v = (int)nuNvCrf.Value;
            bool h265 = key.Contains("265") || key.Contains("hevc");
            lblNvCrfHint.Text = nuVBitrate.Value > 0 ? "The bitrate above 0 is used instead of the CRF."
                              : v == 0 ? "Automatic: CQ set by Quality. Type a value (1–51) to choose it yourself."
                              : "CQ " + v + " — " + (v <= (h265 ? 20 : 18) ? "very high quality, large file" : v <= (h265 ? 26 : 23) ? "high quality (recommended range)" : v <= (h265 ? 32 : 28) ? "medium quality, smaller file" : "low quality, very small file");
            lblNvCrfHint.ForeColor = nuVBitrate.Value > 0 ? Theme.Accent1 : Theme.Muted;
        }

        void UpdateSeedRows()
        {
            if (secSeed == null) return;
            bool on = chkSeed.Checked;
            bool img = category == MediaKind.Image;
            foreach (var c in seedRows) if (c != null) c.Visible = on;
            foreach (var c in seedVidRows) if (c != null) c.Visible = on && !img;
            // also hide the labels of video-only rows
            foreach (var c in seedVidRows) { var p = c.Parent as TableLayoutPanel; if (p != null) { var l = p.GetControlFromPosition(0, p.GetRow(c)); if (l != null && l != c) l.Visible = on && !img; } }
            lblSeedStatus.Text = SeedEngine.Status();
            lblSeedStatus.ForeColor = SeedEngine.Ready ? Theme.Success : Theme.Accent1;
            var m = cbSeedModel.SelectedItem as SeedEngine.Model;
            bool have = m != null && SeedEngine.Downloaded().Contains(m.File);
            lblSeedInfo.Text = (m == null ? "" : (have ? "✓ " + SeedEngine.Short(m.File) + " is downloaded." : "↓ " + SeedEngine.Short(m.File) + " will download on the first conversion.")) +
                               "\nRuns after the Denoiser and filters, before encoding. Scale / OptiScaler and RTX options must be off." +
                               " Needs an NVIDIA GPU (CUDA); slow — minutes per minute of video with 7B.";
        }

        SeedSettings SeedSnapshot(MediaKind k)
        {
            var v = new SeedSettings();
            v.Enabled = chkSeed.Checked && (k == MediaKind.Video || k == MediaKind.Image);
            var m = cbSeedModel.SelectedItem as SeedEngine.Model;
            v.Model = m != null ? m.File : SeedEngine.DefaultModel;
            v.SizeIdx = Math.Max(0, cbSeedSize.SelectedIndex);
            v.BatchSize = SeedEngine.Batches[Math.Max(0, cbSeedBatch.SelectedIndex)];
            v.Color = SeedEngine.Colors[Math.Max(0, cbSeedColor.SelectedIndex)];
            v.InputNoise = tbSeedInNoise.Value / 100.0;
            v.LatentNoise = tbSeedLatNoise.Value / 100.0;
            v.Seed = (int)nuSeedSeed.Value;
            v.ChunkSize = (int)nuSeedChunk.Value;
            v.Overlap = (int)nuSeedOverlap.Value;
            v.BlocksToSwap = (int)nuSeedSwap.Value;
            v.Attention = SeedEngine.Attentions[Math.Max(0, cbSeedAttn.SelectedIndex)];
            v.Gpu = txtSeedGpu.Text.Trim();
            v.VaeTiled = chkSeedTiled.Checked;
            v.Offload = chkSeedOffload.Checked;
            v.Compile = chkSeedCompile.Checked;
            return v;
        }

        void LoadSeedSettings(Func<string, int, int> gi)
        {
            string v;
            if (settings.TryGetValue("seed_dir", out v)) SeedEngine.Dir = v;
            if (SeedEngine.ScriptDir(SeedEngine.Dir) == null && SeedEngine.ScriptDir(SeedEngine.DefaultInstallDir) != null) SeedEngine.Dir = SeedEngine.DefaultInstallDir;
            Func<ComboBox, string, int, int> sel = (c, key, def) => c.SelectedIndex = Math.Max(0, Math.Min(c.Items.Count - 1, gi(key, def)));
            chkSeed.Checked = gi("seed_on", 0) == 1;
            nuNvCrf.Value = Math.Max(0, Math.Min(51, gi("nv_crf", 0)));
            if (settings.TryGetValue("seed_model", out v)) { int mi = SeedEngine.Models.FindIndex(x => x.File == v); if (mi >= 0) cbSeedModel.SelectedIndex = mi; }
            sel(cbSeedSize, "seed_size", 1);
            { int bi = Array.IndexOf(SeedEngine.Batches, gi("seed_batch", 13)); cbSeedBatch.SelectedIndex = bi >= 0 ? bi : Array.IndexOf(SeedEngine.Batches, 13); }
            sel(cbSeedColor, "seed_color", 0);
            tbSeedInNoise.Value = Math.Max(0, Math.Min(100, gi("seed_innoise", 0)));
            tbSeedLatNoise.Value = Math.Max(0, Math.Min(100, gi("seed_latnoise", 0)));
            nuSeedSeed.Value = Math.Max(0, Math.Min(999999999, gi("seed_seed", 42)));
            nuSeedChunk.Value = Math.Max(0, Math.Min(2000, gi("seed_chunk", 165)));
            nuSeedOverlap.Value = Math.Max(0, Math.Min(16, gi("seed_overlap", 3)));
            nuSeedSwap.Value = Math.Max(0, Math.Min(36, gi("seed_swap", 0)));
            sel(cbSeedAttn, "seed_attn", 0);
            if (settings.TryGetValue("seed_gpu", out v) && v.Length > 0) txtSeedGpu.Text = v;
            chkSeedTiled.Checked = gi("seed_tiled", 0) == 1;
            chkSeedOffload.Checked = gi("seed_offload", 0) == 1;
            chkSeedCompile.Checked = gi("seed_compile", 0) == 1;
            UpdateSeedRows();
        }

        void SaveSeedSettings()
        {
            settings["seed_dir"] = SeedEngine.Dir ?? "";
            settings["seed_on"] = chkSeed.Checked ? "1" : "0";
            settings["nv_crf"] = nuNvCrf.Value.ToString(CultureInfo.InvariantCulture);
            var m = cbSeedModel.SelectedItem as SeedEngine.Model;
            settings["seed_model"] = m != null ? m.File : SeedEngine.DefaultModel;
            settings["seed_size"] = cbSeedSize.SelectedIndex.ToString();
            settings["seed_batch"] = SeedEngine.Batches[Math.Max(0, cbSeedBatch.SelectedIndex)].ToString();
            settings["seed_color"] = cbSeedColor.SelectedIndex.ToString();
            settings["seed_innoise"] = tbSeedInNoise.Value.ToString();
            settings["seed_latnoise"] = tbSeedLatNoise.Value.ToString();
            settings["seed_seed"] = nuSeedSeed.Value.ToString(CultureInfo.InvariantCulture);
            settings["seed_chunk"] = nuSeedChunk.Value.ToString(CultureInfo.InvariantCulture);
            settings["seed_overlap"] = nuSeedOverlap.Value.ToString(CultureInfo.InvariantCulture);
            settings["seed_swap"] = nuSeedSwap.Value.ToString(CultureInfo.InvariantCulture);
            settings["seed_attn"] = cbSeedAttn.SelectedIndex.ToString();
            settings["seed_gpu"] = txtSeedGpu.Text.Trim();
            settings["seed_tiled"] = chkSeedTiled.Checked ? "1" : "0";
            settings["seed_offload"] = chkSeedOffload.Checked ? "1" : "0";
            settings["seed_compile"] = chkSeedCompile.Checked ? "1" : "0";
        }

        void ChooseSeedDir()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose the SeedVR2 folder (FFmpeg Forge install, or the ComfyUI-SeedVR2_VideoUpscaler folder that contains inference_cli.py)" })
            {
                if (!string.IsNullOrEmpty(SeedEngine.Dir) && Directory.Exists(SeedEngine.Dir)) d.SelectedPath = SeedEngine.Dir;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                SeedEngine.Dir = d.SelectedPath;
                string sc = SeedEngine.ScriptDir(d.SelectedPath);
                Log(sc != null ? "SeedVR2: " + sc + (SeedEngine.Python(sc) != null ? "  ·  Python: " + SeedEngine.Python(sc) : "  ·  no Python environment found") : "inference_cli.py not found in " + d.SelectedPath,
                    sc != null && SeedEngine.Python(sc) != null ? Theme.Success : Theme.Error);
                SaveSettings();
                UpdateSeedRows();
            }
        }

        void InstallSeed()
        {
            string root = SeedEngine.ScriptDir(SeedEngine.Dir) != null && SeedEngine.Python(SeedEngine.Script) == null ? SeedEngine.Dir : SeedEngine.DefaultInstallDir;
            if (MessageBox.Show(this,
                "FFmpeg Forge will install SeedVR2 in:\n" + root + "\n\n" +
                "It downloads (about 4 GB): the official SeedVR2 command line (github.com/numz/ComfyUI-SeedVR2_VideoUpscaler, Apache-2.0), " +
                "uv + Python 3.12 and PyTorch with CUDA 12.8. The models download later, on the first conversion.\n\n" +
                "A console window shows the progress. Continue?", "Install SeedVR2", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            try
            {
                string bat = SeedEngine.WriteInstaller(root);
                SeedEngine.Dir = root;
                SaveSettings();
                var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\"\"") { WorkingDirectory = root, UseShellExecute = true });
                Log("SeedVR2 installation started in a console window (" + root + ").", Theme.Accent1);
                if (p != null)
                    new Thread(() => { try { p.WaitForExit(); } catch { } UI(() => { UpdateSeedRows(); Log(SeedEngine.Ready ? "SeedVR2 is installed and ready." : "SeedVR2 installation did not finish — see the console window.", SeedEngine.Ready ? Theme.Success : Theme.Error); }); }) { IsBackground = true }.Start();
            }
            catch (Exception ex) { Log("Could not start the installation: " + ex.Message, Theme.Error); }
        }

        // =====================================================================
        //  Persistent settings
        // =====================================================================
        void LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsPath)) return;
                foreach (var line in File.ReadAllLines(settingsPath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) settings[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }

        void ApplySettings()
        {
            string v;
            chkSameDir.Checked = !settings.TryGetValue("samedir", out v) || v == "1";
            txtOutDir.Text = settings.TryGetValue("outdir", out v) ? v : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "FFmpeg Forge");
            txtOutDir.Enabled = btnBrowseOut.Enabled = !chkSameDir.Checked;
            if (settings.TryGetValue("filters", out v) && v.Length > 0)
                foreach (var part in v.Split('\u001F')) { var fe = FilterEntry.Parse(part); if (fe != null) filters.Add(fe); }
            if (settings.TryGetValue("graph", out v)) txtGraph.Text = v.Replace("\u001D", "\r\n");
            if (settings.TryGetValue("extra", out v)) txtExtraInputs.Text = v;
            if (settings.TryGetValue("chlayout", out v))
                for (int ci = 0; ci < cbCh.Items.Count; ci++) if (((Choice)cbCh.Items[ci]).Key == v) cbCh.SelectedIndex = ci;
            Func<string, int, int> gi = (key, def) => { string x; int r; return settings.TryGetValue(key, out x) && int.TryParse(x, out r) ? r : def; };
            cbUpmix.SelectedIndex = Math.Max(0, Array.IndexOf(UpmixCodes, Math.Max(0, Math.Min(5, gi("upmix_style", gi("upmix", 0))))));
            chkScale.Checked = gi("scale_on", 0) == 1;
            if (settings.TryGetValue("scale_type", out v))
                for (int si = 0; si < cbScaleType.Items.Count; si++) if (((Scaling.ScaleType)cbScaleType.Items[si]).Key == v) cbScaleType.SelectedIndex = si;
            cbScaleFactor.SelectedIndex = Math.Max(0, Math.Min(5, gi("scale_factor", 1)));
            tbScaleSharp.Value = Math.Max(0, Math.Min(100, gi("scale_sharp", 100)));
            chkScaleOpt.Checked = gi("scale_opt", 1) == 1;
            chkScaleFid.Checked = gi("scale_fid", 1) == 1;
            chkScale444.Checked = gi("scale_444", 1) == 1;
            loadingSettings = true;
            chkDn.Checked = gi("dn", 0) == 1;
            cbDnPreset.SelectedIndex = Math.Max(0, Math.Min(Denoise.Presets.Count - 1, gi("dn_preset", 0)));
            cbDnEngine.SelectedIndex = Math.Max(0, Math.Min(Denoise.Engines.Length - 1, gi("dn_engine", 0)));
            tbDnStrength.Value = Math.Max(0, Math.Min(200, gi("dn_strength", 100)));
            tbDnSharp.Value = Math.Max(0, Math.Min(100, gi("dn_sharp", 20)));
            chkDnTemporal.Checked = gi("dn_temporal", 1) == 1;
            chkDnAuto.Checked = gi("dn_auto", 1) == 1;
            loadingSettings = false;
            chkOpti.Checked = gi("opti", 0) == 1;
            cbOptiMult.SelectedIndex = Math.Max(0, Math.Min(OptiMults.Length - 1, gi("opti_mult", 1)));
            cbOptiKernel.SelectedIndex = Math.Max(0, Math.Min(Scaling.OptiKernels.Length - 1, gi("opti_kernel", 2)));
            tbOptiSharp.Value = Math.Max(0, Math.Min(100, gi("opti_sharp", 40)));
            chkOptiContrast.Checked = gi("opti_contrast", 0) == 1;
            UpdateScaleRows(); UpdateOptiRows();
            UpdateUpmixRow();
            chkVsr.Checked = gi("rtx_vsr", 0) == 1; chkHdr.Checked = gi("rtx_hdr", 0) == 1; chkDlss.Checked = gi("rtx_dlss", 0) == 1;
            cbVsrQ.SelectedIndex = Math.Max(0, Math.Min(3, gi("vsr_q", 3)));
            cbVsrScale.SelectedIndex = Math.Max(0, Math.Min(2, gi("vsr_scale", 0)));
            cbDlssMode.SelectedIndex = Math.Max(0, Math.Min(4, gi("dlss_mode", 3)));
            cbRtxEngine.SelectedIndex = Math.Max(0, Math.Min(3, gi("rtx_engine2", 0)));
            LoadSeedSettings(gi);
            nuHdrContrast.Value = Math.Max(0, Math.Min(200, gi("hdr_contrast", 125)));
            nuHdrSat.Value = Math.Max(0, Math.Min(200, gi("hdr_sat", 100)));
            nuHdrGray.Value = Math.Max(10, Math.Min(100, gi("hdr_gray", 25)));
            nuHdrLum.Value = Math.Max(400, Math.Min(10000, gi("hdr_lum", 1000)));
            if (settings.TryGetValue("vsr_expr", out v) && v.Length > 0) { vsrExpr = v; btnVsrParams.Text += "  (custom ✓)"; }
            if (settings.TryGetValue("hdr_expr", out v) && v.Length > 0) { hdrExpr = v; btnHdrParams.Text += "  (custom ✓)"; }
            if (settings.TryGetValue("dlss_expr", out v) && v.Length > 0) { dlssExpr = v; btnDlssParams.Text += "  (custom ✓)"; }
            UpdateRtxRows();
            RefreshFilterList(-1);
            MediaKind k = MediaKind.Video;
            if (settings.TryGetValue("category", out v)) { try { k = (MediaKind)Enum.Parse(typeof(MediaKind), v); } catch { } }
            if (k == MediaKind.Unknown) k = MediaKind.Video;
            cbQuality.SelectedIndex = 1;
            cbPreset.SelectedIndex = 3;
            SetCategory(k);
            UpdateExtractLabel();
            UpdateEmpty();
        }

        void SaveSettings()
        {
            try
            {
                settings["samedir"] = chkSameDir.Checked ? "1" : "0";
                settings["outdir"] = txtOutDir.Text;
                settings["category"] = category.ToString();
                settings["filters"] = string.Join("\u001F", filters.Select(f => f.Serialize()).ToArray());
                settings["graph"] = txtGraph.Text.Replace("\r\n", "\u001D").Replace("\n", "\u001D");
                settings["extra"] = txtExtraInputs.Text;
                settings["chlayout"] = cbCh.SelectedItem is Choice ? ((Choice)cbCh.SelectedItem).Key : "";
                settings["upmix_style"] = UpmixCodes[Math.Max(0, cbUpmix.SelectedIndex)].ToString();
                settings["scale_on"] = chkScale.Checked ? "1" : "0";
                settings["scale_type"] = cbScaleType.SelectedItem is Scaling.ScaleType ? ((Scaling.ScaleType)cbScaleType.SelectedItem).Key : "fsr";
                settings["scale_factor"] = cbScaleFactor.SelectedIndex.ToString();
                settings["scale_sharp"] = tbScaleSharp.Value.ToString();
                settings["scale_opt"] = chkScaleOpt.Checked ? "1" : "0";
                settings["scale_fid"] = chkScaleFid.Checked ? "1" : "0";
                settings["scale_444"] = chkScale444.Checked ? "1" : "0";
                settings["dn"] = chkDn.Checked ? "1" : "0";
                settings["dn_preset"] = cbDnPreset.SelectedIndex.ToString();
                settings["dn_engine"] = cbDnEngine.SelectedIndex.ToString();
                settings["dn_strength"] = tbDnStrength.Value.ToString();
                settings["dn_sharp"] = tbDnSharp.Value.ToString();
                settings["dn_temporal"] = chkDnTemporal.Checked ? "1" : "0";
                settings["dn_auto"] = chkDnAuto.Checked ? "1" : "0";
                settings["opti"] = chkOpti.Checked ? "1" : "0";
                settings["opti_mult"] = cbOptiMult.SelectedIndex.ToString();
                settings["opti_kernel"] = cbOptiKernel.SelectedIndex.ToString();
                settings["opti_sharp"] = tbOptiSharp.Value.ToString();
                settings["opti_contrast"] = chkOptiContrast.Checked ? "1" : "0";
                settings["rtx_vsr"] = chkVsr.Checked ? "1" : "0"; settings["rtx_hdr"] = chkHdr.Checked ? "1" : "0"; settings["rtx_dlss"] = chkDlss.Checked ? "1" : "0";
                settings["vsr_q"] = cbVsrQ.SelectedIndex.ToString(); settings["vsr_scale"] = cbVsrScale.SelectedIndex.ToString();
                settings["dlss_mode"] = cbDlssMode.SelectedIndex.ToString(); settings["rtx_engine2"] = cbRtxEngine.SelectedIndex.ToString();
                settings["hdr_contrast"] = nuHdrContrast.Value.ToString(); settings["hdr_sat"] = nuHdrSat.Value.ToString();
                settings["hdr_gray"] = nuHdrGray.Value.ToString(); settings["hdr_lum"] = nuHdrLum.Value.ToString();
                settings["vsr_expr"] = vsrExpr ?? ""; settings["hdr_expr"] = hdrExpr ?? ""; settings["dlss_expr"] = dlssExpr ?? "";
                if (!string.IsNullOrEmpty(Rtx.ProcessorPath)) settings["rtxproc"] = Rtx.ProcessorPath;
                SaveSeedSettings();
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllLines(settingsPath, settings.Select(kv => kv.Key + "=" + kv.Value.Replace("\r", "").Replace("\n", "")).ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (running)
            {
                if (MessageBox.Show(this, "A conversion is running. Quit and cancel it?", "FFmpeg Forge", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                { e.Cancel = true; return; }
                cancelRequested = true;
                Thread.Sleep(400);
            }
            SaveSettings();
        }
    }
}
