using System.IO;
using System.IO.Compression;
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

    [Fact]
    public void ImportCustomZip_FlattensNestedDirectory()
    {
        using var manager = new SoundManager();
        var tempDir = Path.Combine(Path.GetTempPath(), $"zip_test_{Guid.NewGuid()}");
        var userSoundsDir = Path.Combine(tempDir, "sounds");
        var zipPath = Path.Combine(tempDir, "NestedPack.zip");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(userSoundsDir);

        try
        {
            var stageDir = Path.Combine(tempDir, "staging", "NestedPack");
            Directory.CreateDirectory(stageDir);
            CreateDummyWav(Path.Combine(stageDir, "type_01.wav"));
            CreateDummyWav(Path.Combine(stageDir, "space_01.wav"));
            CreateDummyWav(Path.Combine(stageDir, "delete_01.wav"));
            ZipFile.CreateFromDirectory(Path.Combine(tempDir, "staging"), zipPath);

            var imported = manager.ImportCustomZip(zipPath, userSoundsDir);

            Assert.True(imported);
            Assert.NotNull(manager.SelectedPack);
            Assert.Equal("NestedPack", manager.SelectedPack.Name);
            Assert.NotEmpty(manager.SelectedPack.Samples);
            Assert.NotEmpty(manager.SelectedPack.SpaceSamples);
            Assert.NotEmpty(manager.SelectedPack.BackspaceSamples);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ImportCustomZip_CleansUpOnInvalidArchive()
    {
        using var manager = new SoundManager();
        var tempDir = Path.Combine(Path.GetTempPath(), $"zip_invalid_{Guid.NewGuid()}");
        var userSoundsDir = Path.Combine(tempDir, "sounds");
        var zipPath = Path.Combine(tempDir, "CorruptPack.zip");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(userSoundsDir);

        try
        {
            // Empty zip with no audio files
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("readme.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.WriteLine("No sounds here");
            }

            var imported = manager.ImportCustomZip(zipPath, userSoundsDir);

            Assert.False(imported);
            // Ensure no empty orphaned folder is left behind
            Assert.False(Directory.Exists(Path.Combine(userSoundsDir, "CorruptPack")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DeleteCustomPack_WhenTargetIsRootUserSoundsDirectory_DeletesLooseFilesWithoutDeletingRootFolder()
    {
        using var manager = new SoundManager();
        var tempDir = Path.Combine(Path.GetTempPath(), $"user_sounds_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var looseFile = Path.Combine(tempDir, "loose_01.wav");
            CreateDummyWav(looseFile);
            var subPackDir = Path.Combine(tempDir, "KeepThisPack");
            Directory.CreateDirectory(subPackDir);
            CreateDummyWav(Path.Combine(subPackDir, "sound_01.wav"));

            var loosePack = new SoundPack("Custom Sounds", tempDir, isBuiltIn: false);
            manager.SoundPacks.Add(loosePack);
            manager.SelectedPack = loosePack;

            // Delete loose sounds pack pointing to the root sounds directory
            var deleted = manager.DeleteCustomPack(loosePack);

            Assert.True(deleted);
            // Root directory MUST NOT be deleted
            Assert.True(Directory.Exists(tempDir));
            // Loose files inside root should be deleted
            Assert.False(File.Exists(looseFile));
            // Subdirectories/other custom packs must remain intact!
            Assert.True(Directory.Exists(subPackDir));
            Assert.True(File.Exists(Path.Combine(subPackDir, "sound_01.wav")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData("CON", "CustomPack")]
    [InlineData("PRN", "CustomPack")]
    [InlineData("AUX", "CustomPack")]
    [InlineData("NUL", "CustomPack")]
    [InlineData("COM1", "CustomPack")]
    [InlineData("LPT1", "CustomPack")]
    [InlineData("..", "CustomPack")]
    [InlineData("... ", "CustomPack")]
    [InlineData("MyPack.", "MyPack")]
    [InlineData("ValidPackName", "ValidPackName")]
    public void SanitizePackName_HandlesReservedNamesAndTraversal(string input, string expected)
    {
        var sanitized = SoundManager.SanitizePackName(input);
        Assert.Equal(expected, sanitized);
    }

    [Theory]
    [InlineData("Typewriter", "Typewriter (Custom)")]
    [InlineData("Mechanical", "Mechanical (Custom)")]
    [InlineData("Bubble", "Bubble (Custom)")]
    [InlineData("Shotgun", "Shotgun (Custom)")]
    [InlineData("CustomName", "CustomName")]
    public void ResolveSafePackName_ProtectsBuiltInPacks(string input, string expected)
    {
        var resolved = SoundManager.ResolveSafePackName(input);
        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ImportCustomZip_RejectsExecutableFilesAndExtractsAudioOnly()
    {
        using var manager = new SoundManager();
        var tempDir = Path.Combine(Path.GetTempPath(), $"zip_sec_{Guid.NewGuid()}");
        var userSoundsDir = Path.Combine(tempDir, "sounds");
        var zipPath = Path.Combine(tempDir, "MaliciousPack.zip");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(userSoundsDir);

        try
        {
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                // Add valid audio
                var wavEntry = archive.CreateEntry("click.wav");
                using (var ms = new MemoryStream())
                {
                    using var bw = new BinaryWriter(ms);
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                    bw.Write(36 + 8820);
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                    bw.Write(16);
                    bw.Write((short)1);
                    bw.Write((short)1);
                    bw.Write(44100);
                    bw.Write(44100 * 2);
                    bw.Write((short)2);
                    bw.Write((short)16);
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                    bw.Write(8820);
                    bw.Write(new byte[8820]);
                    using var entryStream = wavEntry.Open();
                    entryStream.Write(ms.ToArray());
                }

                // Add dangerous executable and script files
                var exeEntry = archive.CreateEntry("payload.exe");
                using (var writer = new StreamWriter(exeEntry.Open()))
                {
                    writer.WriteLine("MZ executable data");
                }

                var batEntry = archive.CreateEntry("malicious.bat");
                using (var writer = new StreamWriter(batEntry.Open()))
                {
                    writer.WriteLine("@echo off");
                }
            }

            var imported = manager.ImportCustomZip(zipPath, userSoundsDir);

            Assert.True(imported);
            var packFolder = Path.Combine(userSoundsDir, "MaliciousPack");
            Assert.True(Directory.Exists(packFolder));
            Assert.True(File.Exists(Path.Combine(packFolder, "click.wav")));
            // Dangerous files MUST NOT be extracted
            Assert.False(File.Exists(Path.Combine(packFolder, "payload.exe")));
            Assert.False(File.Exists(Path.Combine(packFolder, "malicious.bat")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SoftLimiter_SaturatesLoudAudioWithoutClippingArtifacts()
    {
        var dummyFormat = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        // Create sample provider returning values > 1.0 (clipping level)
        var source = new DummySampleProvider(dummyFormat, [1.5f, 2.5f, -2.0f, 0.5f]);
        var limiter = new SoftLimiterSampleProvider(source);

        var buffer = new float[4];
        int read = limiter.Read(buffer, 0, 4);

        Assert.Equal(4, read);
        // Small sample (0.5f) is untouched
        Assert.Equal(0.5f, buffer[3]);
        // Loud samples are smoothly saturated via tanh and bounded to < 1.0f
        Assert.True(buffer[0] < 1.0f && buffer[0] > 0.85f);
        Assert.True(buffer[1] < 1.0f && buffer[1] > 0.85f);
        Assert.True(buffer[2] > -1.0f && buffer[2] < -0.85f);
    }

    [Fact]
    public void MultiChannelToStereo_DownmixesCleanlyToStereo()
    {
        // 4-channel surround format
        var quadFormat = NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(44100, 4);
        var source = new DummySampleProvider(quadFormat, [0.4f, 0.4f, 0.2f, 0.2f]); // 1 quad frame
        var downmixer = new MultiChannelToStereoSampleProvider(source);

        Assert.Equal(2, downmixer.WaveFormat.Channels);
        var buffer = new float[2];
        int read = downmixer.Read(buffer, 0, 2);

        Assert.Equal(2, read);
        // Auxiliary channels mixed in
        Assert.True(buffer[0] > 0.4f);
        Assert.True(buffer[1] > 0.4f);
    }

    private sealed class DummySampleProvider : NAudio.Wave.ISampleProvider
    {
        private readonly float[] _data;
        public NAudio.Wave.WaveFormat WaveFormat { get; }

        public DummySampleProvider(NAudio.Wave.WaveFormat format, float[] data)
        {
            WaveFormat = format;
            _data = data;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int toCopy = Math.Min(count, _data.Length);
            Array.Copy(_data, 0, buffer, offset, toCopy);
            return toCopy;
        }
    }

    private static void CreateDummyWav(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + 8820);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); // subchunk1size (PCM)
        bw.Write((short)1); // PCM
        bw.Write((short)1); // mono
        bw.Write(44100); // sample rate
        bw.Write(44100 * 2); // byte rate
        bw.Write((short)2); // block align
        bw.Write((short)16); // bits per sample
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(8820); // 0.1s of audio data
        bw.Write(new byte[8820]);
    }
}
