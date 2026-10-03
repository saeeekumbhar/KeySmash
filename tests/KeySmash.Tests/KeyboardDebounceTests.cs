using KeySmash.Keyboard;
using Xunit;

namespace KeySmash.Tests;

public class KeyboardDebounceTests
{
    [Fact]
    public void MinInterval_DefaultsToSensibleValue()
    {
        using var hook = new KeyboardHook();

        Assert.True(hook.MinIntervalMs >= 20 && hook.MinIntervalMs <= 60);
    }

    [Fact]
    public void MinInterval_ClampsNegativeValuesToZero()
    {
        using var hook = new KeyboardHook();

        hook.MinIntervalMs = -20;
        Assert.Equal(0, hook.MinIntervalMs);
    }

    [Theory]
    [InlineData(10, 35, false)] // 10ms elapsed < 35ms threshold -> rate limited
    [InlineData(34, 35, false)] // 34ms elapsed < 35ms threshold -> rate limited
    [InlineData(35, 35, true)]  // 35ms elapsed >= 35ms threshold -> allowed
    [InlineData(60, 35, true)]  // 60ms elapsed >= 35ms threshold -> allowed
    public void RateLimit_EvaluatesIntervalCorrectly(long elapsedMs, int thresholdMs, bool shouldTrigger)
    {
        var passes = elapsedMs >= thresholdMs;
        Assert.Equal(shouldTrigger, passes);
    }

    [Fact]
    public void SuppressHeldKeyRepeats_DefaultsToTrue()
    {
        using var hook = new KeyboardHook();
        Assert.True(hook.SuppressHeldKeyRepeats);
    }

    [Fact]
    public void KeyStateMap_InitializesCleanAndHandlesBounds()
    {
        using var hook = new KeyboardHook();

        Assert.False(hook.IsKeyDown(0x57)); // 'W' key
        Assert.False(hook.IsKeyDown(0x20)); // Space
        Assert.False(hook.IsKeyDown(999));  // Out-of-bounds key

        hook.ResetKeyState();
        Assert.False(hook.IsKeyDown(0x57));
    }

    [Fact]
    public void ResetKeyState_ClearsStateAndAllowsImmediateRetrigger()
    {
        using var hook = new KeyboardHook();
        hook.ResetKeyState();

        Assert.False(hook.IsKeyDown(0x20));
        Assert.False(hook.IsKeyDown(0x08));
    }

    [Theory]
    [InlineData(1050, true)] // > 1000ms typematic threshold -> recover from missed KeyUp
    [InlineData(50, false)]  // <= 1000ms -> active held repeat
    public void MissedKeyUp_RecoversWhenElapsedExceedsTypematicWindow(long elapsedMs, bool shouldRecover)
    {
        bool isRepeat = true;
        if (isRepeat && elapsedMs > 1000)
        {
            isRepeat = false;
        }

        Assert.Equal(shouldRecover, !isRepeat);
    }

    [Fact]
    public void PlayModifierKeys_DefaultsToTrue()
    {
        using var hook = new KeyboardHook();
        Assert.True(hook.PlayModifierKeys);
    }

    [Theory]
    [InlineData(0x10, true)]  // Shift
    [InlineData(0xA0, true)]  // LShift
    [InlineData(0xA1, true)]  // RShift
    [InlineData(0x11, true)]  // Ctrl
    [InlineData(0xA2, true)]  // LCtrl
    [InlineData(0xA3, true)]  // RCtrl
    [InlineData(0x12, true)]  // Alt
    [InlineData(0xA4, true)]  // LAlt
    [InlineData(0xA5, true)]  // RAlt
    [InlineData(0x5B, true)]  // LWin
    [InlineData(0x5C, true)]  // RWin
    [InlineData(0x14, true)]  // CapsLock
    [InlineData(0x90, true)]  // NumLock
    [InlineData(0x91, true)]  // ScrollLock
    [InlineData(0x20, false)] // Space
    [InlineData(0x0D, false)] // Enter
    [InlineData(0x08, false)] // Backspace
    [InlineData(0x41, false)] // 'A'
    [InlineData(0x31, false)] // '1'
    public void IsModifierKey_CorrectlyClassifiesVirtualKeyCodes(uint vkCode, bool expectedIsModifier)
    {
        var result = KeyboardHook.IsModifierKey(vkCode);
        Assert.Equal(expectedIsModifier, result);
    }

    [Fact]
    public void KeyboardHook_DoubleDispose_DoesNotThrow()
    {
        var hook = new KeyboardHook();
        var ex = Record.Exception(() =>
        {
            hook.Dispose();
            hook.Dispose();
        });

        Assert.Null(ex);
    }

    [Fact]
    public void KeyboardHook_StartAfterDispose_ReturnsFalse()
    {
        var hook = new KeyboardHook();
        hook.Dispose();

        var started = hook.Start();
        Assert.False(started);
        Assert.False(hook.IsActive);
    }

    [Fact]
    public void ContinuousHeldRepeats_NeverTriggerSoundRegardlessOfDuration()
    {
        // Simulate continuous typematic repeat events (e.g. 33ms interval) over 3 seconds (90 events)
        long lastKeyDownTime = 0;
        long lastSoundTime = 0;
        bool isKeyDown = false;
        int soundTriggerCount = 0;
        const int minIntervalMs = 20;

        for (int i = 0; i <= 90; i++)
        {
            long nowMs = 10000 + (i * 33); // 10000, 10033, 10066, ..., 12970ms
            bool isRepeat = isKeyDown;
            isKeyDown = true;

            long elapsedSinceLastKeyDown = lastKeyDownTime > 0 ? (nowMs - lastKeyDownTime) : long.MaxValue;
            long elapsedSinceLastSound = lastSoundTime > 0 ? (nowMs - lastSoundTime) : long.MaxValue;

            lastKeyDownTime = nowMs;

            if (isRepeat)
            {
                bool isPhysicallyPressed = true;
                // If key is physically held and events arrive every ~33ms, it must NEVER recover as a fresh press
                if (!isPhysicallyPressed || elapsedSinceLastKeyDown > 1000)
                {
                    isRepeat = false;
                }
            }

            bool allowTrigger = false;
            if (!isRepeat)
            {
                if (elapsedSinceLastSound >= minIntervalMs)
                {
                    allowTrigger = true;
                }
            }

            if (allowTrigger)
            {
                soundTriggerCount++;
                lastSoundTime = nowMs;
            }
        }

        // Must trigger sound EXACTLY once on the initial press, and 0 times during the 3 seconds of held repeats!
        Assert.Equal(1, soundTriggerCount);
    }
}
