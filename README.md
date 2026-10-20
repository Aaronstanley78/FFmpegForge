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

### 🔊 Immersive audio: Dolby Atmos & DTS:X layouts
- **Channel layouts** from mono to 7.1, plus the immersive beds 5.1.2, 5.1.4, 7.1.2, 7.1.4 (Dolby Atmos / DTS:X), **7.1.6** and **9.1.6** (Dolby Atmos), 9.1.4 (DTS:X Pro) and 22.2.
- Up to 24 discrete channels in PCM 24-bit (WAV, W64, CAF, MOV, MKV), Opus, Vorbis, WavPack or TTA.
- **Automatic immersive upmix** turns a stereo track into a real multichannel mix. Choose Auto, Music, Cinema or Live concert:
  - the lead vocal or dialogue is steered to the **center speaker**;
  - instruments keep their place across front, wides and sides;
  - bass and kick drum go to the **LFE**;
  - reverb, room, cymbals and air fill the **wides and height speakers**, and the voice never goes up.

  <p align="center"><img src="docs/screenshot-upmix.png" alt="Immersive upmix" width="420"></p>

- **Atmos / DTS:X passthrough** copies an existing TrueHD Atmos, E-AC-3 JOC or DTS:X track bit-for-bit.
- FFmpeg cannot encode Atmos objects or DTS:X metadata, so new mixes are written as channel-based beds.

### 🔎 Scale: game-style upscalers for video
<p align="center"><img src="docs/screenshot-scale.png" alt="Scale section" width="420"></p>

- Tick **Scale** and choose an upscaler:
  - game upscalers: **AMD FSR 1** (EASU + RCAS), **NVIDIA Image Scaling (NIS)**, **Snapdragon GSR**;
  - for anime: **Anime4K**;
  - for pixel art: **xBR**, **hqx**, **Integer**, **Nearest neighbor**, **Sharp bilinear**;
  - general purpose: **BCAS** (bicubic + CAS), **EWA Lanczos Sharp**, **Lanczos**, **Bicubic**, **Spline**.
- **Sharpness** defaults to 100 %, and **Optimized** (GPU through FFmpeg libplacebo / Vulkan) is ticked by default. Choose a ×1.5–×4 factor or scale to your target resolution.
- **Screen quality encode** and **Full chroma 4:4:4** are ticked by default, so the file keeps what the upscaler draws: 10-bit H.265 / AV1 / VP9, a CRF 4 steps finer, edge-preserving encoder tuning, and full colour resolution for H.265 / VP9, as on screen. Untick 4:4:4 for TVs and phones that cannot play it.
- The FSR, NIS and SGSR shaders are FFmpeg Forge ports of the official open-source SDKs (AMD MIT, NVIDIA MIT, Qualcomm BSD-3). They are embedded in the .exe, and their licenses are in `SHADER_LICENSES.txt`.

### 🟩 NVIDIA RTX AI: tick what you want
<p align="center">
  <img src="docs/screenshot-rtx.png" alt="NVIDIA RTX AI section" width="420">
</p>

- **RTX Video Super Resolution** (AI upscaling and artifact clean-up) with quality 1–4 and an upscale factor of ×2, ×3 or ×4.
- **RTX TrueHDR** (SDR → HDR10) with contrast, saturation, middle gray and peak brightness (nits). The output is encoded as HDR10 with 10-bit color.
- **DLSS Super Resolution** (NVIDIA NGX AI) with five modes: DLAA, Quality, Balanced, Performance and Ultra Performance.
- **Three engines are supported:**
  - **NVIDIA driver (built-in, default):** `ForgeRTX.exe` uses the RTX Video feature of your NVIDIA driver through the D3D11 video processor, the same way Chrome, VLC and mpv do. No SDK or extra download is needed. Enable *RTX Video Enhancement* in the NVIDIA App;
  - an FFmpeg build that includes NVIDIA's RTX Video filters (`vsr_cuda`, `truehdr_cuda`, `isr_cuda`, `dlpp_drv_cuda`). All their real options open in the visual editor;
  - **[RTXVideoProcessor](https://github.com/DrC0ns0le/RTXVideoProcessor)**, which is built on the official NVIDIA RTX Video SDK and handles VSR ×2 and TrueHDR.
- Requires an NVIDIA RTX GPU and a recent driver. Each engine has its own GPU requirements.

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
