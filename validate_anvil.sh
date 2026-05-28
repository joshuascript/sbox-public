#!/usr/bin/env bash
# Validates the anvil installation: environment, dependencies, and patch binaries.
# Called by bootstrap.sh — can also be run standalone from the repo root:
#   bash validate_anvil.sh
set -e

REPO_ROOT="${1:-"$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"}"
ANVIL_DIR="$REPO_ROOT/anvil"

PASS="✅"
WARN="⚠️ "
FAIL="❌"
WARNINGS=0

# ---------------------------------------------------------------------------
# Environment
# ---------------------------------------------------------------------------

echo "Checking environment..."

# Python 3
if command -v python3 &>/dev/null; then
    PY_VER=$(python3 --version 2>&1)
    echo "  $PASS $PY_VER"
else
    echo "  $FAIL python3 not found — patch_engine.sh will not work."
    WARNINGS=$((WARNINGS + 1))
fi

# Game binary
if [ -f "$REPO_ROOT/game/sbox" ]; then
    echo "  $PASS game/sbox found."
else
    echo "  $WARN game/sbox not found — run the managed build or download game artifacts."
    WARNINGS=$((WARNINGS + 1))
fi

# Steam running
if pgrep -x steam &>/dev/null; then
    echo "  $PASS Steam is running."
else
    echo "  $WARN Steam is not running — some integrations may not work."
    WARNINGS=$((WARNINGS + 1))
fi

# inotify limit
INOTIFY_PATH="/proc/sys/fs/inotify/max_user_watches"
INOTIFY_TARGET=524288
if [ -r "$INOTIFY_PATH" ]; then
    INOTIFY_CURRENT=$(cat "$INOTIFY_PATH")
    if [ "$INOTIFY_CURRENT" -ge "$INOTIFY_TARGET" ]; then
        echo "  $PASS fs.inotify.max_user_watches = $INOTIFY_CURRENT (>= $INOTIFY_TARGET)."
    else
        echo "  $WARN fs.inotify.max_user_watches = $INOTIFY_CURRENT (< $INOTIFY_TARGET)."
        echo "        The launch scripts will prompt you to raise this at startup."
        WARNINGS=$((WARNINGS + 1))
    fi
fi

echo ""

# ---------------------------------------------------------------------------
# Patches
# ---------------------------------------------------------------------------

echo "Checking patches..."

PATCH_SRC="$ANVIL_DIR/patches"
PATCH_BIN="$ANVIL_DIR/patches/bin"
NEEDS_BUILD=0

if [ ! -d "$PATCH_BIN" ] || [ -z "$(ls "$PATCH_BIN"/*.so 2>/dev/null)" ]; then
    echo "  $WARN patches/bin/ is empty — patches have not been compiled."
    NEEDS_BUILD=1
else
    for src in "$PATCH_SRC"/*.c; do
        [ -f "$src" ] || continue
        name="$(basename "$src" .c)"
        so="$PATCH_BIN/$name.so"
        if [ ! -f "$so" ]; then
            echo "  $WARN $name.so missing."
            NEEDS_BUILD=1
        elif [ "$src" -nt "$so" ]; then
            echo "  $WARN $name.so is stale (source newer than binary)."
            NEEDS_BUILD=1
        else
            echo "  $PASS $name.so up to date."
        fi
    done
fi

if [ "$NEEDS_BUILD" -eq 1 ]; then
    echo ""
    read -r -p "  Build patches now? [Y/n] " answer
    if [[ ! "$answer" =~ ^[Nn]$ ]]; then
        bash "$ANVIL_DIR/patch_engine.sh"
    fi
fi

echo ""

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

if [ "$WARNINGS" -eq 0 ] && [ "$NEEDS_BUILD" -eq 0 ]; then
    echo "Anvil is ready."
else
    echo "Anvil is ready (with $WARNINGS warning(s) above)."
fi

echo ""
echo "  Compile patches : bash anvil/patch_engine.sh"
echo "  Launch sbox     : bash anvil/launch-sbox.sh"
echo ""
echo "  Do not use the game/sbox binary directly."
echo ""
