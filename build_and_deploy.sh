#!/usr/bin/env bash
set -euo pipefail

MOD_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" &>/dev/null && pwd)"
SERVER_DIR="${ULLR_STAGE_DIR:-$MOD_ROOT/../zedbook-valheim-server}"
REMOTE_HOST="${SKIHEIM_REMOTE_HOST:-}"
REMOTE_PLUGINS_DIR="${SKIHEIM_REMOTE_PLUGINS_DIR:-/opt/stacks/skiheim-server/config/bepinex/plugins}"
VALHEIM_CLIENT_DIR="${VALHEIM_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Valheim}"
PACKAGE_ID="JimmyLab-UllrSkiAndSnowboard-1.0.0"

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"

echo "=== [1/4] Building UllrSkiAndSnowboard.dll (Release) ==="
dotnet build "$MOD_ROOT/UllrSkiAndSnowboard.csproj" -c Release

DLL_PATH="$MOD_ROOT/bin/Release/net48/UllrSkiAndSnowboard.dll"
DIST_DIR="$MOD_ROOT/dist"
STAGE_DIR="$DIST_DIR/$PACKAGE_ID"

echo "=== [2/4] Packaging Thunderstore Archive ($PACKAGE_ID.zip) ==="
rm -rf "$STAGE_DIR"
mkdir -p "$STAGE_DIR/plugins"
cp "$MOD_ROOT/Thunderstore/manifest.json" "$STAGE_DIR/manifest.json"
cp "$MOD_ROOT/Thunderstore/README.md" "$STAGE_DIR/README.md"
cp "$MOD_ROOT/Thunderstore/icon.png" "$STAGE_DIR/icon.png"
cp "$DLL_PATH" "$STAGE_DIR/plugins/UllrSkiAndSnowboard.dll"

(
    cd "$STAGE_DIR"
    rm -f "$DIST_DIR/$PACKAGE_ID.zip"
    zip -qr "$DIST_DIR/$PACKAGE_ID.zip" manifest.json README.md icon.png plugins/
)
echo "Created: $DIST_DIR/$PACKAGE_ID.zip"

echo "=== [3/4] Installing into Local Valheim Client (if present) ==="
if [ -d "$VALHEIM_CLIENT_DIR/BepInEx/plugins" ]; then
    CLIENT_MOD_DIR="$VALHEIM_CLIENT_DIR/BepInEx/plugins/$PACKAGE_ID"
    mkdir -p "$CLIENT_MOD_DIR/plugins"
    cp -R "$STAGE_DIR/"* "$CLIENT_MOD_DIR/"
    echo "Installed to active plugins: $CLIENT_MOD_DIR"
fi

if [ -d "$VALHEIM_CLIENT_DIR/modpacks/downloads" ]; then
    DL_MOD_DIR="$VALHEIM_CLIENT_DIR/modpacks/downloads/$PACKAGE_ID"
    rm -rf "$DL_MOD_DIR"
    mkdir -p "$DL_MOD_DIR/plugins"
    cp "$STAGE_DIR/README.md" "$DL_MOD_DIR/README.md"
    cp "$STAGE_DIR/icon.png" "$DL_MOD_DIR/icon.png"
    cp "$STAGE_DIR/manifest.json" "$DL_MOD_DIR/manifest.saved"
    cp "$STAGE_DIR/plugins/UllrSkiAndSnowboard.dll" "$DL_MOD_DIR/plugins/UllrSkiAndSnowboard.dll"
    printf "1.0.0" > "$DL_MOD_DIR/.version"

    MODPACK_DIR="$VALHEIM_CLIENT_DIR/modpacks/UllrsPeakModpack"
    mkdir -p "$MODPACK_DIR"
    cat > "$MODPACK_DIR/manifest.saved" << 'EOF'
{
  "name": "UllrsPeakModpack",
  "version_number": "1.0.0",
  "website_url": "https://github.com/drewv-labs/UllrSkiAndSnowboard",
  "description": "Minimal modpack for Ullr's Peak Ski & Snowboard",
  "dependencies": [
    "denikson-BepInExPack_Valheim-5.4.2333",
    "ValheimModding-Jotunn-2.28.0",
    "JimmyLab-UllrSkiAndSnowboard-1.0.0"
  ]
}
EOF
    ln -sfn "$DL_MOD_DIR" "$MODPACK_DIR/$PACKAGE_ID"
    if [ -d "$VALHEIM_CLIENT_DIR/modpacks/downloads/ValheimModding-Jotunn-2.28.0" ]; then
        ln -sfn "$VALHEIM_CLIENT_DIR/modpacks/downloads/ValheimModding-Jotunn-2.28.0" "$MODPACK_DIR/ValheimModding-Jotunn-2.28.0"
    fi
    echo "Registered selectable modpack profile: UllrsPeakModpack"
fi

echo "=== [4/4] Optional Dedicated Server Sync ==="
if [ -n "$REMOTE_HOST" ] && ssh -o BatchMode=yes -o ConnectTimeout=3 "$REMOTE_HOST" "test -d '$REMOTE_PLUGINS_DIR'" 2>/dev/null; then
    rsync -az --delete "$STAGE_DIR/" "$REMOTE_HOST:$REMOTE_PLUGINS_DIR/$PACKAGE_ID/"
    echo "Synced $PACKAGE_ID to $REMOTE_HOST:$REMOTE_PLUGINS_DIR/$PACKAGE_ID"
elif [ -d "$SERVER_DIR" ]; then
    SERVER_PLUGINS_DIR="$SERVER_DIR/config/bepinex/plugins"
    mkdir -p "$SERVER_PLUGINS_DIR"
    rm -rf "$SERVER_PLUGINS_DIR/$PACKAGE_ID"
    cp -R "$STAGE_DIR" "$SERVER_PLUGINS_DIR/$PACKAGE_ID"
    echo "Staged mod at $SERVER_PLUGINS_DIR/$PACKAGE_ID"
else
    echo "Skipped (set SKIHEIM_REMOTE_HOST=user@host to auto-sync to a remote server)"
fi
echo "Done!"
