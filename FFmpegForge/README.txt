FFmpeg FORGE — Universal Video • Audio • Image converter
=========================================================

RUN: double-click C:\FFmpeg\FFmpegForge.exe
(no install needed — it uses C:\FFmpeg\bin\ffmpeg.exe and ffprobe.exe)

HOW TO USE
 1. Drop files or whole folders into the window (or "Add files" / "Add folder").
 2. Pick VIDEO, AUDIO or IMAGE, then the output format.
 3. Adjust settings if needed (codec, quality, resolution, bitrate, trim…).
 4. Add filters if you want (see below).
 5. Click CONVERT. Files are written next to the originals (or into the chosen
    folder). Originals are never overwritten: "name (2).mp4" if the name exists.

FORMATS & CODECS
 - Every output format of your FFmpeg build is listed: tuned presets first, then
   "ALL … FORMATS OF YOUR FFMPEG BUILD" (read live from ffmpeg.exe — every muxer
   and every still-image type). Type the first letter of an extension to jump.
 - Codec lists = tuned presets + every video / audio encoder of your build.
 - Apple ProRes: 422 Proxy, 422 LT, 422, 422 HQ, 4444 and 4444 XQ (with alpha).
   Avid DNxHR: LB, SQ, HQ, HQX (10-bit), 444 (10-bit).
 - Formats with strict rules (DV, GXF, MXF D-10, H.261, AMV, GSM, AMR…) are fixed
   automatically (size, frame rate, sample rate, channels).

IMMERSIVE AUDIO (Audio settings > Channels)
 - Layouts: mono ... 7.1, 5.1.2, 5.1.4, 7.1.2, 7.1.4 (Dolby Atmos / DTS:X beds),
   7.1.6 and 9.1.6 (Dolby Atmos beds), 9.1.4 (DTS:X Pro), 22.2.
 - IMMERSIVE UPMIX (automatic when the source has fewer channels):
   lead vocal / dialogue steered to the center speaker (closer to you),
   instruments keep their stereo position (front, wides, sides), bass and
   kick to the LFE, reverb / room / cymbals / air to the wides and heights
   (the voice never goes up). Styles: Auto, Music, Cinema, Live concert, Off.
 - The codec switches automatically (PCM 24-bit / Opus) when the format's
   default codec cannot carry the channels (MKV, MOV, WAV, W64, CAF, MKA...).
 - Written as discrete channel beds. Above 8 channels use PCM 24-bit
   (WAV / W64 / CAF / MOV / MKV), Opus, Vorbis, WavPack or TTA.
 - FFmpeg cannot encode Dolby Atmos objects or DTS:X metadata. To KEEP an
   existing Atmos (TrueHD / E-AC-3 JOC) or DTS:X track, choose
   "Passthrough" (bit-for-bit copy, MKV recommended).

SCALE / ECHELLE (video & image output)
 - Tick "Scale" and pick a type: FSR 1 (AMD), NIS (NVIDIA), SGSR (Qualcomm),
   Anime4K, xBR, hqx, Integer, Nearest neighbor, Sharp bilinear, BCAS,
   EWA Lanczos Sharp, Lanczos, Bicubic, Spline.
 - Factor x1.5 ... x4, or "Use the Resolution setting".
 - Sharpness 100 % by default (FSR, NIS, SGSR, BCAS). "Optimized" (ticked by
   default) = GPU processing (FFmpeg libplacebo / Vulkan), high precision.
 - "Screen quality encode" (ticked by default): H.265 / AV1 / VP9 in 10-bit,
   CRF 4 steps finer, edge-preserving encoder tuning, final pixel conversion
   done by libplacebo. "Full chroma 4:4:4" (ticked by default) keeps full
   colour resolution like the screen for H.265 / VP9 (VLC, mpv and PCs play it; some TVs and
   phones cannot - untick it for those).
 - FSR 1 follows AMD's defaults (RCAS denoise off), like Lossless Scaling.
 - FSR / NIS / SGSR / Anime4K / EWA need a "full" FFmpeg build (libplacebo).
 - Open-source algorithms only (licenses: SHADER_LICENSES.txt). Lossless
   Scaling's own "LS1" is proprietary and is not included.

