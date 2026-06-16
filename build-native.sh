#!/usr/bin/env bash
# Build the native DataLens core (Linux) and vendor libdatalens.so into this package's
# Plugins folder. Run this whenever the core changes so the Unity binding picks up the new .so.
#
# Usage: ./build-native.sh [path-to-DataLens/Core]   (defaults to ~/Dev/GitHub/DataLens/Core)
set -euo pipefail

CORE="${1:-$HOME/Dev/GitHub/DataLens/Core}"
PKG_DIR="$(cd "$(dirname "$0")" && pwd)/com.heathen.datalensfoundation"
DEST="$PKG_DIR/Runtime/Plugins/Linux/x86_64"

if [[ ! -f "$CORE/CMakeLists.txt" ]]; then
  echo "error: DataLens Core not found at '$CORE'. Pass the path as arg 1." >&2
  exit 1
fi

cmake -S "$CORE" -B "$CORE/build" -DCMAKE_BUILD_TYPE=Release -DDATALENS_BUILD_TESTS=OFF -DDATALENS_BUILD_BENCH=OFF
cmake --build "$CORE/build" -j

mkdir -p "$DEST"
cp "$CORE/build/libdatalens.so" "$DEST/libdatalens.so"
echo "Installed libdatalens.so -> $DEST"
