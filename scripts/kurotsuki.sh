#!/usr/bin/env bash
DIR="$(cd "$(dirname "$0")" && pwd)"

if [ -z "${GDK_BACKEND:-}" ] && [ -n "${WAYLAND_DISPLAY:-}" ]; then
    export GDK_BACKEND=wayland
fi

exec "$DIR/runtime/bin/java" \
    --enable-native-access=javafx.graphics \
    -cp "$DIR/lib/*" \
    pt.yaku.App "$@"
