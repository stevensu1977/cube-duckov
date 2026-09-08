#!/usr/bin/env bash
# Close-up render of ducks and/or GLB props: tools/showcase.sh [extra godot user args, e.g. --models=a.glb,b.glb --no-ducks]
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_ROOT="$HOME/.dotnet"; export PATH="$HOME/.dotnet:$PATH"
mkdir -p screenshots/preview
xvfb-run -a -s '-screen 0 1280x720x24' godot --path . --audio-driver Dummy --fixed-fps 30 --script test/Showcase.cs ++ "$@" 2>&1 \
  | grep -E "ERROR|Showcase|Exception" | grep -v RID | head -10
