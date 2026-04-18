# DarkQuill

A Windows desktop app that records short microphone clips and transcribes them locally using OpenAI's Whisper — no cloud, no API keys, no subscriptions. Built with C#/.NET 8 and Avalonia UI.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![UI](https://img.shields.io/badge/UI-Avalonia%2011-blueviolet)
[![License](https://img.shields.io/badge/license-GPLv3-blue)](LICENSE)

## What It Does

DarkQuill is project-based — you create named sessions, record audio clips, transcribe them, and export results to Markdown. Everything runs locally on your machine. No data leaves your computer.

- Record audio clips from your microphone (up to 5 minutes)
- Transcribe recordings locally using Whisper.net (CPU or CUDA GPU)
- Organize recordings into named projects grouped by date
- Copy transcriptions to clipboard or export to Markdown
- Switch between Whisper models for speed vs. accuracy tradeoffs
- Global hotkeys for hands-free recording (F9 to start, Space to stop)

---

## Whisper Models

On first launch, DarkQuill automatically downloads two Whisper GGML models from Hugging Face. No manual setup required — just launch the app and it handles the rest.

| Model | File | Size | Speed | Accuracy |
|-------|------|------|-------|----------|
| **Base (default)** | `ggml-base.bin` | ~142 MB | Fast | Good for clear speech in quiet environments |
| Large v3 Turbo | `ggml-large-v3-turbo.bin` | ~1.6 GB | Slower | Best accuracy — handles accents, background noise, and technical terms well |

You can switch between models at any time using the **Whisper Model** button in the transcription panel. Your selection persists across sessions.

### Adding More Models

Models are stored in `%AppData%\DarkQuill\models\`. You can drop any Whisper GGML-format `.bin` file into this folder and it will appear in the model selector. Compatible models are available from [ggerganov/whisper.cpp on Hugging Face](https://huggingface.co/ggerganov/whisper.cpp) — this includes tiny, small, medium, and distilled variants depending on your speed/accuracy needs.

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Language | C# 12 / .NET 8 |
| UI Framework | Avalonia UI 11.2.3 |
| MVVM Toolkit | CommunityToolkit.Mvvm 8.4.0 |
| Audio Capture | NAudio 2.2.1 |
| Transcription | Whisper.net 1.8.0 + Whisper.net.Runtime 1.8.0 |
| Serialization | System.Text.Json (built-in) |
| DI Container | Microsoft.Extensions.DependencyInjection 8.0.1 |
| Testing | xUnit 2.x, NSubstitute 5.x |

## Architecture

DarkQuill follows strict **MVVM** with constructor-injected services, interface-driven design, and file-based persistence (JSON + WAV). No database, no ORM, no cloud dependencies.

```
src/DarkQuill/
├── Models/          # Data models and DTOs
├── ViewModels/      # MVVM ViewModels with CommunityToolkit.Mvvm
├── Views/           # Avalonia AXAML views (minimal code-behind)
├── Services/        # Audio, transcription, storage, settings, export
├── Converters/      # Value converters for UI bindings
├── Controls/        # Custom controls (VU meter)
└── Themes/          # Colors, brushes, typography, component styles
```

All cross-ViewModel communication uses `WeakReferenceMessenger`. All I/O is async. All services have corresponding interfaces for testability.

## Building

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows (NAudio and global hotkeys are Windows-only; the UI framework is cross-platform)

### Build & Run

```bash
# Clone the repo
git clone https://github.com/yourusername/DarkQuill.git
cd DarkQuill

# Build
dotnet build

# Run
dotnet run --project src/DarkQuill
```

### Run Tests

```bash
dotnet test
```

Note: The test suite includes integration tests that download the Whisper tiny model on first run, so the initial test run may take several minutes.

## GPU Acceleration

DarkQuill attempts CUDA GPU acceleration automatically and falls back to CPU if unavailable. To use GPU acceleration:

1. Install the `Whisper.net.Runtime.Cuda` NuGet package
2. Ensure you have a compatible NVIDIA GPU with CUDA drivers installed

AMD and Vulkan GPUs are not currently supported by Whisper.net.

## Global Hotkeys (Windows)

| Key | Action |
|-----|--------|
| F9 | Start recording |
| Space | Stop recording |
| Ctrl+Shift+T | Transcribe most recent recording |

Hotkeys work globally — the app doesn't need to be focused.

## Data Storage

All user data is stored in `%AppData%/DarkQuill/`:

```
%AppData%/DarkQuill/
├── settings.json        # App settings (device, model selection, hotkeys)
├── app-state.json       # Soft-delete tracking
├── models/              # Whisper GGML model files
├── recordings/          # WAV audio files organized by project and date
└── transcriptions/      # JSON transcription results
```

No data is sent to any external service. Everything stays on your machine.

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE). You're free to use, modify, and distribute it — but any distributed copies or derivative works must also be released under GPLv3 with full source code.

Created by [Ken Smith](mailto:whidbey88@gmail.com)
