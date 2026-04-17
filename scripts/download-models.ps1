# DarkQuill - Whisper GGML Model Downloader (PowerShell)
# Downloads Whisper models from Hugging Face and places them in the DarkQuill models folder.
#
# Usage:
#   .\download-models.ps1                  # Downloads the base model (default, required)
#   .\download-models.ps1 -Model base      # Downloads ggml-base.bin (~148 MB)
#   .\download-models.ps1 -Model turbo     # Downloads ggml-large-v3-turbo.bin (~1.6 GB)
#   .\download-models.ps1 -Model all       # Downloads both models

param(
    [ValidateSet("base", "turbo", "all")]
    [string]$Model = "base"
)

$BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main"
$ModelsDir = Join-Path (Join-Path $env:APPDATA "DarkQuill") "models"

$Models = @{
    "base"  = @{ File = "ggml-base.bin";            Size = "~148 MB" }
    "turbo" = @{ File = "ggml-large-v3-turbo.bin";  Size = "~1.6 GB" }
}

function Download-Model {
    param([string]$Key)

    $info = $Models[$Key]
    $fileName = $info.File
    $size = $info.Size
    $url = "$BaseUrl/$fileName"
    $dest = Join-Path $ModelsDir $fileName

    if (Test-Path $dest) {
        Write-Host "[OK] $fileName already exists, skipping." -ForegroundColor Green
        return
    }

    Write-Host "Downloading $fileName ($size)..." -ForegroundColor Cyan
    Write-Host "  From: $url"
    Write-Host "  To:   $dest"
    Write-Host ""

    try {
        # Use BITS for large files (shows progress), fall back to Invoke-WebRequest
        if ($Key -eq "turbo") {
            Start-BitsTransfer -Source $url -Destination $dest -Description "Downloading $fileName"
        } else {
            $ProgressPreference = 'SilentlyContinue'
            Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing
        }
        Write-Host "[OK] $fileName downloaded successfully." -ForegroundColor Green
    }
    catch {
        Write-Host "[ERROR] Failed to download $fileName : $_" -ForegroundColor Red
        if (Test-Path $dest) { Remove-Item $dest -Force }
    }
}

# Ensure models directory exists
if (-not (Test-Path $ModelsDir)) {
    New-Item -ItemType Directory -Path $ModelsDir -Force | Out-Null
    Write-Host "Created models directory: $ModelsDir" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "DarkQuill Model Downloader" -ForegroundColor Magenta
Write-Host "===========================" -ForegroundColor Magenta
Write-Host "Models folder: $ModelsDir"
Write-Host ""

if ($Model -eq "all") {
    Download-Model "base"
    Write-Host ""
    Download-Model "turbo"
} else {
    Download-Model $Model
}

Write-Host ""
Write-Host "Done. Launch DarkQuill and select your model via the Whisper Model button." -ForegroundColor Cyan
