# Halo

A Dynamic Island–style notch for Windows: live activities, a media player, a file shelf, and
Circle-to-Search that answers with Google (AI Mode, Web or Lens) right inside the notch. No paid LLM:
Google does the answering, speech-to-text runs offline with Whisper, and OCR uses Windows' built-in engine.
A slowly circling, multi-colour halo glows around the notch and reacts to what's happening: album colours
while music plays, green while charging, red for errors.

## Install

1. Download `Halo-v0.2.1-win-x64.zip` from [Releases](https://github.com/Roushan-2104/Halo-Notch-For-Windows/releases).
2. Unzip it anywhere (for example `%LOCALAPPDATA%\Programs\Halo`) and run `Halo.exe`.
3. Optional: tray icon › **Start with Windows**.

Needs Windows 10 2004+ or Windows 11 with the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
and the WebView2 runtime (both usually already installed on Windows 11). The build isn't code-signed yet,
so SmartScreen may ask you to confirm the first run.

## Using it

| Do this | Get this |
|---|---|
| Hover the notch | Expands: now playing (art, progress, controls), clock, battery, shelf tab |
| **Hold Right Alt** | Screen freezes; speak and/or circle anything; let go to search |
| Hold Right Alt, let go without doing anything | Screen stays frozen so you can circle at leisure (Esc cancels) |
| **Tap Right Alt** | Type-to-search box (sums like `18% of 2400` are answered instantly) |
| Drag files onto the notch | They're kept on the shelf; drag them back out any time |
| Volume keys | Halo's volume HUD instead of Windows' popup |
| A notification arrives | It drops out of the notch with app icon, title and text; hover to keep it, click to open the app, right-click to mute that app |
| Bell tab (expanded notch) | Recent notifications, newest first |

### Things you can type (tap Right Alt) or say (hold Right Alt)

| Say or type | What happens |
|---|---|
| `play kaahe mose` · `play tum hi ho on spotify` · `pause` · `next` | Plays in your YouTube Music app (or Spotify) · media controls |
| `lyrics` | Synced lyrics tab for the current song |
| `timer 25m tea` · `stopwatch` · `pomodoro` · `focus 45m` | Live countdown in the notch |
| `remind me at 6pm to call mom` · `note: buy milk` · `notes` | Reminder card when due · Notes tab |
| `5 ft in cm` · `100 usd to inr` · `5pm ist in pst` · `time in tokyo` | Instant answers (free exchange rates) |
| `translate good morning to hindi` | Opens Google Translate |
| `open spotify` · `open github.com` · `open downloads` | App / site / folder launcher |
| `snippet ;sig Best, Roushan` · `snippets` | Text expansion in any app (`;date`, `;time`, `;shrug` built in) |
| `weather` · `ticker cricket` · `ticker stocks RELIANCE.NS` · `ticker off` · `pet on` | Weather tab · rotating ticker · a tiny notch pet |
| `mute mic` · `cpu` · `clipboard` · `send to phone` | Mic toggle · System tab · Clipboard tab · QR transfer of the latest shelf file |
| `take a screenshot` | Whole screen saved to Pictures › Screenshots and copied |

Screenshots: besides the command, the frozen circle screen has **Screenshot** (everything) and **Snip**
(drag an area) buttons, and the results card has a button that captures the whole scrolling page.

Also automatic: a meeting pill with one-click mic mute while any app uses your microphone, a preview
when you copy, a screenshot catcher (drag it into any app or onto the shelf), circling a QR code opens it,
circling a price converts it, results get **Copy text** / **Translate** chips, scroll over the notch for
volume, swipe sideways to skip tracks, and the notch outline takes on the album colour.

Each feature is a self-contained module in `src/Halo.App/Modules/`; see its README to add more.

To see notifications only in the notch, turn on Windows **Do Not Disturb** (tray › "Turn off Windows
pop-ups…"). Windows doesn't let apps hide its pop-ups themselves, so Halo deliberately keeps showing
notifications while Do Not Disturb is on, and they still land in Action Center.

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
  Notifications/ other apps' toasts via the notification listener, history
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
- **Rare crash under investigation.** About once every 10–20 minutes of uptime Halo can exit with a native
  heap-corruption error (0xc0000374). It doesn't lose data; just start it again. A fix needs a crash dump.
- **Notifications** come from Windows' notification listener, read when Windows' notification store
  changes (desktop apps don't get its live event). Clicking opens the app, not the exact chat or email: Windows doesn't let other
  apps trigger a toast's own action or reply buttons. Older Windows builds may require MSIX packaging
  for notification access; Halo says so in the bell tab if that's the case.
- Right Alt is reserved for search, so AltGr characters on non-US layouts won't type while Halo runs.
- First launch shows the notch ~3 s after start (WPF's one-time theme load).
- The halo costs roughly 2–5% of one CPU core while idle (the transparent notch window must be re-copied
  on every frame). Turn it off from the tray (**Halo glow**) for near-zero idle CPU.

## Privacy and security

- Voice is transcribed offline with Whisper. The model download is pinned to a fixed revision and checked against its SHA-256.
- Searches go to Google, and only when you ask. When a circled image is uploaded to Lens, the card shows "Sent to Google".
- The results card can only open http/https pages. Pages can't launch apps or protocol handlers.
- Notification text is hidden while an app has your microphone or camera open, or is capturing the screen.
- Clipboard history, notes, snippets and other module data are encrypted with Windows DPAPI under your account. Unpinned clipboard items are forgotten after 24 hours.
- Clipboard secrets from QR codes (e.g. Wi-Fi passwords) are kept out of Windows clipboard history and cloud sync.
- Logs never contain what you said, searched or copied. They are kept for 3 days.
- Phone transfers:
  - work only on your local network, behind a random link token;
  - received executables are renamed to `.blocked`;
  - every received file is marked as downloaded from the internet.

## License

[MIT](LICENSE)
