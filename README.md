# BPet

**Your AI companion on the desktop.**

BPet is a Windows 10/11 desktop pet written in C# and WPF. This repository now contains the first working foundation: a transparent draggable pet, system tray controls, modern Vietnamese-first settings, relationship/personality controls, local reminders, and provider abstractions for OpenAI and Google Gemini.

## Run

Requirements: Windows 10/11 and .NET 8 SDK.

```powershell
dotnet build -c Release
dotnet run
```

## What works in this initial version

- Transparent borderless desktop pet; double click opens chat, right click opens quick actions.
- Remembered position, Always on Top and Click Through.
- System tray: show, chat, settings, topmost/click-through toggles and exit.
- Settings UI: General, Character placeholder, AI Provider, AI Personality, Relationship and Pet Behavior.
- Vietnamese relationship presets: Trợ lý, Bạn bè, Dễ thương, Anh - Em, Em - Anh, Chồng - Vợ, Vợ - Chồng, Tôi - Bạn, Mình - Bạn, Sếp - Trợ lý and Custom.
- Prompt builder turns attitude, pronouns and sliders into one structured prompt.
- API-provider interface with OpenAI, Gemini, OpenAI-compatible endpoint and offline fallback.
- API keys protected with Windows DPAPI and never stored in `settings.json`.
- Chat surface with provider indicator and local fallback when AI is not configured.
- Local reminder scheduler that causes the pet to show a speech bubble.

## Architecture

- `Models.cs`: clean models for app, AI, personality, pet behaviour, profiles and reminders.
- `Services.cs`: settings store, encrypted credential vault, prompt builder, provider implementations, reminders and tray.
- `MainWindow`: desktop-pet shell and interaction state.
- `SettingsWindow`: consumer-style settings experience.
- `ChatWindow`: initial chat interaction.

## Deliberately staged next work

The repository is ready for the next implementation slices: sprite/manifest character packs and animation state machine, streaming rendering, conversation SQLite memory, reminder natural-language parsing, speech providers, profile save/switch controls, permission-gated desktop tools, and character ZIP/folder importer.

## Security

BPet does not execute shell commands, delete files, read arbitrary files or upload clipboard data. Future desktop tools must be routed through explicit permission checks. Do not commit API keys.
