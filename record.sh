#!/usr/bin/env bash
# Record a proof MP4 of a scripted scenario (raid | kia) with Godot's movie writer under xvfb.
# Works on software Vulkan (llvmpipe). Godot writes an MJPEG AVI (its PNG writer spends most of the time
# compressing frames); ffmpeg transcodes it to H.264. A 35 s raid takes ~2 min.
set -euo pipefail
cd "$(dirname "$0")"
SCENARIO="${1:-raid}"
FRAMES=screenshots/video
OUT="screenshots/result/duckov_${SCENARIO}.mp4"
rm -rf "$FRAMES"; mkdir -p "$FRAMES"
xvfb-run -a -s '-screen 0 1280x720x24' godot --path . --audio-driver Dummy \
  --write-movie "$FRAMES/frame.avi" --fixed-fps 30 \
  --script test/Capture.cs ++ "--scenario=$SCENARIO" "--dir=screenshots/result/sequence" > "$FRAMES/capture.log" 2>&1
ffmpeg -y -loglevel error -i "$FRAMES/frame.avi" \
  -c:v libx264 -crf 20 -pix_fmt yuv420p -movflags +faststart "$OUT"
rm -f "$FRAMES"/frame.avi
echo "Wrote $OUT"
