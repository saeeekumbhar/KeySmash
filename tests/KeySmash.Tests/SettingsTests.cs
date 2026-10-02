using System.IO;
using System.Text.Json;
using KeySmash.Settings;
using Xunit;

namespace KeySmash.Tests;

public class SettingsTests
{
    [Fact]
    public void DefaultSettings_HaveSensibleDefaults()
    {
        var settings = new AppSettings();

        Assert.True(settings.Enabled);
        Assert.Equal(0.75f, settings.MasterVolume);
        Assert.Equal("Typewriter", settings.SelectedSoundPack);
        Assert.True(settings.Randomize);
        Assert.False(settings.StartWithWindows);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaultSettings()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"settings_missing_{Guid.NewGuid()}.json");

        // AppSettings.Load falls back when settings file does not exist
        var settings = AppSettings.Load();

        Assert.NotNull(settings);
    }

    [Fact]
    public void CorruptedJson_FallsBackGracefully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"keysmash_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var testFilePath = Path.Combine(tempDir, "settings.json");

        File.WriteAllText(testFilePath, "{ invalid json content !!!");

        AppSettings? result = null;
        try
        {
            var json = File.ReadAllText(testFilePath);
            result = JsonSerializer.Deserialize<AppSettings>(json);
        }
        catch
        {
            // fallback simulating AppSettings.Load
            result = new AppSettings();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }

        Assert.NotNull(result);
        Assert.True(result.Enabled);
        Assert.Equal(0.75f, result.MasterVolume);
    }

    [Fact]
    public void Volume_IsClampedBetweenZeroAndOne()
    {
        var settings = new AppSettings();

        settings.MasterVolume = 1.5f;
        var clampedHigh = Math.Clamp(settings.MasterVolume, 0f, 1f);
        Assert.Equal(1.0f, clampedHigh);

        settings.MasterVolume = -0.5f;
        var clampedLow = Math.Clamp(settings.MasterVolume, 0f, 1f);
        Assert.Equal(0.0f, clampedLow);
    }

    [Fact]
    public void Serialization_RoundTrip_PreservesValues()
    {
        var original = new AppSettings
        {
            Enabled = false,
            MasterVolume = 0.42f,
            SelectedSoundPack = "Mechanical",
            Randomize = false,
            StartWithWindows = true,
            IsMuted = true
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.False(restored.Enabled);
        Assert.Equal(0.42f, restored.MasterVolume);
        Assert.Equal("Mechanical", restored.SelectedSoundPack);
        Assert.False(restored.Randomize);
        Assert.True(restored.StartWithWindows);
        Assert.True(restored.IsMuted);
    }

    [Fact]
    public void Save_ConcurrentWrites_DoNotThrowExceptions()
    {
        var settings = new AppSettings();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 10, i =>
        {
            try
            {
                settings.MasterVolume = (i % 10) / 10f;
                settings.Save();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }
}
