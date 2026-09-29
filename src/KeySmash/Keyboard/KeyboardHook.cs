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
    private long _lastPressTimestamp;
    private int _minIntervalMs = 20;

    // Asynchronous lock-free queue: isolates Windows hook thread from audio processing
    // and protects against Windows LowLevelHooksTimeout watchdog silently unhooking.
    private readonly Channel<KeyCategory> _keyChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumerTask;

    // Full 256-key state map to accurately track multiple held keys simultaneously (e.g. gaming WASD + Shift/Space)
    private readonly bool[] _isKeyDown = new bool[256];
    public bool SuppressHeldKeyRepeats { get; set; } = true;

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
    }

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

    public void Start()
    {
        if (_hookId != nint.Zero)
            return;

        ResetKeyState();

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var moduleHandle = curModule?.BaseAddress ?? nint.Zero;

        _hookId = SetWindowsHookEx(WhKeyboardLl, _proc, moduleHandle, 0);
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
        if (nCode >= 0)
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

                bool allowTrigger = false;
                long now = Stopwatch.GetTimestamp();

                if (!isRepeat)
                {
                    var elapsedMs = (now - _lastPressTimestamp) * 1000 / Stopwatch.Frequency;
                    if (elapsedMs >= _minIntervalMs)
                    {
                        allowTrigger = true;
                    }
                }
                else if (!SuppressHeldKeyRepeats)
                {
                    var elapsedMs = (now - _lastPressTimestamp) * 1000 / Stopwatch.Frequency;
                    if (elapsedMs >= _minIntervalMs)
                    {
                        allowTrigger = true;
                    }
                }
                else if (vkCode == 0x08 || vkCode == 0x2E) // Backspace or Delete
                {
                    // Allow smooth, pleasant pacing when deleting text (90ms)
                    var elapsedMs = (now - _lastPressTimestamp) * 1000 / Stopwatch.Frequency;
                    if (elapsedMs >= 90)
                    {
                        allowTrigger = true;
                    }
                }
                // All other keys (WASD, letters, numbers, space) are completely silenced while held

                if (allowTrigger)
                {
                    _lastPressTimestamp = now;

                    var category = vkCode switch
                    {
                        0x20 => KeyCategory.Space,
                        0x0D => KeyCategory.Enter,
                        0x08 => KeyCategory.Backspace,
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

        // Return immediately without delay to keep Windows hook thread responsive
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        _cts.Cancel();
        _keyChannel.Writer.TryComplete();
        _cts.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);
}
