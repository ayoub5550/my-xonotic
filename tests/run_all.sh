#!/usr/bin/env bash
# Standalone structural tests without needing the Unity Editor:
#   1. Regenerates synthetic BSP fixtures (tools/content/pk3_tool.py fixture)
#   2. Compiles + runs the pure-.NET parser test driver with Mono
#   3. Runs the Python PK3-safety/fixture-format unit tests
#
# Usage: tests/run_all.sh [path/to/mono] [path/to/mcs] [external-map.bsp ...]
set -euo pipefail
cd "$(dirname "$0")/.."

MONO="${1:-mono}"
MCS="${2:-mcs}"
MAPS=("${@:3}")

echo "== 1/4: generating synthetic fixtures =="
python3 tools/content/pk3_tool.py fixture --out-dir tests/fixtures/generated

echo "== 2/4: compiling + running BSP parser tests (Mono, no Unity) =="
BUILD_DIR="$(mktemp -d)"
trap 'rm -rf "$BUILD_DIR"' EXIT
"$MCS" -target:exe -out:"$BUILD_DIR/ParserTests.exe" \
  Assets/MyXonotic/Runtime/Content/Bsp/*.cs \
  tests/csharp/ParserTests.cs
"$MONO" "$BUILD_DIR/ParserTests.exe" tests/fixtures/generated "${MAPS[@]}"

echo "== 3/4: compiling + running static MD3 parser tests (Mono, no Unity) =="
"$MCS" -target:exe -out:"$BUILD_DIR/Md3ParserTests.exe" \
  Assets/MyXonotic/Runtime/Content/Bsp/*.cs \
  Assets/MyXonotic/Runtime/Content/Md3/*.cs \
  tests/csharp/Md3ParserTests.cs
"$MONO" "$BUILD_DIR/Md3ParserTests.exe"

echo "== 4/4: running Python PK3-safety/fixture tests =="
python3 -m unittest discover -s tests/python -p "test_*.py" -v

echo "All test suites passed."
