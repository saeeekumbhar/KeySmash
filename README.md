# KeySmash

> Turn your keyboard into whatever the hell you want it to sound like.

KeySmash is a lightweight Windows desktop utility that plays custom sound effects whenever you press keys on your keyboard. It runs quietly in your system tray, uses almost zero CPU, and works with any keyboard connected to Windows.

---

## Features

* **Universal Keyboard Support**: Works automatically with laptop keyboards, USB keyboards, Bluetooth keyboards, mechanical switches, and multiple connected keyboards.
* **Ultra-Low Latency WASAPI Audio**: Event-driven WASAPI audio output delivers sub-15ms response times with automatic fallback to WaveOut and dynamic audio endpoint hotplug recovery.
* **Realistic Per-Key Acoustics**: Distinct, authentic audio samples for Spacebar (stabilizer thock), Enter (return clunk / bell), and Backspace (spring snap), alongside general typing keys.
* **Global Quick-Mute Hotkey**: Press <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>M</kbd> anywhere in Windows to instantly mute or unmute audio feedback.
* **Multi-Sample Sound Packs**: Supports sound packs with multiple samples and non-repeating random selection so typing feels organic and dynamic.
* **Built-in Sound Packs**: Includes original, copyright-free sound packs out of the box (`Typewriter`, `Mechanical`, `Bubble`, and `Shotgun`).
* **Sound Pack Management**: Import individual audio files (`.wav`, `.mp3`) or bundled sound packs (`.zip`), auto-detect loose sound files, and delete custom packs directly from the UI.
* **Volume & Mute Control**: Fluid, draggable master volume slider synchronized with mute states and system tray controls.
* **Single-Instance Bring-to-Front**: Duplicate launches seamlessly focus the existing window instead of creating duplicate background processes.
* **System Tray Operation**: Minimizes to the notification tray to stay out of your way while typing.
* **Optional Windows Startup**: Start automatically when Windows boots with live registry synchronization.
* **Strict Privacy**: Discards keyboard events immediately after triggering audio. Zero keylogging, zero telemetry, 100% offline.

---

## Supported Windows Versions

* Windows 10 (64-bit)
* Windows 11 (64-bit)

---

## Installation

### Pre-built Release
1. Download the latest self-contained executable from [Releases](../../releases).
2. Extract the archive and run `KeySmash.exe`.
3. No .NET runtime installation required.

---

## Running From Source

### Prerequisites
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or later) on Windows.

### Build & Run
```powershell
# clone the repository
git clone https://github.com/saeeekumbhar/KeySmash.git
cd KeySmash

# restore dependencies
dotnet restore

# build the solution
dotnet build

# run KeySmash
dotnet run --project src/KeySmash/KeySmash.csproj
```

### Run Tests
```powershell
dotnet test
```

---

## Using Sound Packs

KeySmash organizes sounds into folders containing audio samples (`.wav` or `.mp3`):

```text
sounds/
├── Typewriter/
│   ├── typewriter_01.wav
│   ├── typewriter_02.wav
│   └── typewriter_03.wav
├── Mechanical/
│   ├── mechanical_01.wav
│   ├── mechanical_02.wav
│   └── mechanical_03.wav
├── Bubble/
│   └── bubble_01.wav
└── Shotgun/
    └── shotgun_01.wav
```

### Selecting a Sound Pack
Use the dropdown in KeySmash to pick your active sound pack. If **randomize sounds** is checked, KeySmash chooses randomly between the samples in that pack without playing the same sample twice in a row.

### Importing Custom Sounds
1. Click **`+ add sound`** in KeySmash.
2. Select any `.wav` or `.mp3` file from your computer.
3. KeySmash validates the file, creates a new sound pack in `%LocalAppData%\KeySmash\sounds\`, and adds it to your dropdown.
4. Click **`open folder`** to view or manage your custom sound packs directly in Windows Explorer.

---

## Configuration

Settings are saved locally as standard JSON in `%LocalAppData%\KeySmash\settings.json`:

```json
{
  "Enabled": true,
  "MasterVolume": 0.75,
  "SelectedSoundPack": "Typewriter",
  "Randomize": true,
  "StartWithWindows": false
}
```

If the settings file is missing or corrupted, KeySmash automatically resets to safe default values.

---

## Privacy & Security

KeySmash uses the Windows low-level keyboard hook (`WH_KEYBOARD_LL`) strictly to detect keystroke events.

* **No typed text is recorded or stored.**
* **No key identities are logged.**
* **No network connections are made.**
* **No analytics or telemetry exists in the codebase.**
* **Input is passed directly through to Windows without interception or delay.**

See [SECURITY.md](SECURITY.md) for full details.

---

## Troubleshooting

* **No sound playing?**
  * Check that KeySmash is enabled in the UI (green "Enabled" status).
  * Check the volume slider and make sure you are not muted.
  * Verify your Windows default audio playback device is active.
* **Sound cutting off or distorted?**
  * KeySmash includes a built-in rate limiter (20ms threshold) to prevent buffer overflows when holding keys down while supporting ultra-fast typing bursts.
* **Application closes instead of minimizing?**
  * Closing or minimizing the window sends KeySmash to the system tray. Use the tray icon's context menu to reopen or fully exit the app.

---

## Building a Release

To create a standalone, self-contained single-file release that runs on any 64-bit Windows PC without requiring the .NET SDK:

```text
clone repository
      ↓
restore dependencies
      ↓
build
      ↓
publish
```

### Publish Command
```powershell
dotnet publish src/KeySmash/KeySmash.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The output will be placed in the `publish/` directory:
* `publish/KeySmash.exe` — standalone executable.
* `publish/sounds/` — built-in sound packs.

---

## License

This project is licensed under the [MIT License](LICENSE).
All built-in sound assets are original audio synthesized specifically for KeySmash under the same MIT license.
