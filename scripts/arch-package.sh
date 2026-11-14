#!/usr/bin/env bash
# Builds the Arch package (KuroTsukiV2-<ver>-x86_64.pkg.tar.zst) into packaging/arch/ from the working tree.
#   scripts/arch-package.sh [makepkg args, e.g. -si to install afterwards]
set -euo pipefail
cd "$(dirname "$0")/.."
root="$PWD"
cd packaging/arch
name=$(sed -n 's/^pkgname=//p' PKGBUILD)
ver=$(sed -n 's/^pkgver=//p' PKGBUILD)
tar --exclude-vcs --exclude='./bin' --exclude='*/bin' --exclude='*/obj' --exclude='./publish' \
    --exclude='./packaging/arch/src' --exclude='./packaging/arch/pkg' --exclude='*.pkg.tar.*' --exclude='*.tar.gz' \
    --transform "s,^\.,$name-$ver," -czf "$name-$ver.tar.gz" -C "$root" .
makepkg -f "$@"
ls -1 ./*.pkg.tar.zst
