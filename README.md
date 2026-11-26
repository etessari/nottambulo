# Nottambulo

<img src="docs/icon.png" alt="Tray icon: awake (left) and off (right)" height="96">

*Nottambulo* (Italian for "night owl") is a tiny Windows tray app that keeps your PC awake — even with the
screen locked and the lid closed — so builds, downloads and AI coding agents can keep working while you're away.

## Download

**[⬇ Download Nottambulo.exe](https://github.com/etessari/nottambulo/releases/latest/download/Nottambulo.exe)** (Windows 10/11, ~40 KB, no installer)

Just run it: the face appears in the system tray. The exe isn't code-signed, so Windows SmartScreen may warn you the first time —
click *More info → Run anyway*, or [build it yourself](#build) from source.

- **Left-click** the tray face to toggle it on/off (amber face with tired, bloodshot eyes = keeping the PC awake; grey sleeping face = off).
- **Right-click** for the menu:
  - keep awake until turned off, or for 30 min / 1 / 2 / 4 hours;
  - **Keep screen on** — required on laptops with Modern Standby (S0), which otherwise go to sleep as soon as the display turns off;
  - **Stay awake with the lid closed** (plus **…also on battery**);
  - **Simulate activity** — presses the invisible F15 key every 50 s (only works while the session is unlocked);
  - Notifications, Start with Windows, Exit.

## How it works

- Sleep is prevented with the official `SetThreadExecutionState` API, so it works on the lock screen too.
- The lid close action is read from your current power plan (plugged in and on battery), saved to
  `%APPDATA%\Nottambulo\lid-backup.txt`, set to *Do nothing*, and restored exactly as it was when you turn the app off.
  If the app crashes or the PC loses power, the original setting is restored on the next start.
- No admin rights needed. No dependencies: a single small `.exe`.

With the lid closed the built-in display still turns off (Windows does that regardless); the PC keeps running and external monitors stay on.

## Build

No SDK required — it uses the C# compiler that ships with Windows:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

This produces `Nottambulo.exe`, which you can put anywhere.

## Warning

With *Stay awake with the lid closed* enabled on battery, the laptop will not sleep in your bag: it can overheat and drain the battery.
Turn it off before packing up, or untick *…also on battery*.

## License

[MIT](LICENSE)
