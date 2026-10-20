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
  <img alt="Version" src="https://img.shields.io/badge/version-1.1.2-FF7A18">
  <img alt="Portable" src="https://img.shields.io/badge/portable-FFmpeg%209.0.2%20included-E8344E">
</p>

<p align="center">
  <img src="docs/screenshot-main.png" alt="FFmpeg Forge main window" width="900">
</p>

<p align="center">
  <a href="../../releases/latest"><b>⬇️ Download FFmpeg Forge 1.1.2</b></a> ·
  Portable edition with FFmpeg 9.0.2 included, or the light edition (≈ 0.5 MB) for your own FFmpeg
</p>

---

## 🆕 What's new in 1.1.2

| | |
|---|---|
| 🧹 **Denoiser** | Neat Video-style noise reduction: auto noise profile, temporal 3D FFT filter, detail recovery and sharpening |
| 🎒 **Portable edition** | One folder with FFmpeg 9.0.2 *full*. Nothing to install, and settings stay in the folder |
| 🧪 **FSR 2 / FSR 3** | Experimental temporal upscaling, with optional frame generation ×2 |
| ✨ **OptiScaler** | Supersampled output scaling with OptiScaler's RCAS and downscalers |
| 🎶 **Spatial Music** | Immersive upmix that recreates the sound of Atmos music mixes, with Spatial Audio for headphones |
| 🎯 **FSR 1 = Lossless Scaling** | Same rendering as Lossless Scaling, plus 10-bit 4:4:4 "screen quality" encoding |

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
- **Automatic immersive upmix** turns a stereo track into a real multichannel mix. Choose Auto, **Spatial Music**, Music (classic), Cinema or Live concert:
  - the lead vocal or dialogue is steered to the **center speaker**;
  - instruments keep their place across front, wides and sides;
  - bass and kick drum go to the **LFE**;
  - reverb, room, cymbals and air fill the **wides and height speakers**, and the voice never goes up.

  <p align="center"><img src="docs/screenshot-upmix.png" alt="Immersive upmix" width="420"></p>

- **Spatial Music** is the default for audio. It recreates the sound of the Dolby Atmos music mixes on Apple Music:
  - the lead vocal stays dry in the center;
  - instruments open up to the sides and wides;
  - cymbals and air go overhead;
  - every surround and height speaker plays its own decorrelated reverb, with a vocal "plate" above you.
- **🎧 Spatial Audio for headphones:** pick it in *Channels* to render the 7.1.4 mix in 3D for any headphones. It uses measured HRTFs and a virtual listening room, and writes a normal stereo MP3 / AAC / FLAC file. A multichannel file played on headphones is only folded down to stereo, so choose this option for headphones.

  <p align="center"><img src="docs/screenshot-spatial-audio.png" alt="Spatial Audio for headphones" width="420"></p>

- **Atmos / DTS:X passthrough** copies an existing TrueHD Atmos, E-AC-3 JOC or DTS:X track bit-for-bit.
- FFmpeg cannot encode Atmos objects or DTS:X metadata, so new mixes are written as channel-based beds.

### 🔎 Scale: game-style upscalers for video
<p align="center"><img src="docs/screenshot-scale.png" alt="Scale section" width="420"></p>

- Tick **Scale** and choose an upscaler:
  - game upscalers: **AMD FSR 1** (EASU + RCAS), **NVIDIA Image Scaling (NIS)**, **Snapdragon GSR**;
  - temporal (experimental): **FSR 2** and **FSR 3** (+ frame generation ×2), with the motion estimated from the video, since a video file has no game motion vectors;
  - for anime: **Anime4K**;
  - for pixel art: **xBR**, **hqx**, **Integer**, **Nearest neighbor**, **Sharp bilinear**;
  - general purpose: **BCAS** (bicubic + CAS), **EWA Lanczos Sharp**, **Lanczos**, **Bicubic**, **Spline**.
