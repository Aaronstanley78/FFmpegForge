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
