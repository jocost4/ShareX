#!/usr/bin/env bash
set -e
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ -z "$DISPLAY" ] && command -v systemctl >/dev/null 2>&1; then
    eval $(systemctl --user show-environment 2>/dev/null | grep -E '^(DISPLAY|WAYLAND_DISPLAY|XAUTHORITY|XDG_RUNTIME_DIR)=' | sed 's/^/export /')
fi

if [ "$1" = "--restart" ] || [ "$1" = "-r" ]; then
    pkill -f 'ShareX.dll' 2>/dev/null || true
    sleep 0.5
    shift
fi

exec dotnet "$DIR/source/ShareX/bin/Debug/net10.0/ShareX.dll" "$@"
