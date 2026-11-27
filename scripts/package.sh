#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-v1.0.0}"
OUTPUT_ROOT="${2:-dist}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

STAGE_NAME="CrackMeasure-win-x64"
STAGE_DIR="$OUTPUT_ROOT/$STAGE_NAME"
ZIP_NAME="CrackMeasure-${VERSION}-win-x64.zip"

rm -rf "$STAGE_DIR" "$OUTPUT_ROOT/$ZIP_NAME"
mkdir -p "$STAGE_DIR"

EXTRA_ARGS=()
if [[ -n "${NUGET_PACKAGES:-}" ]]; then
    EXTRA_ARGS+=(--no-restore --packages "$NUGET_PACKAGES")
fi

echo "[1/3] Publishing CrackMeasure (win-x64 self-contained single-file)..."
dotnet publish "src/CrackMeasure/CrackMeasure.csproj" \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=false \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    "${EXTRA_ARGS[@]}" \
    -o "$STAGE_DIR"

echo "[2/3] Copying samples and documentation..."
cp -r samples "$STAGE_DIR/samples"
cp README.md LICENSE "$STAGE_DIR/"

echo "[3/3] Creating release archive $OUTPUT_ROOT/$ZIP_NAME..."
(cd "$OUTPUT_ROOT" && zip -r -9 "$ZIP_NAME" "$STAGE_NAME") >/dev/null

echo "Done! Output directory: $STAGE_DIR"
echo "Release archive: $OUTPUT_ROOT/$ZIP_NAME"
