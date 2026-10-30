#!/usr/bin/env bash
# Builds the Linux Companion (src/MnMCompanion.Desktop, drawn with Avalonia) as one self-contained program: nothing else to install but,
# for the Map page, WebKitGTK (webkit2gtk-4.1). Usage: scripts/build-linux.sh [output folder] (default: publish/linux-x64)
set -euo pipefail
cd "$(dirname "$0")/.."
out="${1:-publish/linux-x64}"
dotnet publish src/MnMCompanion.Desktop -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "$out"
echo "Built $out/MnMCompanion"
