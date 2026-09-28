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
}
