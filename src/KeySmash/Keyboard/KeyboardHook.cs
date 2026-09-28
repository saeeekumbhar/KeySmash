using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeySmash.Keyboard;

public sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeydown = 0x0100;
    private const int WmSyskeydown = 0x0104;

    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    private readonly HookProc _proc;
    private nint _hookId = nint.Zero;
    private long _lastPressTimestamp;
    private int _minIntervalMs = 35;

    public event Action? KeyPressed;

    public bool IsActive => _hookId != nint.Zero;

    public int MinIntervalMs
    {
        get => _minIntervalMs;
        set => _minIntervalMs = Math.Max(0, value);
    }

    public KeyboardHook()
    {
        // keep delegate reference alive to prevent garbage collection
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookId != nint.Zero)
            return;

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
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == WmKeydown || wParam == WmSyskeydown))
        {
            var now = Stopwatch.GetTimestamp();
            var elapsedMs = (now - _lastPressTimestamp) * 1000 / Stopwatch.Frequency;

            // rate limit held key repeats to avoid audio distortion
            if (elapsedMs >= _minIntervalMs)
            {
                _lastPressTimestamp = now;

                // trigger audio and immediately discard event info - no key identity stored
                KeyPressed?.Invoke();
            }
        }

        // never block or modify normal keyboard input
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);
}
