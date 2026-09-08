#!/usr/bin/env bash
# Quick look: run the scripted raid for N seconds under xvfb and drop stills into screenshots/preview.
# usage: tools/snap.sh [seconds=3] [scenario=raid] [period=4]
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
SECS="${1:-3}"; SCEN="${2:-raid}"; PERIOD="${3:-4}"
rm -rf screenshots/preview; mkdir -p screenshots/preview
xvfb-run -a -s '-screen 0 1280x720x24' godot --path . --audio-driver Dummy --fixed-fps 30 \
  --script test/Capture.cs ++ "--scenario=$SCEN" "--max=$SECS" "--period=$PERIOD" --dir=screenshots/preview \
  > screenshots/preview/log.txt 2>&1 || true
grep -E "ERROR|error|Exception|\[Arena\]|\[Game\]" screenshots/preview/log.txt | grep -v "RID" | head -20
ls screenshots/preview/*.png
