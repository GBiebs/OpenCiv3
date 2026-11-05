#!/bin/bash
# Builds the Windows installer for this fork: exports the game with Godot,
# gathers the folders it reads at run time beside the exe (as the CI's
# export does: Text with the asset credits and NOTICE, Assets, Lua,
# LICENSE), and packs them with Inno Setup (OpenCiv3.iss).
#
# Usage (Git Bash, from anywhere):
#   GODOT=<Godot 4.4.1 .NET console exe> bash packaging/build-windows-installer.sh <version>
# ISCC defaults to a per-user Inno Setup 6 (winget install JRSoftware.InnoSetup).
# Writes build/windows/OpenCiv3 (the game) and build/OpenCiv3-Setup-<version>.exe.
set -euo pipefail

VERSION=${1:?usage: build-windows-installer.sh <version, e.g. 2026.11.05>}
GODOT=${GODOT:?set GODOT to the Godot 4.4.1 .NET console executable}
ISCC=${ISCC:-$LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe}
REPO=$(cd "$(dirname "$0")/.." && pwd)
GAME=$REPO/build/windows/OpenCiv3

rm -rf "$GAME"
mkdir -p "$GAME"

# The game itself (Godot builds the C# and bundles .NET with it).
"$GODOT" --headless --path "$REPO/C7" --export-release "Windows Desktop" "$GAME/OpenCiv3.exe"
test -f "$GAME/OpenCiv3.exe" || { echo "The export made no OpenCiv3.exe"; exit 1; }

# What it reads beside the exe.
python "$REPO/.github/scripts/generate_asset_credits.py" "$REPO/C7"
cp -r "$REPO/C7/Text" "$GAME/Text"
cp "$REPO/NOTICE.md" "$GAME/Text/NOTICE.md"
cp "$REPO/LICENSE" "$GAME/LICENSE"
cp -r "$REPO/C7/Lua" "$GAME/Lua"
mkdir -p "$GAME/Assets"
(cd "$REPO/C7/Assets" && tar --exclude=.git --exclude=.github --exclude=.gitignore --exclude=.gitattributes -cf - .) | (cd "$GAME/Assets" && tar -xf -)
cp "$REPO/C7/icon.ico" "$GAME/icon.ico"
# (Never a developer's own settings or log.)
rm -f "$GAME/C7.ini" "$GAME/log.txt"

# (MSYS_NO_PATHCONV: Git Bash would take ISCC's /D and /O switches for paths.)
MSYS_NO_PATHCONV=1 "$ISCC" "/DAppVersion=$VERSION" "/DSourceDir=$(cygpath -w "$GAME")" "/O$(cygpath -w "$REPO/build")" "$(cygpath -w "$REPO/packaging/OpenCiv3.iss")"
echo "Built $REPO/build/OpenCiv3-Setup-$VERSION.exe"
