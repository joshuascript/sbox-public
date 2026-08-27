#!/bin/bash
# Launch s&box editor on Linux with HarfBuzz + sdlhint (hint 1) preloads.
# HarfBuzz preload deduplicates hb_* between engine's libHarfBuzzSharp.so
# and system libharfbuzz via Qt xcb/fontconfig/portal.
# Hint 1 forces SDL_VIDEO_X11_EXTERNAL_WINDOW_INPUT=1 so the play widget
# gets XI2 input and game view / viewport mouse works.
# Trio QT_QPA_PLATFORM=xcb + SDL_VIDEODRIVER=x11 + SDL_VIDEO_X11_XINPUT2=0
# stabilizes: xcb avoids Wayland syncobj fatal, x11 keeps SDL/Qt in sync,
# XINPUT2=0 avoids X11_HandleXinput2Event null deref with external window.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME_DIR="$SCRIPT_DIR/game"
NATIVE_DIR="$GAME_DIR/bin/linuxsteamrt64"
PATCH_DIR="$SCRIPT_DIR/bootstrap-linux/patches"
HINT_SRC="$PATCH_DIR/sdlhint.c"
HINT_SO="$PATCH_DIR/libsdlhint.so"

EXE_NAME="${SBOX_EXE:-sbox-dev}"
EXE="$GAME_DIR/$EXE_NAME"
HARFBUZZ="${SBOX_HARFBUZZ:-$NATIVE_DIR/libHarfBuzzSharp.so}"

if [ ! -f "$HARFBUZZ" ]; then echo "error: HarfBuzz not found at $HARFBUZZ" >&2; exit 1; fi
if [ ! -x "$EXE" ]; then echo "error: $EXE not found at $EXE (run ./Bootstrap.sh / dotnet build)" >&2; exit 1; fi

# .NET: sbox-dev/sbox-launcher target net10.0 but game/dotnet only ships 8.0.29.
# Symlink system runtime if missing; also export DOTNET_ROOT as fallback.
if [ ! -d "$GAME_DIR/dotnet/shared/Microsoft.NETCore.App/10.0.10" ] && [ -d "/usr/lib64/dotnet/shared/Microsoft.NETCore.App/10.0.10" ]; then
  ln -sf /usr/lib64/dotnet/shared/Microsoft.NETCore.App/10.0.10 "$GAME_DIR/dotnet/shared/Microsoft.NETCore.App/10.0.10" 2>/dev/null || true
fi
if [ -z "${DOTNET_ROOT:-}" ]; then
  for cand in /usr/lib64/dotnet /usr/share/dotnet; do
    if [ -d "$cand/shared/Microsoft.NETCore.App" ]; then export DOTNET_ROOT="$cand"; break; fi
  done
fi
# Qt: only xcb ships (qt5_plugins/platforms/libqxcb.so). Force xcb
# even if session is wayland — QApp.def does this only if empty.
if [ "${QT_QPA_PLATFORM:-}" = "wayland" ] || [ -z "${QT_QPA_PLATFORM:-}" ]; then export QT_QPA_PLATFORM=xcb; fi
# Also force to xcb if wayland requested but not shipped
if [ "${QT_QPA_PLATFORM:-}" != "xcb" ] && [ ! -f "$NATIVE_DIR/qt5_plugins/platforms/libq${QT_QPA_PLATFORM}.so" ]; then export QT_QPA_PLATFORM=xcb; fi
# SDL: keep X11 in sync with Qt xcb. Wayland SDL + xcb Qt hits
# wp_linux_drm_syncobj_surface_v1 fatal then XWayland disconnect.
# User confirmed QT_QPA_PLATFORM=xcb SDL_VIDEODRIVER=x11 runs well.
if [ "${QT_QPA_PLATFORM:-}" = "xcb" ] && [ -z "${SDL_VIDEODRIVER:-}" ]; then export SDL_VIDEODRIVER=x11; fi
# XInput2 off: X11_HandleXinput2Event null deref on external window with hint 1.
# Trio QT_QPA_PLATFORM=xcb + SDL_VIDEODRIVER=x11 + SDL_VIDEO_X11_XINPUT2=0 stabilizes.
if [ -z "${SDL_VIDEO_X11_XINPUT2:-}" ]; then export SDL_VIDEO_X11_XINPUT2=0; fi

# Build sdlhint shim on demand
if [ -f "$HINT_SRC" ]; then
  if [ ! -f "$HINT_SO" ] || [ "$HINT_SRC" -nt "$HINT_SO" ]; then
    echo "building $(basename "$HINT_SO")"
    gcc -shared -fPIC -O2 -o "$HINT_SO" "$HINT_SRC" -ldl
  fi
  # SBOX_NO_SDLHINT=1 to run without the hint override
  if [ "${SBOX_NO_SDLHINT:-0}" != "1" ]; then
    export LD_PRELOAD="$HINT_SO${LD_PRELOAD:+:$LD_PRELOAD}"
  fi
fi

# HarfBuzz must stay first in LD_PRELOAD (hb_* interpose). SBOX_NO_HARFBUZZ=1 to skip.
if [ "${SBOX_NO_HARFBUZZ:-0}" != "1" ]; then
  export LD_PRELOAD="$HARFBUZZ${LD_PRELOAD:+:$LD_PRELOAD}"
fi
export LD_LIBRARY_PATH="$NATIVE_DIR${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"

if [ "${1:-}" = "--print-env" ] || { [ "${1:-}" = "--launcher" ] && [ "${2:-}" = "--print-env" ]; }; then
  if [ "${1:-}" = "--launcher" ]; then shift; fi
  echo "LD_PRELOAD=$LD_PRELOAD"
  echo "LD_LIBRARY_PATH=$LD_LIBRARY_PATH"
  echo "DOTNET_ROOT=$DOTNET_ROOT"
  echo "QT_QPA_PLATFORM=$QT_QPA_PLATFORM"
  echo "SDL_VIDEODRIVER=$SDL_VIDEODRIVER"
  echo "SDL_VIDEO_X11_XINPUT2=$SDL_VIDEO_X11_XINPUT2"
  echo "exec $EXE"
  exit 0
fi

# --launcher: run sbox-dev with no -project so Launcher.cs spawns sbox-launcher chooser
if [ "${1:-}" = "--launcher" ]; then
  shift
  cd "$GAME_DIR"
  exec "$EXE" "$@"
fi

# Default: sbox-dev without -project re-execs launcher and exits; inject sweeper for direct launch
if [[ "${*:-}" != *"-project"* ]]; then
  set -- -project "$GAME_DIR/samples/sweeper/.sbproj" "$@"
fi

cd "$GAME_DIR"
exec "$EXE" "$@"
