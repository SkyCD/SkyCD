#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Release}"
OUTPUT_ROOT="${2:-artifacts/publish}"

declare -a TARGETS=("win-x64" "linux-x64" "osx-x64")

for runtime in "${TARGETS[@]}"; do
  output="${OUTPUT_ROOT}/${runtime}"
  dotnet publish src/SkyCD.App/SkyCD.App.csproj \
    --configuration "${CONFIGURATION}" \
    --runtime "${runtime}" \
    --self-contained false \
    --output "${output}"
done

echo "Publish artifacts created under ${OUTPUT_ROOT}"