- **Sharpness** defaults to 100 %, and **Optimized** (GPU through FFmpeg libplacebo / Vulkan) is ticked by default. Choose a ×1.5–×4 factor or scale to your target resolution.
- **Screen quality encode** and **Full chroma 4:4:4** are ticked by default, so the file keeps what the upscaler draws: 10-bit H.265 / AV1 / VP9, a CRF 4 steps finer, edge-preserving encoder tuning, and full colour resolution for H.265 / VP9, as on screen. Untick 4:4:4 for TVs and phones that cannot play it.
- The FSR, NIS and SGSR shaders are FFmpeg Forge ports of the official open-source SDKs (AMD MIT, NVIDIA MIT, Qualcomm BSD-3). They are embedded in the .exe, and their licenses are in `SHADER_LICENSES.txt`.

### 🧹 Denoiser: Neat Video-style noise reduction
<p align="center"><img src="docs/screenshot-denoiser.png" alt="Denoiser section" width="420"></p>

- Tick **Denoiser** for videos and images. It works like Neat Video:
  - an **auto noise profile** measures each video's luma and colour noise;
  - a **temporal 3D FFT** filter removes the noise using 3 frames;
  - **detail recovery** mixes a little of the original texture back in;
  - **luma sharpening** restores crispness.
- Presets: Auto, Light, Medium, Strong, Very strong, Film grain, Low light / smartphone, Animation and Old VHS.
- Quality: Balanced, High quality (+ non-local means) or Fast (hqdn3d). Strength goes from 0 to 200 % and sharpness from 0 to 100 %.
- It runs before Scale, OptiScaler and RTX.

### ✨ OptiScaler: supersampled upscaling
<p align="center"><img src="docs/screenshot-optiscaler.png" alt="OptiScaler section" width="420"></p>

- Tick **OptiScaler** for videos and images. It works on top of the **Scale** type, or with EWA Lanczos Sharp when Scale is off.
- It works like OptiScaler's **Output Scaling**:
  1. The upscaler renders at output × **1.25–3** (×1.5 by default).
  2. OptiScaler's **RCAS** sharpens that picture (40 % by default, with optional contrast adaptation).
  3. A downscaler brings it to the output size: **Bicubic**, **Catmull-Rom**, **Lanczos 2** (default), **Lanczos 3**, **Kaiser 2**, **Kaiser 3** or **Magic Kernel**.
