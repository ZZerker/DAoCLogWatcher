#!/usr/bin/env bash
set -euo pipefail

# Build a self-contained AppImage of DAoCLogWatcher for linux-x64 using Velopack (vpk).
#
# Velopack produces the .AppImage plus the release metadata (releases.linux.json, *.nupkg,
# RELEASES-linux) that the in-app updater consumes — the same updater used on Windows. Publishing
# those files to a GitHub release lets the app self-update by replacing the running AppImage.
#
# Usage:
#   ./appimage/build-appimage.sh              # version read from the .csproj
#   VERSION=0.4.4 ./appimage/build-appimage.sh
#
# Requirements:
#   - .NET 10 SDK on PATH (or installed in ~/.dotnet)
#   - vpk: the Velopack CLI. Installed automatically as a dotnet global tool if missing.
#   - An SVG rasterizer for the icon: rsvg-convert, inkscape, or ImageMagick (magick/convert).

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
RID="linux-x64"

cd "$REPO_ROOT"

# --- rasterize an SVG to a square PNG using whatever tool is available -------
rasterize() { # <svg> <out.png> <size>
	local svg="$1" out="$2" size="$3"
	if command -v rsvg-convert >/dev/null 2>&1; then
		rsvg-convert -w "$size" -h "$size" -o "$out" "$svg"
	elif command -v inkscape >/dev/null 2>&1; then
		inkscape "$svg" --export-type=png --export-filename="$out" -w "$size" -h "$size" >/dev/null 2>&1
	elif command -v magick >/dev/null 2>&1; then
		magick -background none "$svg" -resize "${size}x${size}" "$out"
	elif command -v convert >/dev/null 2>&1; then
		convert -background none "$svg" -resize "${size}x${size}" "$out"
	else
		echo "error: no SVG rasterizer found (install rsvg-convert, inkscape or imagemagick)" >&2
		exit 1
	fi
}

# --- locate the .NET SDK -----------------------------------------------------
if ! command -v dotnet >/dev/null 2>&1 && [ -x "$HOME/.dotnet/dotnet" ]; then
	export DOTNET_ROOT="$HOME/.dotnet"
	export PATH="$HOME/.dotnet:$PATH"
fi
command -v dotnet >/dev/null 2>&1 || { echo "error: dotnet SDK not found on PATH" >&2; exit 1; }
export PATH="$PATH:$HOME/.dotnet/tools"

# --- ensure the Velopack CLI (vpk) is available ------------------------------
if ! command -v vpk >/dev/null 2>&1; then
	echo ">> installing Velopack CLI (vpk)"
	dotnet tool install -g vpk
fi

# --- resolve version ---------------------------------------------------------
if [ -z "${VERSION:-}" ]; then
	VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' DAoCLogWatcher.UI/DAoCLogWatcher.UI.csproj | head -1)"
fi
VERSION="${VERSION:-0.0.0}"
echo ">> Building DAoCLogWatcher $VERSION ($RID)"

# --- publish self-contained --------------------------------------------------
# NOTE: no -p:Flatpak=true here — the AppImage keeps the Velopack updater compiled in (that flag is
# only for the Flatpak build, which updates through the store). vpk bundles the app files itself, so
# do not use PublishSingleFile.
PUBLISH_DIR="$REPO_ROOT/publish/appimage"
rm -rf "$PUBLISH_DIR"
dotnet publish DAoCLogWatcher.UI/DAoCLogWatcher.UI.csproj \
	-c Release \
	-f net10.0 \
	-r "$RID" \
	--self-contained true \
	-p:Version="$VERSION" \
	-o "$PUBLISH_DIR"

# --- icon --------------------------------------------------------------------
ICON_PNG="$REPO_ROOT/build/appimage-icon.png"
mkdir -p "$(dirname "$ICON_PNG")"
rasterize "$REPO_ROOT/DAoCLogWatcher.UI/Assets/icon2.svg" "$ICON_PNG" 256

# --- pack the AppImage + release metadata with Velopack ----------------------
OUTPUT_DIR="${OUTPUT_DIR:-$REPO_ROOT/releases/linux}"
rm -rf "$OUTPUT_DIR"
# Extract-and-run lets appimagetool (invoked by vpk) work on hosts/CI without FUSE.
export APPIMAGE_EXTRACT_AND_RUN=1
vpk pack \
	--packId DAoCLogWatcher \
	--packVersion "$VERSION" \
	--packDir "$PUBLISH_DIR" \
	--mainExe DAoCLogWatcher.UI \
	--packTitle "DAoC Log Watcher" \
	--packAuthors "ZZerker" \
	--categories "Game;Utility" \
	--icon "$ICON_PNG" \
	--outputDir "$OUTPUT_DIR"

echo ">> built Velopack Linux release in $OUTPUT_DIR:"
ls -1 "$OUTPUT_DIR"