NVIDIA RTX AI (video output, RTX GPU)
 - Tick what you want: RTX Video Super Resolution (quality 1-4, x2/x3/x4),
   RTX TrueHDR (SDR -> HDR10: contrast, saturation, middle gray, peak nits),
   DLSS Super Resolution (NVIDIA NGX AI: DLAA, Quality, Balanced, Performance,
   Ultra Performance).
 - Built-in engine (default): "NVIDIA driver" = ForgeRTX.exe next to
   FFmpegForge.exe. Uses the RTX Video feature of your NVIDIA driver (like
   Chrome / VLC). Turn ON "RTX Video Enhancement > Super Resolution" (and
   "HDR" for TrueHDR) in the NVIDIA App / Control Panel; the VSR quality
   level is chosen there.
 - Engine 1: an FFmpeg build that includes NVIDIA's RTX Video filters
   (vsr_cuda, truehdr_cuda, isr_cuda, dlpp_drv_cuda). "All ... parameters"
   then opens every real option of the filter.
 - Engine 2: RTXVideoProcessor.exe (github.com/DrC0ns0le/RTXVideoProcessor,
   NVIDIA RTX Video SDK) for VSR x2 + TrueHDR. Put it in C:\FFmpeg\bin or use
   "Locate RTXVideoProcessor.exe".
 - DLSS is a game technology (needs game motion vectors): it only runs with
   an FFmpeg build that has NVIDIA's NGX filters. Otherwise it is skipped and
   RTX Video Super Resolution is used.
 - TrueHDR needs a 10-bit codec (H.265, AV1, VP9, ProRes...).

RESOLUTION (no limit)
 - Pick a preset (8K, DCI 4K, 4K, 1080p, vertical 9:16, square…) or type any
   width × height. 0 = automatic (keeps the aspect ratio).
 - Aspect handling when both are set: Stretch, Fit (black bars) or Fill (crop).

FILTERS
 - "+ Add filter…" / "Filter library" opens EVERY filter of your FFmpeg build
   (read live from ffmpeg.exe — 500+ in a full build). Search by name or
   description, filter by category (Video, Audio, Multi, Source, Sink, Timeline).
 - Each filter gets an automatic parameter editor: drop-down lists for choices,
   color pickers, file pickers, defaults and ranges shown, hover for help.
 - Timeline filters can be limited to a time range ("Apply only between").
 - "Presets ▾" = 45+ one-click recipes (B&W, sepia, sharpen, denoise, deinterlace,
   stabilize, crop 16:9 / 1:1 / 9:16, speed ×2, slow motion, text watermark,
   bass boost, noise removal, karaoke, 8D audio, pitch shift…).
 - Filters run in list order; untick to disable, ▲▼ to reorder, double-click to edit.
 - Multi-input filters (overlay, amix, hstack, concat…), sources and sinks go into
   the "Filtergraph" box. Add the extra files in "Extra inputs" ([1:v], [1:a]…)
   and name the final outputs [vout] / [aout].
   Example watermark:  [0:v][1:v]overlay=W-w-20:H-h-20[vout]
 - "Show command" prints (and copies) the exact FFmpeg command line.

TIPS
 - Select several rows: only those are converted.
 - Video -> Image: one frame, every frame, or one frame every N seconds.
 - Image -> Video: creates a video of the chosen length.
 - Audio -> Video: animated waveform video.  Audio -> Image: waveform picture.
 - "Stream copy" = instant lossless container change (no filters possible).
 - GPU codecs (NVIDIA / Intel / AMD) are much faster when your card supports them.
 - Right-click a file: open result, show in Explorer…

REBUILD (optional): FFmpegForge\build.bat (C# compiler included with Windows).
Settings are stored in %APPDATA%\FFmpegForge\settings.ini
