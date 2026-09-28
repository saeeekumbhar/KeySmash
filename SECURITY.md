# Security Policy

## Security & Privacy Model

KeySmash is designed with a strict privacy-first architecture. It functions solely as an offline audio trigger utility for keystrokes.

### Keyboard Hook Guarantees

* **No Key Logging**: KeySmash installs a standard Windows low-level keyboard hook (`WH_KEYBOARD_LL`) strictly to detect key-down events.
* **Immediate Discard**: The key identifier and event data are never stored in memory or written to disk. The application triggers the selected sound and immediately yields control back to Windows (`CallNextHookEx`).
* **No Text Reconstruction**: No typed words, sentences, or typing patterns are tracked or reconstructed.
* **No Sensitive Field Capture**: Passwords, form fields, and secure input remain untouched.

### Offline Operation & Zero Telemetry

* **100% Offline**: KeySmash contains no network code, no web clients, no analytics, and no telemetry services.
* **No Remote Communication**: The application never connects to remote servers or third-party endpoints.
* **Local Storage Only**: User settings and imported sound packs are saved exclusively to `%LocalAppData%\KeySmash` on your local drive.

### Device Permissions

KeySmash requires no special privileges beyond standard user permissions:
* No administrator elevation required.
* No microphone or camera access.
* No clipboard inspection or monitoring.
* No screenshot or screen capture access.

## Reporting a Security Concern

If you identify an issue or unexpected behavior:
1. Open an issue on GitHub with reproduction details.
2. Ensure you download KeySmash binaries only from official GitHub Releases.