- The result has finer edges, less aliasing and less ringing.
- It runs on the GPU through libplacebo / Vulkan.
- These shaders are a GLSL port of [OptiScaler](https://github.com/optiscaler/OptiScaler)'s open-source shaders and are licensed under **GPL-3.0**.
- OptiScaler's DLSS, XeSS, FSR 2+ and MAS need a game's motion vectors and depth buffer, so they cannot run on video files.

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

### 🎒 Portable edition (recommended)
1. Download **`FFmpegForge-1.1.2-Portable.rar`** from the [Releases](../../releases) page.
2. Extract it anywhere: a USB stick, `D:\Tools\` or another PC.
3. Double-click **`FFmpegForge.exe`**.

The archive already contains **FFmpeg 9.0.2 *full* (gyan.dev)**: `ffmpeg.exe`, `ffprobe.exe` and `ffplay.exe`. Nothing is installed, and nothing is written to the registry or `%APPDATA%`, because settings are kept in `Settings\`.

```
FFmpegForge-1.1.2-Portable\
├─ FFmpegForge.exe        FFmpeg Forge 1.1.2
├─ ForgeRTX.exe           NVIDIA RTX Video helper
├─ bin\                   ffmpeg.exe · ffprobe.exe · ffplay.exe (9.0.2 full)
├─ FFmpeg\                FFmpeg licence, build info, HTML docs, presets
├─ Settings\              created on first start
├─ Source\                FFmpeg Forge source code + build.bat
├─ portable.txt           portable mode switch (delete it to use %APPDATA%)
└─ README-PORTABLE.txt
```

### 🪶 Light edition (your own FFmpeg)
1. Download FFmpeg for Windows. A *full* build from [gyan.dev](https://www.gyan.dev/ffmpeg/builds/) or [BtbN](https://github.com/BtbN/FFmpeg-Builds/releases) gives the most formats, encoders and filters.
2. Extract it so that `ffmpeg.exe` and `ffprobe.exe` are in **`C:\FFmpeg\bin\`**.
3. Download **`FFmpegForge.rar`** from the [Releases](../../releases) page and extract it into `C:\FFmpeg\`.
4. Double-click **`FFmpegForge.exe`**.

> FFmpeg Forge looks for FFmpeg in this order:
> 1. next to the `.exe`;
> 2. in `bin\` next to the `.exe` (in portable mode, `bin\` always comes first);
> 3. in `C:\FFmpeg\bin\`;
> 4. in your `PATH`.
>
> You can also pick it manually with **“FFmpeg location…”**.

> Windows SmartScreen may warn you because the executable is not signed. Click **More info → Run anyway**.

**Requirements:**
- Windows 10 or 11 (.NET Framework 4.x is included).
- FFmpeg 5.0 or newer for the light edition.
- A Vulkan graphics driver for the GPU features (Scale, OptiScaler, FSR 2/3).
- An NVIDIA RTX GPU for RTX AI.

---

## 🛠️ Build from source

You don't need Visual Studio: Windows already includes the C# compiler.

```bat
cd FFmpegForge
build.bat
```

Or by hand:

`build.bat` compiles `src\*.cs` with the C# compiler included in Windows. It also embeds the shaders (`shaders\*.glsl`) and the HRTF data (`src\hrir_ircam.bin`) as resources.

| File | Role |
|---|---|
| `src/MainForm.cs` | Main window and conversion queue |
| `src/Engine.cs` | FFmpeg command builder, runner, progress parsing, format rules |
| `src/Catalog.cs` | Reads every muxer and encoder from `ffmpeg.exe` (cached) |
| `src/Filters.cs` | Filter library, parameter editor, presets |
| `src/Formats.cs` | Tuned format and codec presets |
| `src/Controls.cs` | Dark theme and custom controls |
| `src/Scaling.cs` | Scale, FSR 2/3, OptiScaler, screen-quality encoding |
| `src/Denoise.cs` | Denoiser: noise profile and filter chain |
| `src/Spatial.cs` | Spatial Music reverbs and binaural (HRTF) rendering |
| `src/Rtx.cs` · `rtxhelper/ForgeRTX.c` | NVIDIA RTX AI engines |
| `shaders/` | FSR 1, FSR 2/3, NIS, SGSR, Anime4K and OptiScaler GLSL shaders |

---

## 🙏 Credits

- **[FFmpeg](https://ffmpeg.org)** does all the work behind the scenes. FFmpeg Forge is a graphical front-end that calls `ffmpeg.exe` and `ffprobe.exe`. The portable edition ships the **unmodified FFmpeg 9.0.2 *full* build by [gyan.dev](https://www.gyan.dev/ffmpeg/builds/)** under the GNU GPL v3. Its licence and build information are in `FFmpeg\`, and its source code is at [ffmpeg.org](https://ffmpeg.org/download.html).
- **Shaders and data:**
  - AMD FidelityFX FSR (MIT), NVIDIA Image Scaling (MIT), Snapdragon GSR (BSD-3) and Anime4K (MIT);
  - [OptiScaler](https://github.com/optiscaler/OptiScaler) shaders (GPL-3.0);
  - IRCAM LISTEN HRTF data, as distributed with WebKit.
  - All the details are in `SHADER_LICENSES.txt`.
- FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project. FFmpeg Forge is an independent project, not affiliated with or endorsed by the FFmpeg project.
