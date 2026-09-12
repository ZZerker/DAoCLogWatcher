#!/usr/bin/env bash
set -euo pipefail

# Smoke-launches a built DAoCLogWatcher binary under xvfb and checks the AppLog startup
# header, then watches it for a fixed grace period.
#
# Guards against a native/managed SkiaSharp mismatch that crashed v0.6.0 on Linux before
# the first window ever opened: the process could stay alive for a while without ever
# writing a log line, so just checking "is the process still running" was not enough.
#
# Usage:
#   ./appimage/smoke-launch.sh <binary>

BIN="$1"
OUT="${RUNNER_TEMP:-/tmp}/smoke-launch.out"
LOG_DIR="$HOME/.config/DAoCLogWatcher/logs"

fail() {
	echo "::error::$1"
	cat "$OUT" 2>/dev/null || true
	local log
	log=$(ls "$LOG_DIR"/app-*.log 2>/dev/null | head -1 || true)
	if [ -n "$log" ]; then
		cat "$log"
	fi
	exit 1
}

chmod +x "$BIN"
rm -rf "$LOG_DIR"

# Detach stdio from the step's own pipe: otherwise the containerised appimage job hangs on
# docker exec waiting for EOF.
xvfb-run --auto-servernum "$BIN" > "$OUT" 2>&1 < /dev/null &
PID=$!

# PID is the xvfb-run wrapper, not the app itself. Match on the process name (comm, 15 chars,
# so "DAoCLogWatcher.UI" and "DAoCLogWatcher.AppImage" both read "DAoCLogWatcher."), never
# with -f: the full command line of this very script contains the binary path and pkill -f
# would kill the script inside its own trap.
trap 'pkill -TERM "^DAoCLogWatcher" 2>/dev/null || true; kill "$PID" 2>/dev/null || true; wait "$PID" 2>/dev/null || true' EXIT

# Poll for the AppLog startup header instead of a blind sleep: a hung or slow start should
# fail fast rather than wait out a fixed timer every run.
HEADER_SEEN=0
for _ in $(seq 1 60); do
	if ! kill -0 "$PID" 2>/dev/null; then
		fail "app exited before writing the AppLog startup header"
	fi
	LOG=$(ls "$LOG_DIR"/app-*.log 2>/dev/null | head -1 || true)
	if [ -n "$LOG" ] && grep -q "started on" "$LOG"; then
		HEADER_SEEN=1
		break
	fi
	sleep 1
done

if [ "$HEADER_SEEN" -ne 1 ]; then
	fail "no AppLog startup header within 60 s"
fi

# The SkiaSharp mismatch crashes at the first Skia call, which happens just after the
# header is written, so give it a fixed grace period before declaring success.
sleep 10
if ! kill -0 "$PID" 2>/dev/null; then
	fail "app exited after startup"
fi

if grep -Eq "ERROR \[Program.Main\]|AppDomain.UnhandledException" "$LOG"; then
	echo "::error::fatal startup error in AppLog"
	cat "$LOG"
	exit 1
fi

echo ">> smoke launch OK: $BIN started cleanly and stayed up"
exit 0
