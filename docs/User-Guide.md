# DarkQuill User Guide

Desktop audio transcription — record, transcribe, and export with local AI.

## Contents

1. [Getting Started](#1-getting-started)
2. [Projects](#2-projects)
3. [Recording Audio](#3-recording-audio)
4. [Importing Audio Files](#4-importing-audio-files)
5. [Transcription](#5-transcription)
6. [Managing Recordings](#6-managing-recordings)
7. [Managing Transcriptions](#7-managing-transcriptions)
8. [Exporting](#8-exporting)
9. [Whisper Models](#9-whisper-models)
10. [Audio Settings](#10-audio-settings)
11. [Keyboard Shortcuts](#11-keyboard-shortcuts)
12. [File Storage](#12-file-storage)
13. [Tips & Best Practices](#13-tips--best-practices)
14. [Troubleshooting](#14-troubleshooting)

---

## 1. Getting Started

DarkQuill is a desktop application for capturing short audio recordings and transcribing them locally using OpenAI's Whisper speech-to-text model. Everything runs on your machine — no cloud services, no accounts, no internet required after initial model download.

### First Launch

When you first open DarkQuill, two things happen:

- **Project dialog** — You'll be asked to create or select a project. Projects group your recordings and transcriptions by name and date.
- **Model download** — If no Whisper models are found on your system, DarkQuill will prompt you to download them. Two models are downloaded: `ggml-base.bin` (fast, lower accuracy) and `ggml-large-v3-turbo.bin` (slower, higher accuracy). The base model is selected by default.

> **Note:** Model downloads happen once. The base model is approximately 142 MB and the large model is approximately 1.6 GB. Both are downloaded from Hugging Face.

## 2. Projects

Projects are the top-level organizer in DarkQuill. Each project has a name and scopes all recordings and transcriptions under that name, organized by date.

### Creating a Project

On the project dialog, type a name in the text field and click **Create**. Project names can contain letters, numbers, spaces, and hyphens. DarkQuill normalizes the name internally (lowercase, spaces become hyphens) for folder and file naming.

### Loading an Existing Project

If you've recorded today under an existing project, DarkQuill will list it in the project dialog. Select it and click **Load** (or double-click) to resume where you left off. All recordings and transcriptions for that project will be loaded into the panels.

### Switching Projects

Click the **New Session** button in the upper-right corner of the main window to return to the project dialog. You can create a new project or switch to a different one.

## 3. Recording Audio

The recording control panel sits at the top of the main content area. It contains the record/stop button, a real-time VU meter, and a duration timer.

### How to Record

- Click the **Record** button or press `F9` to start recording.
- Speak naturally — DarkQuill is designed for short clips of 2–5 sentences.
- Click **Stop** or press `Space` to finish.

While recording, you'll see the VU meter responding to your voice level and a timer counting elapsed time. Audio is captured at 16 kHz, 16-bit PCM mono — the format Whisper expects.

> **Tip:** The `Space` key only stops a recording that's already in progress. It won't interfere with typing in other applications.

### Auto-Stop

Recordings automatically stop at 5 minutes. If you need longer audio, break it into multiple clips — this also makes transcription more manageable.

### After Recording

Once you stop, the audio is saved as a WAV file and a new entry appears in the **Recordings** panel on the left with a status of **Pending**.

## 4. Importing Audio Files

In addition to recording directly, you can import existing audio files by dragging and dropping them onto the **Recordings** panel on the left side of the window.

### How to Import

Drag one or more audio files from File Explorer onto the recordings panel. A hint overlay is shown when the panel is ready to accept files, and a purple border highlight appears during the drag. Dropped files appear immediately in today's recording group as **Pending** entries, ready for transcription.

### Supported Formats

DarkQuill accepts any audio format supported by NAudio on Windows, including: `.wav`, `.mp3`, `.wma`, `.aac`, `.m4a`, `.aiff`, and `.flac`. Non-WAV files are automatically converted to the format Whisper requires before transcription.

### Important Notes

- **Files are not copied.** DarkQuill references the original file location. If you move or delete the source file, the recording entry will no longer be able to play or transcribe.
- **No duration limit.** Unlike live recordings (which cap at 5 minutes), imported files have no length restriction. Longer files will simply take more time to transcribe.
- **Auto-selected for transcription.** Dropped files are automatically added to the "Transcribe Selected" batch, so you can immediately click the button to begin processing them.

> **Tip:** This feature is great for transcribing voice memos, interview recordings, or audio exported from other applications.

## 5. Transcription

Transcription converts your audio recordings into text using the Whisper AI model running locally on your machine.

### Transcribing a Single Recording

Select a recording in the left panel with **Pending** status and click **Transcribe**, or press `Ctrl`+`Shift`+`T` to transcribe the most recent pending recording. A status bar will appear showing progress as the Whisper model loads and processes the audio.

### Batch Transcription

Select multiple recordings using `Ctrl`+Click or `Shift`+Click, then click **Transcribe Selected**. Recordings are processed one at a time in sequence. Already-transcribed recordings in the selection are skipped automatically.

### Transcription Status

| Status | Meaning |
|---|---|
| **Pending** | Recording exists but has not been transcribed yet |
| **Transcribing** | Whisper is currently processing this recording |
| **Complete** | Transcription finished — text is available in the main panel |
| **Failed** | An error occurred — you can retry the transcription |

Once a recording is marked Complete, its transcription appears in the main panel grouped by date. The recording's status in the left panel changes and its background darkens to visually distinguish it from pending items.

## 6. Managing Recordings

The left panel displays all recordings for the current project, grouped by date. Each date group can be expanded or collapsed by clicking the arrow toggle.

### Selection

Click a recording to select it. Use `Ctrl`+Click to add individual recordings to your selection, or `Shift`+Click to select a range. Selected recordings can be transcribed or deleted as a batch.

### Deleting Recordings

Each recording has a delete button (✖) on the right side. You can also delete an entire day's recordings at once using the delete button on the day group header. Deletions are soft-deletes — the files remain on disk but are hidden from the UI. This protects against accidental data loss.

## 7. Managing Transcriptions

The main content area displays transcriptions grouped by date. Each transcription card shows the recording timestamp, the transcribed text, and action buttons.

### Copying Text

Click the **Copy** button on any transcription card to copy its text to your clipboard. This is useful for quickly pasting transcribed content into other applications.

### Deleting Transcriptions

Each transcription card has a delete button (✖), and each day group header has a group delete button. Like recordings, transcription deletions are soft-deletes.

### Expanding and Collapsing Groups

Click the arrow (▶/▼) on a day group header to expand or collapse that group. This helps manage the view when you have many transcriptions across multiple days.

## 8. Exporting

Click the **Export** button in the upper-right corner to save all transcriptions for the current project as a Markdown file. You'll be prompted to choose a save location and filename. The exported file contains all transcriptions ordered chronologically, making it easy to review or share your work.

> **Tip:** The default filename includes your project name — for example, `airport-dialogue-study-export.md`.

## 9. Whisper Models

DarkQuill uses Whisper GGML models for speech-to-text. Two models are available out of the box:

| Model | Size | Speed | Accuracy |
|---|---|---|---|
| `ggml-base.bin` | ~142 MB | Fast | Good for clear speech |
| `ggml-large-v3-turbo.bin` | ~1.6 GB | Slower | Best accuracy, handles accents and noise well |

### Switching Models

Click the **Whisper Model** button below the transcription list to open the model selection dialog. Select a model from the dropdown and click **Apply**. The new model will be used for all subsequent transcriptions. DarkQuill loads the model on demand before each transcription batch, so switching is seamless.

### Adding More Models

You can place additional Whisper GGML model files (any `.bin` file) in the models folder. DarkQuill scans this folder for available models. The models folder location is shown in the model selection dialog.

> **Note:** GPU acceleration is available if you have an NVIDIA GPU with CUDA support and the `Whisper.net.Runtime.Cuda` package. Without it, DarkQuill uses CPU inference, which works well for the base model.

## 10. Audio Settings

Click the microphone/settings icon on the recording control panel to open Audio Settings. Here you can:

- **Select a microphone** — Choose from available audio input devices.
- **Adjust input level** — Use the slider to set the recording input level (0–100).
- **Test your mic** — Click Test to see a live level meter with your selected device before committing.

Your device selection and input level are saved automatically and persist between sessions.

## 11. Keyboard Shortcuts

| Shortcut | Action | When Available |
|---|---|---|
| `F9` | Start recording | Project loaded, not currently recording |
| `Space` | Stop recording | While recording is active |
| `Ctrl`+`Shift`+`T` | Transcribe most recent | Pending recordings exist |

> **Note:** `F9` and `Ctrl`+`Shift`+`T` are global hotkeys — they work even when DarkQuill is not in the foreground. `Space` is a local shortcut and only works when DarkQuill is focused.

## 12. File Storage

DarkQuill stores everything as files on your local disk — no database, no cloud. Understanding the folder structure can be helpful for backup or manual cleanup.

### Recordings

```
{recordings-root}/{project-name}-MM-DD-YYYY/
Example: recordings/airport-study-04-15-2026/airport-study-14-30-45.wav
```

Each project+date combination gets its own subfolder. WAV files are named with the recording start time.

### Transcriptions

```
{transcriptions-root}/{project-name}-MM-DD-YYYY.json
Example: transcriptions/airport-study-04-15-2026.json
```

Each JSON file contains an array of transcription entries for that project and date, including the full text and any speaker segments detected by Whisper.

### Settings

Application settings are stored in `settings.json` in the app data folder. This includes your selected audio device, input level, recordings/transcriptions paths, and selected Whisper model.

### Soft-Delete State

When you delete recordings or transcriptions in the UI, they aren't removed from disk. Instead, their IDs are tracked in `app-state.json`. The original files remain intact for safety.

## 13. Tips & Best Practices

- **Keep clips short.** Whisper works best with clips under 2 minutes. Short clips also transcribe faster and are easier to review.
- **Use a decent microphone.** A USB headset or dedicated microphone produces much cleaner transcriptions than a laptop's built-in mic.
- **Start with the base model.** It's fast and good enough for clear speech in quiet environments. Switch to the large model when you need better accuracy for accented speech, background noise, or technical terminology.
- **Organize by project.** Create a new project for each topic or session. This keeps your recordings grouped logically and makes exports cleaner.
- **Export regularly.** After a recording session, export your transcriptions to Markdown while the context is fresh. You can then edit the exported file as needed.
- **Check the VU meter.** Before a long session, do a quick test recording to make sure your levels are good. The meter should respond clearly to your voice without hitting the top constantly.

## 14. Troubleshooting

### No audio devices found

Make sure a microphone is connected and recognized by Windows. Check Windows Sound Settings to verify the device appears as an input device. Then restart DarkQuill.

### Transcription is very slow

If you're using the large model on CPU, transcription can take significantly longer than real-time. Consider switching to the base model for faster results, or install the CUDA runtime for GPU acceleration if you have a compatible NVIDIA GPU.

### Empty or garbled transcription

This usually means the recording audio is too quiet or too noisy. Open Audio Settings, check your input level, and do a test recording. Make sure you're using the correct microphone.

### Model download failed

Model files are downloaded from Hugging Face. If the download fails, check your internet connection and try again. DarkQuill uses temporary files during download, so a failed download won't leave corrupt model files behind.

### Recordings or transcriptions disappeared

If items vanished from the UI but you know the files exist, they may have been soft-deleted. Check `app-state.json` in the app data folder — clearing the deleted IDs list will restore them. Alternatively, the project dialog may have loaded a different project; verify you're in the correct project.

---

DarkQuill — Local audio transcription powered by Whisper
Created by [Ken Smith](mailto:whidbey88@gmail.com)
Licensed under the [GNU General Public License v3.0](https://www.gnu.org/licenses/gpl-3.0.html)
