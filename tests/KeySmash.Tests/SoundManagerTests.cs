using System.IO;
using KeySmash.Audio;
using Xunit;

namespace KeySmash.Tests;

public class SoundManagerTests
{
    [Fact]
    public void Volume_ClampsToZeroAndOne()
    {
        using var manager = new SoundManager();

        manager.MasterVolume = 1.25f;
        Assert.Equal(1.0f, manager.MasterVolume);

        manager.MasterVolume = -0.1f;
        Assert.Equal(0.0f, manager.MasterVolume);

        manager.MasterVolume = 0.5f;
        Assert.Equal(0.5f, manager.MasterVolume);
    }

    [Fact]
    public void Mute_TogglesMuteState()
    {
        using var manager = new SoundManager();

        manager.MasterVolume = 0.8f;
        Assert.False(manager.IsMuted);

        manager.IsMuted = true;
        Assert.True(manager.IsMuted);
        // volume property retains user setting
        Assert.Equal(0.8f, manager.MasterVolume);

        manager.IsMuted = false;
        Assert.False(manager.IsMuted);
        Assert.Equal(0.8f, manager.MasterVolume);
    }

    [Fact]
    public void LoadPacksFromDirectory_HandlesNonExistentDirectoryGracefully()
    {
        using var manager = new SoundManager();

        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"missing_dir_{Guid.NewGuid()}");

        // should not throw
        manager.LoadPacksFromDirectory(nonExistentPath, isBuiltIn: false);
    }

    [Fact]
    public void LoadPackFromFolder_IgnoresCorruptFilesGracefully()
    {
        using var manager = new SoundManager();

        var tempDir = Path.Combine(Path.GetTempPath(), $"pack_corrupt_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // write a fake unreadable wav file
            File.WriteAllText(Path.Combine(tempDir, "broken.wav"), "This is not a real audio file.");

            var pack = manager.LoadPackFromFolder(tempDir, "CorruptPack", isBuiltIn: false);

            Assert.NotNull(pack);
            Assert.Equal("CorruptPack", pack.Name);
            // broken file skipped gracefully
            Assert.Empty(pack.Samples);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadBuiltInPacks_FindsGeneratedSounds()
    {
        using var manager = new SoundManager();

        var current = AppDomain.CurrentDomain.BaseDirectory;
        var dirInfo = new DirectoryInfo(current);
        while (dirInfo != null && !File.Exists(Path.Combine(dirInfo.FullName, "KeySmash.sln")))
        {
            dirInfo = dirInfo.Parent;
        }

        var root = dirInfo?.FullName ?? Path.GetFullPath(@"..\..\..\..\..");
        var assetsDir = Path.Combine(root, "assets", "sounds");

        manager.LoadPacksFromDirectory(assetsDir, isBuiltIn: true);

        Assert.NotEmpty(manager.SoundPacks);
        Assert.Contains(manager.SoundPacks, p => p.Name == "Typewriter");
        Assert.Contains(manager.SoundPacks, p => p.Name == "Mechanical");

        var typewriter = manager.SoundPacks.First(p => p.Name == "Typewriter");
        Assert.NotEmpty(typewriter.SpaceSamples);
        Assert.NotEmpty(typewriter.EnterSamples);
        Assert.NotEmpty(typewriter.BackspaceSamples);
    }

    [Fact]
    public void DeleteCustomPack_DeletesFolderAndRemovesFromList()
    {
        using var manager = new SoundManager();

        var tempDir = Path.Combine(Path.GetTempPath(), $"pack_del_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "dummy.wav"), "not a real wav");

        var pack = new SoundPack("CustomToDelete", tempDir, isBuiltIn: false);
        manager.SoundPacks.Add(pack);
        manager.SelectedPack = pack;

        var deleted = manager.DeleteCustomPack(pack);

        Assert.True(deleted);
        Assert.False(Directory.Exists(tempDir));
        Assert.DoesNotContain(pack, manager.SoundPacks);
    }

    [Fact]
    public void DeleteCustomPack_PreventsDeletingBuiltInPacks()
    {
        using var manager = new SoundManager();
        var builtIn = new SoundPack("BuiltIn", "some/path", isBuiltIn: true);
        manager.SoundPacks.Add(builtIn);

        var deleted = manager.DeleteCustomPack(builtIn);

        Assert.False(deleted);
        Assert.Contains(builtIn, manager.SoundPacks);
    }

    [Fact]
    public void PlayKeySound_UnderHighConcurrency_DoesNotThrow()
    {
        using var manager = new SoundManager();
        var pack = new SoundPack("StressPack");
        pack.Samples.Add(new CachedSound(new float[4410])); // 0.1s sample
        manager.SoundPacks.Add(pack);
        manager.SelectedPack = pack;

        // rapidly trigger 100 key sounds across parallel threads
        Parallel.For(0, 100, _ =>
        {
            manager.PlayKeySound();
        });
    }
}
