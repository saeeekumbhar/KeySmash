# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
