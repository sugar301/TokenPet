# 🐾 TokenPet — Your Desktop AI Companion

[![.NET](https://img.shields.io/badge/.NET-8.0-blueviolet)](https://dotnet.microsoft.com/)
[![WPF](https://img.shields.io/badge/WPF-Desktop-orange)]()
[![License](https://img.shields.io/badge/license-MIT-green)](./LICENSE)

[中文文档](./README.md) | English

> Love Codex's pet feature but don't want to switch editors? TokenPet gives you a desktop companion everywhere you go 🐱  
> Fully compatible with the **Codex Petdex spritesheet format** — just import your Codex pet packs!

## 🖼️ Demo

| | |
|:---:|:---:|
| ![Main UI](演示图/m1.png) | ![Feature Demo](演示图/m2.png) |
| *Desktop pet interactions* | *Token stats & settings panel* |

## 🔧 How It Works

TokenPet launches an HTTP proxy on `127.0.0.1`. Point your AI client's API address to the proxy port for zero-intrusion integration:

```
Client ──► http://127.0.0.1:11435/ds/v1/chat/completions
              │
              ▼  Strips /ds prefix, rewrites Host
         https://api.deepseek.com/v1/chat/completions
              │
              ▼  Passes response through, tracks tokens in real time
          Client ◄──  SSE streaming / Regular JSON
```

### Supported Response Formats
- **SSE (Server-Sent Events)** — streaming output, auto-detects `0\r\n\r\n` terminator
- **Regular JSON** — non-streaming requests, reads full response by Content-Length
- **Chunked / Gzip** — auto dechunk and decompress before parsing token usage

### Token Tracking
- Recursively parses `usage` objects in responses, compatible with `prompt_tokens` / `completion_tokens` / `input_tokens` / `output_tokens`
- Records by date + platform (DeepSeek / Qwen / OpenAI)
- Customizable proxy prefix and forward target, supports tracking across multiple platforms

## ✨ Features

- 🎬 **9 lively animations** — Idle, run, wave, jump, fail… every pet comes with its own spritesheet
- 🖱️ **Drag to interact** — hold and drag to move, auto-switches direction when moving left/right
- 😴 **Smart states** — after 18s of inactivity, sits and observes; after 45s, falls asleep
- 💬 **Status bubbles** — real-time popup bubbles above the pet showing token usage and request status
- 🎭 **Reactive expressions** — random animations on API requests, jumps when successful, sulks on failure
- 🔄 **Multi-pet switching** — import/export pet packs, one-click skin swap, keep as many pets as you like
- 📋 **System tray** — right-click menu for quick settings access, no taskbar clutter

## 🚀 Quick Start

```bash
# Clone
git clone https://github.com/sugar301/TokenPet.git
cd TokenPet\c\TokenPet

# Run
dotnet run

# Publish as single-file exe
dotnet publish -c Release -p:PublishSingleFile=true -o ./publish
```

## 🎨 Create Your Own Pet

### Directory Structure
```
my_pet.zip
├── pet.json
└── spritesheet.webp
```

### pet.json
```json
{
  "id": "my_pet",
  "displayName": "My Pet",
  "description": "A cute pixel cat",
  "spritesheetPath": "spritesheet.webp"
}
```

### Spritesheet Specs
| Parameter | Value |
|-----------|-------|
| Size | 1536 × 1872 px |
| Grid | 8 columns × 9 rows |
| Frame size | 192 × 208 px |

| Row | Action | Frames | FPS |
|-----|--------|--------|-----|
| 0 | Idle | 6 | 6 |
| 1 | Run Right | 8 | 12 |
| 2 | Run Left | 8 | 12 |
| 3 | Wave | 4 | 6 |
| 4 | Jump | 5 | 10 |
| 5 | Fail | 8 | 10 |
| 6 | Waiting | 6 | 2 |
| 7 | Sprint | 6 | 15 |
| 8 | Observe | 6 | 4 |

Zip it up and import in settings — done!

## 🛠️ Tech Stack

- .NET 8.0 + WPF
- SkiaSharp (WebP decoding)
- P/Invoke system tray
- Built-in HTTP proxy (SSE streaming + REST requests)

## 📄 License

MIT · Open source
