#!/usr/bin/env bash
set -e

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ANVIL_DIR="$REPO_ROOT/anvil"
ANVIL_REPO="https://github.com/joshuascript/anvil"

PASS="✅"

# ---------------------------------------------------------------------------
# Anvil clone / update
# ---------------------------------------------------------------------------

if [ ! -d "$ANVIL_DIR/.git" ]; then
    echo "Anvil not found — cloning..."
    git clone -q "$ANVIL_REPO" "$ANVIL_DIR"
    echo ""
    python3 "$ANVIL_DIR/.anvil/render_readme.py" "$ANVIL_DIR/README.md"
    echo ""
else
    echo "Syncing Anvil..."
    git -C "$ANVIL_DIR" fetch -q origin
    LOCAL=$(git -C "$ANVIL_DIR" rev-parse HEAD)
    REMOTE=$(git -C "$ANVIL_DIR" rev-parse origin/master)
    if [ "$LOCAL" != "$REMOTE" ]; then
        echo "  Anvil out of date — updating..."
        git -C "$ANVIL_DIR" reset -q --hard origin/master
        echo "  $PASS Updated to $(git -C "$ANVIL_DIR" rev-parse --short HEAD)."
    else
        echo "  $PASS Anvil up to date ($(git -C "$ANVIL_DIR" rev-parse --short HEAD))."
    fi
    echo ""
fi

# ---------------------------------------------------------------------------
# Validate
# ---------------------------------------------------------------------------

bash "$REPO_ROOT/validate_anvil.sh" "$REPO_ROOT"

# ---------------------------------------------------------------------------
# Managed build
# ---------------------------------------------------------------------------

read -r -p "Build managed artifacts now? [y/N] " answer
if [[ "$answer" =~ ^[Yy]$ ]]; then
    dotnet run --project ./engine/Tools/SboxBuild/SboxBuild.csproj -- build --config Developer
fi
