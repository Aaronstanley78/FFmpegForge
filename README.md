<p align="center">
  <img src="docs/logo.png" width="96" alt="FFmpeg Forge logo">
</p>

<h1 align="center">FFmpeg Forge</h1>

<p align="center">
  <b>Convert anything into everything.</b><br>
  A fast, dark-themed Windows converter for video, audio and images, powered by FFmpeg.
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6">
  <img alt=".NET" src="https://img.shields.io/badge/.NET%20Framework-4.x-512BD4">
  <img alt="Powered by FFmpeg" src="https://img.shields.io/badge/powered%20by-FFmpeg-007808">
  <img alt="Size" src="https://img.shields.io/badge/size-~200%20KB-FF7A18">
</p>

<p align="center">
  <img src="docs/screenshot-main.png" alt="FFmpeg Forge main window" width="900">
</p>

---

## ✨ Features

### 🎞️ Every format your FFmpeg supports
- **Video, audio and image output.** Tuned presets come first, then **every muxer and still-image type found in your `ffmpeg.exe`**. The list is read live from FFmpeg, so a newer build means more formats.
- **Every encoder** of your build is selectable, alongside hand-tuned presets for H.264, H.265, AV1, VP9, VP8 and others.
- **Apple ProRes:** 422 Proxy, 422 LT, 422, 422 HQ, 4444 and 4444 XQ (with alpha).
- **Avid DNxHR:** LB, SQ, HQ, HQX (10-bit) and 444 (10-bit). There is also **FFV1** for lossless archiving.
- **GPU encoders:** NVIDIA NVENC (H.264, H.265, AV1), Intel QuickSync and AMD AMF.
- **Formats with strict rules are fixed automatically** (frame size, frame rate, sample rate, channels). This covers DV, GXF, MXF D-10, H.261, AMV, GSM, AMR, DTS and more.

### 📐 Resolution without limits
- Presets range from 8K UHD, DCI 4K, 4K and 1080p to vertical 9:16 (Reels/Shorts/TikTok), square, 4:5 and cinema 2.39:1.
- You can also type **any width × height**. Use `0` for automatic to keep the aspect ratio.
- **Aspect handling:** *Stretch*, *Fit* (black bars) or *Fill* (crop).

### 🎛️ Every FFmpeg filter, with a visual editor
<p align="center">
  <img src="docs/screenshot-filters.png" alt="Filter library" width="820">
</p>

- The **filter library** lists every filter of your build (500+ in a full build). You can search and sort by category: Video, Audio, Multi-input, Source, Sink and Timeline.
- The **parameter editor is generated automatically** for each filter. It shows defaults and ranges, with drop-downs for choices, a color picker, a file picker and help on hover.
- **Timeline support** lets you apply a filter only between two timestamps.
- **45+ one-click presets** include black & white, sepia, cinematic look, sharpen, denoise, deinterlace, stabilize, crop 16:9 / 1:1 / 9:16, speed ×2, slow motion, text watermark, bass boost, noise removal, karaoke, 8D audio and pitch shift.
- **Filter chains** let you enable or disable each filter, reorder them, and edit by double-click.
- An **advanced `-filter_complex` box with extra inputs** handles multi-input filters such as watermark overlay, `amix`, `hstack` and `concat`.

### ⚡ Workflow
- Drag & drop files or **whole folders**. Batch conversion shows per-file and overall progress.
- **Cross-media conversions:**
  - video → image (one frame, every frame, or one every N seconds);
  - image → video;
  - audio → waveform video;
  - audio → waveform picture.
- **Trim** (start / end), rotation and mirroring, frame rate, bitrate, quality and encoding speed.
- Audio volume, **EBU R128 loudness normalization**, sample rate and channels.
- **Originals are never overwritten.** If a name is taken, the new file gets a suffix: `name (2).mp4`.
- **Show command** prints the exact FFmpeg command line and copies it to the clipboard.
- Plain-English error hints, a color log, and settings remembered between sessions.

---

## 📦 Installation

1. **Download FFmpeg** for Windows, for example a *full* build from [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) or [BtbN](https://github.com/BtbN/FFmpeg-Builds/releases).
2. Extract it so that `ffmpeg.exe` and `ffprobe.exe` are in **`C:\FFmpeg\bin\`**.
3. Download **`FFmpegForge.rar`** from the [Releases](../../releases) page and extract it into `C:\FFmpeg\`.
4. Double-click **`FFmpegForge.exe`**.

> FFmpeg Forge looks for FFmpeg in this order:
> 1. next to the `.exe`;
> 2. in `bin\` next to the `.exe`;
> 3. in `C:\FFmpeg\bin\`;
> 4. in your `PATH`.
>
> You can also pick it manually with **“FFmpeg location…”**.

> Windows SmartScreen may warn you because the executable is not signed. Click **More info → Run anyway**.

**Requirements:** Windows 10 or 11 (.NET Framework 4.x is included) and FFmpeg 5.0 or newer. A *full* build gives the most formats, encoders and filters.

---

## 🛠️ Build from source

You don't need Visual Studio: Windows already includes the C# compiler.

```bat
cd FFmpegForge
build.bat
```

Or by hand:

```bat
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /optimize+ ^
  /win32icon:src\app.ico /out:FFmpegForge.exe ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
```

| File | Role |
|---|---|
| `src/MainForm.cs` | Main window and conversion queue |
| `src/Engine.cs` | FFmpeg command builder, runner, progress parsing, format rules |
| `src/Catalog.cs` | Reads every muxer and encoder from `ffmpeg.exe` (cached) |
| `src/Filters.cs` | Filter library, parameter editor, presets |
| `src/Formats.cs` | Tuned format and codec presets |
| `src/Controls.cs` | Dark theme and custom controls |

---

## 🙏 Credits

- **[FFmpeg](https://ffmpeg.org)** does all the work behind the scenes. FFmpeg Forge is a graphical front-end that calls `ffmpeg.exe` and `ffprobe.exe`. It does not include or modify FFmpeg itself.
- FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project. FFmpeg Forge is an independent project, not affiliated with or endorsed by the FFmpeg project.
