#!/usr/bin/env bash
set -euo pipefail

# Build a self-contained AppImage of DAoCLogWatcher for linux-x64.
#
# Usage:
#   ./appimage/build-appimage.sh              # version read from the .csproj
#   VERSION=0.4.4 ./appimage/build-appimage.sh
#
# Requirements:
#   - .NET 10 SDK on PATH (or installed in ~/.dotnet)
#   - An SVG rasterizer: rsvg-convert, inkscape, or ImageMagick (magick/convert)
#   - appimagetool: taken from PATH / $APPIMAGETOOL, otherwise downloaded to
#     appimage/.cache/ automatically.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
APP_ID="io.github.zzerker.DAoCLogWatcher"
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

# --- resolve version ---------------------------------------------------------
if [ -z "${VERSION:-}" ]; then
	VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' DAoCLogWatcher.UI/DAoCLogWatcher.UI.csproj | head -1)"
fi
VERSION="${VERSION:-0.0.0}"
echo ">> Building DAoCLogWatcher $VERSION ($RID)"

# --- publish self-contained --------------------------------------------------
# Flatpak=true also disables the Velopack in-app updater, which cannot work in a
# read-only AppImage — replace the .AppImage file (or use AppImageUpdate) instead.
PUBLISH_DIR="$REPO_ROOT/publish/appimage"
rm -rf "$PUBLISH_DIR"
dotnet publish DAoCLogWatcher.UI/DAoCLogWatcher.UI.csproj \
	-c Release \
	-f net10.0 \
	-r "$RID" \
	--self-contained true \
	-p:Flatpak=true \
	-p:Version="$VERSION" \
	-o "$PUBLISH_DIR"

# --- assemble the AppDir -----------------------------------------------------
APPDIR="$REPO_ROOT/build/AppDir"
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" \
	"$APPDIR/usr/share/applications" \
	"$APPDIR/usr/share/metainfo" \
	"$APPDIR/usr/share/icons/hicolor/256x256/apps"

cp -a "$PUBLISH_DIR/." "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/DAoCLogWatcher.UI"

install -Dm755 "$SCRIPT_DIR/AppRun" "$APPDIR/AppRun"

# Desktop entry: both at the standard location and at the AppDir root (appimagetool
# reads the root one for the Name/Icon keys).
install -Dm644 "$REPO_ROOT/flatpak/$APP_ID.desktop" "$APPDIR/usr/share/applications/$APP_ID.desktop"
cp "$APPDIR/usr/share/applications/$APP_ID.desktop" "$APPDIR/$APP_ID.desktop"

# AppStream metainfo, with the release version/date injected (mirrors the CI flatpak job).
DATE="$(date +%Y-%m-%d)"
sed -e "s/__VERSION__/$VERSION/g" -e "s/__DATE__/$DATE/g" \
	"$REPO_ROOT/flatpak/$APP_ID.metainfo.xml" > "$APPDIR/usr/share/metainfo/$APP_ID.metainfo.xml"

# Icon: rasterize the SVG to 256x256 and place it where appimagetool expects it.
ICON_PNG="$APPDIR/usr/share/icons/hicolor/256x256/apps/$APP_ID.png"
rasterize "$REPO_ROOT/DAoCLogWatcher.UI/Assets/icon2.svg" "$ICON_PNG" 256
cp "$ICON_PNG" "$APPDIR/$APP_ID.png"
cp "$ICON_PNG" "$APPDIR/.DirIcon"

# --- locate appimagetool -----------------------------------------------------
# Pinned release + checksum so CI never executes an unverified moving binary.
APPIMAGETOOL_VERSION="1.9.1"
APPIMAGETOOL_SHA256="ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"

if [ -n "${APPIMAGETOOL:-}" ]; then
	TOOL="$APPIMAGETOOL"
elif command -v appimagetool >/dev/null 2>&1; then
	TOOL="appimagetool"
else
	TOOL="$SCRIPT_DIR/.cache/appimagetool-$APPIMAGETOOL_VERSION"
	if [ ! -x "$TOOL" ]; then
		echo ">> downloading appimagetool $APPIMAGETOOL_VERSION"
		mkdir -p "$SCRIPT_DIR/.cache"
		curl -sSL -o "$TOOL" \
			"https://github.com/AppImage/appimagetool/releases/download/$APPIMAGETOOL_VERSION/appimagetool-x86_64.AppImage"
		chmod +x "$TOOL"
	fi
	echo "$APPIMAGETOOL_SHA256  $TOOL" | sha256sum -c - >/dev/null || {
		echo "error: appimagetool checksum mismatch — delete $TOOL and retry" >&2
		exit 1
	}
fi

# --- build the AppImage ------------------------------------------------------
OUTPUT="${OUTPUT:-$REPO_ROOT/DAoCLogWatcher-$VERSION-x86_64.AppImage}"
export ARCH=x86_64
# Extract-and-run makes appimagetool work on hosts/CI without FUSE.
export APPIMAGE_EXTRACT_AND_RUN=1
"$TOOL" "$APPDIR" "$OUTPUT"

echo ">> built $OUTPUT"
