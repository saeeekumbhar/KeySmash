using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace KeySmash.Keyboard;

public sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmKeyup = 0x0101;
    private const int WmSyskeydown = 0x0104;
    private const int WmSyskeyup = 0x0105;

    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    private readonly HookProc _proc;
    private nint _hookId = nint.Zero;
    private int _minIntervalMs = 20;

    // Asynchronous lock-free queue: isolates Windows hook thread from audio processing
    // and protects against Windows LowLevelHooksTimeout watchdog silently unhooking.
    private readonly Channel<KeyCategory> _keyChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerTask;

    // Full 256-key state map to accurately track multiple held keys simultaneously (e.g. gaming WASD + Shift/Space)
    private readonly bool[] _isKeyDown = new bool[256];
    private readonly long[] _lastKeyDownTimestamp = new long[256];
    private readonly long[] _lastSoundTriggerTimestamp = new long[256];
    private bool _isDisposed;
    public bool SuppressHeldKeyRepeats { get; set; } = true;
    public bool PlayModifierKeys { get; set; } = true;

    public event Action<KeyCategory>? KeyPressed;

    public bool IsActive => _hookId != nint.Zero;

    public int MinIntervalMs
    {
        get => _minIntervalMs;
        set => _minIntervalMs = Math.Max(0, value);
    }

    public KeyboardHook()
    {
        _proc = HookCallback;

        // Bounded channel with DropOldest prevents any memory buildup during extreme typing bursts
        _keyChannel = Channel.CreateBounded<KeyCategory>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        _consumerTask = Task.Run(ProcessQueueAsync);
    }

    public bool IsKeyDown(uint vkCode) => vkCode < 256 && _isKeyDown[vkCode];

    public void ResetKeyState()
    {
        Array.Clear(_isKeyDown, 0, _isKeyDown.Length);
        Array.Clear(_lastKeyDownTimestamp, 0, _lastKeyDownTimestamp.Length);
        Array.Clear(_lastSoundTriggerTimestamp, 0, _lastSoundTriggerTimestamp.Length);
    }

    public static bool IsModifierKey(uint vkCode) => vkCode switch
    {
        0x10 or 0xA0 or 0xA1 => true, // Shift / LShift / RShift
        0x11 or 0xA2 or 0xA3 => true, // Ctrl / LCtrl / RCtrl
        0x12 or 0xA4 or 0xA5 => true, // Alt / LAlt / RAlt
        0x5B or 0x5C => true,         // Win / LWin / RWin
        0x14 => true,                 // CapsLock
        0x90 => true,                 // NumLock
        0x91 => true,                 // ScrollLock
        _ => false
    };

    private async Task ProcessQueueAsync()
    {
        var reader = _keyChannel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var category))
                {
                    try
                    {
                        KeyPressed?.Invoke(category);
                    }
                    catch
                    {
                        // ensure subscriber exceptions never terminate the dispatch loop
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal cancellation on shutdown
        }
    }

    public bool Start()
    {
        if (_isDisposed)
            return false;

        if (_hookId != nint.Zero)
            return true;

        ResetKeyState();

        nint moduleHandle = nint.Zero;
        try
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            moduleHandle = curModule?.BaseAddress ?? nint.Zero;
        }
        catch
        {
            moduleHandle = nint.Zero;
        }

        _hookId = SetWindowsHookEx(WhKeyboardLl, _proc, moduleHandle, 0);
        return _hookId != nint.Zero;
    }

    public void Stop()
    {
        if (_hookId == nint.Zero)
            return;

        UnhookWindowsHookEx(_hookId);
        _hookId = nint.Zero;
        ResetKeyState();
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && lParam != nint.Zero)
        {
            try
            {
                var msg = (int)wParam;
                if (msg == WmKeydown || msg == WmSyskeydown)
                {
                    var vkCode = (uint)Marshal.ReadInt32(lParam);
                    bool isRepeat = false;
                    if (vkCode < 256)
                    {
                        isRepeat = _isKeyDown[vkCode];
                        _isKeyDown[vkCode] = true;
                    }

                    long now = Stopwatch.GetTimestamp();
                    var lastKeyDownTime = vkCode < 256 ? _lastKeyDownTimestamp[vkCode] : 0;
                    var lastSoundTime = vkCode < 256 ? _lastSoundTriggerTimestamp[vkCode] : 0;

                    var elapsedSinceLastKeyDownMs = lastKeyDownTime > 0 ? (now - lastKeyDownTime) * 1000 / Stopwatch.Frequency : long.MaxValue;
                    var elapsedSinceLastSoundMs = lastSoundTime > 0 ? (now - lastSoundTime) * 1000 / Stopwatch.Frequency : long.MaxValue;

                    // Always record the exact timestamp of each WM_KEYDOWN received to accurately detect gaps in typematic repeats
                    if (vkCode < 256)
                    {
                        _lastKeyDownTimestamp[vkCode] = now;
                    }

                    // Recover from missed WM_KEYUP (e.g. Win+L lock screen, Alt+Tab, UAC prompt, or focus transitions)
                    if (isRepeat)
                    {
                        bool isPhysicallyPressed = (GetAsyncKeyState((int)vkCode) & 0x8000) != 0;
                        // If the hardware indicates key is physically released, OR there was a >1000ms gap in the key-down stream,
                        // recover from the missed WM_KEYUP and treat as a fresh press.
                        if (!isPhysicallyPressed || elapsedSinceLastKeyDownMs > 1000)
                        {
                            isRepeat = false;
                        }
                    }

                    bool allowTrigger = false;

                    if (!isRepeat)
                    {
                        // Per-key debounce: suppress switch bounce/chatter on the same key without dropping fast finger rolls
                        if (elapsedSinceLastSoundMs >= _minIntervalMs)
                        {
                            allowTrigger = true;
                        }
                    }
                    else if (!SuppressHeldKeyRepeats)
                    {
                        if (elapsedSinceLastSoundMs >= _minIntervalMs)
                        {
                            allowTrigger = true;
                        }
                    }
                    else if (vkCode == 0x08 || vkCode == 0x2E) // Backspace or Delete
                    {
                        // Allow smooth, pleasant pacing when deleting text (90ms)
                        if (elapsedSinceLastSoundMs >= 90)
                        {
                            allowTrigger = true;
                        }
                    }
                    // All other keys (WASD, letters, numbers, space) are completely silenced while held

                    if (allowTrigger && !PlayModifierKeys && IsModifierKey(vkCode))
                    {
                        allowTrigger = false;
                    }

                    if (allowTrigger)
                    {
                        if (vkCode < 256)
                        {
                            _lastSoundTriggerTimestamp[vkCode] = now;
                        }

                        var category = vkCode switch
                        {
                            0x20 => KeyCategory.Space,
                            0x0D => KeyCategory.Enter,
                            0x08 => KeyCategory.Backspace,
                            0x2E => KeyCategory.Backspace,
                            _ => KeyCategory.General
                        };

                        // Push to lock-free channel in nanoseconds and return immediately!
                        _keyChannel.Writer.TryWrite(category);
                    }
                }
                else if (msg == WmKeyup || msg == WmSyskeyup)
                {
                    var vkCode = (uint)Marshal.ReadInt32(lParam);
                    if (vkCode < 256)
                    {
                        _isKeyDown[vkCode] = false;
                    }
                }
            }
            catch
            {
                // Never let any exception escape into the unmanaged Windows hook chain
            }
        }

        // Return immediately without delay to keep Windows hook thread responsive
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        Stop();
        try
        {
            _cts.Cancel();
            _keyChannel.Writer.TryComplete();
            _consumerTask.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // ignore cancellation / timeout exceptions
        }
        finally
        {
            _cts.Dispose();
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
