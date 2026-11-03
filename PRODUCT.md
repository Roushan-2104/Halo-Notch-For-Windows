# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

(The product itself is a native Windows desktop app, C#/.NET 8 WPF. Design exploration happens in web mockups; the
shipped UI is WPF, so no native iOS/Android guidance applies.)

## Users

- Primary: Roushan, the maker. Uses a Windows laptop (RTX 2050), laptop mic and sometimes earbuds (Airdopes 311 Pro),
  speaks Indian English and Hinglish, lives in Chrome, VS Code, Google Meet and YouTube Music, hides the taskbar.
- Public audience after that: students and developers on Windows laptops who search, code and study all day; and
  Mac switchers / design lovers who miss the Mac's Dynamic Island and notch apps on Windows.

## Product Purpose

Shammy turns the top-centre of the Windows screen into a living notch: a command centre for search, voice commands,
music, timers, reminders, calendar, notifications, files and system status, with a pet that gives it personality.
Success: the user reaches for the notch instead of the taskbar, Start menu or a browser tab for everyday jobs, and
enjoys it while doing so.

## Positioning

- A Dynamic Island / Mac notch experience built natively for Windows, with a live pet ("Shammy") as its face.
- Free and offline-first: local Whisper voice that understands Indian English and Hinglish, no paid AI, no cloud keys.
- Circle to Search on Windows (Copilot key) and a keyboard-and-voice palette (Right Alt) in the same notch.

## Operating Context

- Always on screen, top-centre, over whatever the user is doing: coding, browsing, studying, calls, music, fullscreen video.
- Three keys: hold Right Alt to talk, tap Right Alt to type (live palette), Copilot key or hold Right Ctrl to circle.
  Everything else is hover (smart hover → peek → open).
- Auto-updates through Velopack from GitHub releases (repo Roushan-2104/Shammy-Notch-For-Windows, MIT).

## Capabilities and Constraints

- Capabilities: live palette (apps, files via Windows Search, settings controls, commands, music, web); offline voice
  (Whisper turbo on GPU, optional Hinglish model); circle to search / Google Lens / OCR; music with lyrics; timers,
  pomodoro, focus, reminders, alarms, notes; clipboard history; file shelf and drop actions; phone send by QR;
  Google Calendar (private iCal) with Join; webcam Mirror; weather; battery, earbuds, CPU; notifications with
  screen-share privacy; snippets; screenshots; app launcher; the notch pet with moods.
- Constraints: near-zero CPU at idle, animations only while visible; never steal focus; hide in fullscreen (alarms and
  finished timers break through); nothing leaves the PC without the user asking (searches, Lens uploads, weather city);
  camera only while Mirror is open.
- Undecided: final idle/music behaviour (pet vs music pill), action circle beside the notch (proposed), pet extras
  (customisation, levels) for the app.

## Brand Commitments

- Name: Shammy (renamed from Halo). Internal update id stays "HaloNotch".
- Voice: playful buddy — friendly, cute, a little cheeky, short ("Tea's ready!", "Got it — on it"). Personality also
  lives in the pet's animations.
- The pet ("Shammy", user-nameable) is the face of the product.

## Evidence on Hand

- README.md with screenshots in docs/images/; the redesign mockup canvas (private claude.ai artifact) with approved v2
  boards. No testimonials, user counts, press or benchmarks exist — never fabricate them.

## Product Principles

1. Never in the way: tiny at rest, quiet in fullscreen, no focus stealing.
2. Free and offline-first: local voice, no paid AI, privacy by default.
3. Keyboard and voice first; the mouse is optional.
4. Light on the battery: idle means idle.
5. Alive, not busy: personality comes from small, purposeful motion and the pet, never from clutter.
