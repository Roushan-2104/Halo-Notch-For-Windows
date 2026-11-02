# Halo

A Dynamic Island–style notch for Windows: live activities, a media player, a file shelf, and
Circle-to-Search that answers with Google (AI Mode, Web or Lens) right inside the notch. No paid LLM:
Google does the answering, speech-to-text runs offline with Whisper, and OCR uses Windows' built-in engine.

## Using it

| Do this | Get this |
|---|---|
| Hover the notch | Expands: now playing (art, progress, controls), clock, battery, shelf tab |
| **Hold Right Alt** | Screen freezes; speak and/or circle anything; let go to search |
| Hold Right Alt, let go without doing anything | Screen stays frozen so you can circle at leisure (Esc cancels) |
| **Tap Right Alt** | Type-to-search box (sums like `18% of 2400` are answered instantly) |
| Drag files onto the notch | They're kept on the shelf; drag them back out any time |
| Volume keys | Halo's volume HUD instead of Windows' popup |

Say filters out loud ("…only from Reddit", "…from the past week", "…no junk", "…from the docs") or
toggle them as chips on the results card.

Halo also shows charging, low battery, Bluetooth headphones connecting, downloads in progress/finished,
and a detached bubble (like Dynamic Island's split) when two things are happening at once. It hides
itself for fullscreen games, videos and presentations, and sits below a top taskbar so it never covers it.

## Build and run

Requires Windows 10 2004+ / Windows 11, the .NET 8 SDK, and the WebView2 runtime (preinstalled on Windows 11).

```bash
dotnet test tests/Halo.Core.Tests
dotnet run --project src/Halo.App
```

Release build (what you'd install):

```bash
dotnet publish src/Halo.App -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o artifacts/Halo
```

On first launch Halo downloads the Whisper `base` model (~148 MB) to `%LOCALAPPDATA%\Halo\models`.
Settings, logs and the browser profile live in `%LOCALAPPDATA%\Halo`. The tray icon has the options
(start with Windows, pin over a top taskbar, voice language, volume popup, visualizer) and "Open logs".

## Layout

```
src/Halo.Core    pure logic, unit tested: springs, activity priority, query/filter building,
                 calculator, circle/scribble/tap selection analysis
src/Halo.App     WPF app
  Shell/         notch window (state machine + springs), notch shape, fullscreen watcher
  Views/         compact ears, expanded card, search bar, results card, answer card
  Search/        hotkey flow, screen freeze overlay, OCR, mic, Whisper
  Playback/      now playing (Windows media sessions), loopback audio visualizer
  SystemInfo/    volume, brightness, battery, Bluetooth, downloads
  Shelf/         shelf store, Explorer-quality icons
  Interop/       Win32: keyboard hook, window styles, capture
tests/           xUnit tests for Halo.Core
```

## Known limits

- **Google robot check.** On shared/mobile IP addresses Google sometimes shows "I'm not a robot" in
  the results card. Tick it once; the browser profile keeps the exemption.
- **Lens** uses the same upload endpoint as lens.google.com's own upload button. It isn't a public API
  and could change.
- **Brightness** pop-ups: Halo shows its HUD, but Windows' own brightness flyout can't be suppressed
  (brightness keys are handled by firmware, not the keyboard hook).
- **Notifications mirroring** needs MSIX packaging (planned with the Store build).
- Right Alt is reserved for search, so AltGr characters on non-US layouts won't type while Halo runs.
- First launch shows the notch ~3 s after start (WPF's one-time theme load).
