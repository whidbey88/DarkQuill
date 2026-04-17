#!/usr/bin/env bash
# DarkQuill - Whisper GGML Model Downloader (Bash)
# Downloads Whisper models from Hugging Face and places them in the DarkQuill models folder.
#
# Usage:
#   ./download-models.sh                # Downloads the base model (default, required)
#   ./download-models.sh base           # Downloads ggml-base.bin (~148 MB)
#   ./download-models.sh turbo          # Downloads ggml-large-v3-turbo.bin (~1.6 GB)
#   ./download-models.sh all            # Downloads both models

set -euo pipefail

BASE_URL="https://huggingface.co/ggerganov/whisper.cpp/resolve/main"

# DarkQuill is Windows-only. This script is for Git Bash / MSYS2 on Windows.
MODELS_DIR="$APPDATA/DarkQuill/models"

download_model() {
    local key="$1"
    local file size url dest

    case "$key" in
        base)
            file="ggml-base.bin"
            size="~148 MB"
            ;;
        turbo)
            file="ggml-large-v3-turbo.bin"
            size="~1.6 GB"
            ;;
        *)
            echo "[ERROR] Unknown model: $key (use: base, turbo, all)"
            exit 1
            ;;
    esac

    url="$BASE_URL/$file"
    dest="$MODELS_DIR/$file"

    if [[ -f "$dest" ]]; then
        echo "[OK] $file already exists, skipping."
        return
    fi

    echo "Downloading $file ($size)..."
    echo "  From: $url"
    echo "  To:   $dest"
    echo ""

    if command -v curl &> /dev/null; then
        curl -L --progress-bar -o "$dest" "$url"
    elif command -v wget &> /dev/null; then
        wget --show-progress -O "$dest" "$url"
    else
        echo "[ERROR] Neither curl nor wget found. Install one and try again."
        exit 1
    fi

    echo "[OK] $file downloaded successfully."
}

MODEL="${1:-base}"

# Ensure models directory exists
mkdir -p "$MODELS_DIR"

echo ""
echo "DarkQuill Model Downloader"
echo "==========================="
echo "Models folder: $MODELS_DIR"
echo ""

case "$MODEL" in
    all)
        download_model "base"
        echo ""
        download_model "turbo"
        ;;
    base|turbo)
        download_model "$MODEL"
        ;;
    *)
        echo "Usage: $0 [base|turbo|all]"
        echo "  base   - ggml-base.bin (~148 MB) - default, required"
        echo "  turbo  - ggml-large-v3-turbo.bin (~1.6 GB) - higher accuracy"
        echo "  all    - download both models"
        exit 1
        ;;
esac

echo ""
echo "Done. Launch DarkQuill and select your model via the Whisper Model button."
