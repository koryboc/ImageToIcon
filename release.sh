#!/usr/bin/env bash
# Packages Release builds from bin/Release/<rid>/ into flat zip archives.
# Produces per-platform zips (linux-x64, win-x64, linux-arm64, win-arm64)
# and combined both-platforms-x64 / both-platforms-arm64 archives.
# Usage: $0 [--build]
#   --build  Run ./build.sh Release full before packaging.

set -euo pipefail

# ── Colors ────────────────────────────────────────────────────────────────────

nc='\033[0m'
red='\033[0;31m'
green='\033[0;32m'
yellow='\033[0;33m'
white='\033[1;37m'
ol='\033[53m'
ul='\033[4m'

# ── Args ──────────────────────────────────────────────────────────────────────

do_build=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --build) do_build=1 ;;
        --help|-h)
            echo -e "
${ul}${white}Usage${nc}
  $(basename "$0") [--build]

${ul}${white}Options${nc}
  ${green}--build${nc}     Run ./build.sh Release full before packaging.
  ${green}--help, -h${nc}  Show this help text and exit.
"
            exit 0 ;;
        *) echo -e "${red}[ERROR]${nc} Unknown argument: $1"; exit 1 ;;
    esac
    shift
done

# ── Dependency check ──────────────────────────────────────────────────────────

if command -v 7z &>/dev/null; then
    zip_impl="7z"
elif command -v zip &>/dev/null; then
    zip_impl="zip"
elif command -v python3 &>/dev/null; then
    zip_impl="python3"
else
    echo -e "${red}[ERROR]${nc} Executable not found: 7z, zip or python3"
    exit 1
fi

# ── Paths ─────────────────────────────────────────────────────────────────────

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BIN_DIR="$REPO_ROOT/bin/Release"
OUT_DIR="$BIN_DIR"
STAMP="$(date +%Y-%m-%d_%H-%M-%S)"

if [[ $do_build -eq 1 ]]; then
    "$REPO_ROOT/build.sh" Release full
fi

if [[ ! -d "$BIN_DIR" ]]; then
    echo -e "${red}[ERROR]${nc} Missing: $BIN_DIR (run ./build.sh Release full or pass --build)"
    exit 1
fi

mkdir -p "$OUT_DIR"

echo -e "\n${ul}${white}Packaging ${green}${ul}ImageToIcon [$STAMP]${white}${ul}...${nc}\n"

# ── Packaging ─────────────────────────────────────────────────────────────────

# Files that must never be shipped (runtime-generated user state).
EXCLUDE=(settings.json)

pack() {
    local label="$1"; shift
    local rids=("$@")

    local staging
    staging="$(mktemp -d)"
    trap 'rm -rf "$staging"' RETURN

    for rid in "${rids[@]}"; do
        local src="$BIN_DIR/$rid"
        if [[ ! -d "$src" ]]; then
            echo -e "${yellow}[SKIP]${nc} $label: $rid missing at $src"
            rm -rf "$staging"
            return
        fi
        # Flat copy, excluding runtime files.
        for f in "$src"/*; do
            local name
            name="$(basename "$f")"
            local skip=0
            for e in "${EXCLUDE[@]}"; do
                [[ "$name" == "$e" ]] && skip=1 && break
            done
            [[ $skip -eq 1 ]] && continue
            cp -r "$f" "$staging/"
        done
    done

    local zip_name="ImageToIcon_${label}_${STAMP}.zip"
    local zip_path="$OUT_DIR/$zip_name"
    if [[ "$zip_impl" == "7z" ]]; then
        (cd "$staging" && 7z a -tzip -bso0 -bsp0 "$zip_path" . >/dev/null)
    elif [[ "$zip_impl" == "zip" ]]; then
        (cd "$staging" && zip -qr "$zip_path" .)
    else
        (cd "$staging" && python3 -c "
import os, sys, zipfile
with zipfile.ZipFile(sys.argv[1], 'w', zipfile.ZIP_DEFLATED) as z:
    for root, _, files in os.walk('.'):
        for f in files:
            p = os.path.join(root, f)
            z.write(p, os.path.relpath(p, '.'))
" "$zip_path")
    fi
    echo -e "${white}Archive:${nc} ${green}$zip_name${nc}"

    rm -rf "$staging"
    trap - RETURN
}

pack "linux-x64"            linux-x64
pack "win-x64"              win-x64
pack "both-platforms-x64"   linux-x64 win-x64

pack "linux-arm64"          linux-arm64
pack "win-arm64"            win-arm64
pack "both-platforms-arm64" linux-arm64 win-arm64

echo -e "\n${green}${ol}$(basename "$0")${white}${ol} done!${nc}"
echo -e "  Output: ${green}$OUT_DIR/${nc}\n"
