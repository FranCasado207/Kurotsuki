#!/usr/bin/env bash
# Publishes Kurotsuki-Next for one runtime id into publish/<rid>/ (default linux-x64; others: win-x64, osx-arm64).
#   scripts/publish.sh [rid] [--self-contained]
# Framework-dependent by default (needs the .NET 10 runtime installed); --self-contained bundles it (much larger).
set -euo pipefail
cd "$(dirname "$0")/.."
rid="${1:-linux-x64}"
mode="--no-self-contained"
[[ "${2:-}" == "--self-contained" ]] && mode="--self-contained -p:PublishSingleFile=true"
dotnet publish src/Kurotsuki.App -c Release -r "$rid" $mode -o "publish/$rid"
if [[ "$rid" == linux-* ]]; then
  install -Dm644 packaging/kurotsuki-next.desktop "publish/$rid/kurotsuki-next.desktop"
  install -Dm644 packaging/icons/kurotsuki-next-256.png "publish/$rid/kurotsuki-next.png"
fi
echo "Published to publish/$rid"
