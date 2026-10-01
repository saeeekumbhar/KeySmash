# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- Fixed background zombie process on application exit caused by `ShutdownMode.OnExplicitShutdown` without explicit shutdown call, freeing single-instance mutex reliably.
- Fixed catastrophic data loss vulnerability in custom sound pack deletion where deleting the loose "Custom Sounds" pack deleted the root `%LocalAppData%\KeySmash\sounds` directory and all packs inside it.
- Fixed permanent key muting caused by missed `WM_KEYUP` events during lock screen (`Win + L`), `Alt + Tab`, or UAC prompt transitions via hardware `GetAsyncKeyState` queries and typematic repeat window verification.
- Fixed audio playback crash loops on multi-channel audio files (>2 channels) by automatically downmixing surround sources to stereo.
- Added hyperbolic tangent soft-limiter saturation stage between mixer and volume provider to eliminate digital clipping and pops during rapid typing bursts.
- Added audio sample length clamping (5 seconds max) and import duration validation (15 seconds max) to prevent memory exhaustion and UI freezes.
- Fixed security vulnerability in ZIP sound pack importer by strictly filtering safe audio/metadata files and rejecting executable scripts (`.exe`, `.bat`, `.cmd`, `.dll`, etc.).
- Added Zip bomb protection: enforced limits on max uncompressed payload size (250 MB) and max entry count (500).
- Fixed sound pack name collisions with built-in packs (`Typewriter`, `Mechanical`, etc.) by safely qualifying custom imports with `(Custom)`.
- Fixed audio device recovery UI synchronization by propagating live state changes to the MainWindow footer.
- Fixed concurrency race conditions in settings serialization via synchronized locks and unique atomic temp files.
- Fixed infinite hotkey registration retry loop when shortcut is occupied, capping retries and adding cancellation on exit.

## [1.1.0] - 2026-09-29

### Added
- Ultra-low latency event-driven WASAPI audio output engine (<15ms) with automatic fallback to WaveOut.
- Key categorization system classifying Spacebar, Enter, and Backspace for distinct tactile sound effects without keystroke logging.
- Synthesized dedicated audio samples (Space, Enter, Backspace) for all built-in sound packs (`Typewriter`, `Mechanical`, `Bubble`, `Shotgun`).
- Single-instance IPC activation: launching a duplicate instance signals the running instance and brings its window to the foreground.
- Automatic audio device hotplug recovery when headphones/audio endpoints are connected or disconnected.
- ZIP sound pack and multi-sample archive importer.
- In-app custom sound pack deletion with confirmation dialog.
- Live sound packs reload and refresh button.
- Loose audio file discovery and automatic pack grouping in `%LocalAppData%\KeySmash\sounds`.
- Global quick-mute hotkey: `Ctrl + Shift + M` with `MOD_NOREPEAT` support.
- Hotkey conflict resolution with automatic fallback to `Ctrl + Alt + M` and live UI status display.
- GitHub Actions CI/CD automation workflow (`.github/workflows/ci.yml`).
- Application manifest declaring standard user permissions (`asInvoker`) and PerMonitorV2 DPI awareness (zero admin rights required).
- Asynchronous lock-free Channel queue in `KeyboardHook` isolating Windows message callbacks from audio processing, preventing Windows hook timeout watchdog kills.
- Held-key auto-repeat suppression and intelligent pacing for gaming (<kbd>W</kbd>/<kbd>A</kbd>/<kbd>S</kbd>/<kbd>D</kbd>) and text editing.
- Configurable "minimize to tray on close" with a helpful first-time notification balloon.
- Explicit "Exit KeySmash" button in the application footer for 1-click shutdown.

### Fixed
- Fixed broken hotkey on Windows startup by guaranteeing window HWND allocation via `EnsureHandle()`, configuring `ShutdownMode.OnExplicitShutdown`, and adding startup retry handling.
- Fixed window minimization behavior: clicking minimize now minimizes normally to the Windows Taskbar instead of completely vanishing from sight.
- Fixed volume slider sliding behavior with custom `ModernSlider` control template and `IsMoveToPointEnabled`.
- Fixed volume slider and mute desynchronization by auto-unmuting on volume increase and auto-muting at 0%.
- Added Mute toggle and muted state indicator to the system tray context menu.
- Lowered keyboard debounce interval from 35ms to 20ms to prevent dropping fast typing rolls.
- Synchronized "Start KeySmash with Windows" checkbox directly with Windows Registry.

## [1.0.0] - 2026-09-28

### Added
- Low-level Windows keyboard hook (`WH_KEYBOARD_LL`) with zero-latency audio dispatch.
- Audio engine powered by NAudio with cached in-memory sample playback and mixing.
- Built-in original sound packs: `Typewriter`, `Mechanical`, `Bubble`, and `Shotgun`.
- Non-repeating random sample selection and sequential playback options.
- Master volume control slider with real-time level adjustment and mute toggle.
- Sound preview button to test audio packs instantly.
- Custom sound import feature supporting `.wav` and `.mp3` files into `%LocalAppData%\KeySmash\sounds`.
- System tray minimization and context menu (enable/disable toggle, sound pack status, volume status, open, exit).
- Local JSON settings persistence (`settings.json`) with corrupted settings fallback.
- Optional "Start KeySmash with Windows" toggle via explicit user registry configuration.
- Comprehensive unit test suite covering settings, sound pack discovery, randomization, volume, and debounce logic.
- Self-contained single-file release build script.
