#!/usr/bin/env bash
set -e

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ANVIL_DIR="$REPO_ROOT/anvil"
ANVIL_REPO="https://github.com/joshuascript/anvil"

if [ ! -d "$ANVIL_DIR/.git" ]; then
    echo "Anvil not found — cloning..."
    git clone -q "$ANVIL_REPO" "$ANVIL_DIR"
    echo ""
    python3 "$ANVIL_DIR/.anvil/render_readme.py" "$ANVIL_DIR/README.md"
    echo ""
else
    echo "Validating Anvil..."
    git -C "$ANVIL_DIR" fetch -q origin
    LOCAL=$(git -C "$ANVIL_DIR" rev-parse HEAD)
    REMOTE=$(git -C "$ANVIL_DIR" rev-parse origin/master)
    if [ "$LOCAL" != "$REMOTE" ]; then
        echo "Anvil out of date — updating..."
        git -C "$ANVIL_DIR" reset -q --hard origin/master
    else
        echo "Anvil up to date."
    fi
    echo ""
    echo "Anvil is ready."
    echo "  First run patch engine : bash anvil/launch/patch_engine.sh"
    echo "  To launch sbox, use : bash anvil/launch/launch-sbox.sh"
    echo ""
    echo "  Do not use the standard sbox executable in the game directory."
    echo ""
fi
read -r -p "Build managed artifacts now? [y/N] " answer
if [[ "$answer" =~ ^[Yy]$ ]]; then
    dotnet run --project ./engine/Tools/SboxBuild/SboxBuild.csproj -- build --config Developer
fi
