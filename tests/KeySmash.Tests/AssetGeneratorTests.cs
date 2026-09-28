using System;
using System.IO;
using Xunit;

namespace KeySmash.Tests;

public class AssetGeneratorTests
{
    private const int SampleRate = 44100;

    [Fact]
    public void GenerateBuiltInSoundAssets()
    {
        // locate project root assets folder
        var current = AppDomain.CurrentDomain.BaseDirectory;
        var dirInfo = new DirectoryInfo(current);
        while (dirInfo != null && !File.Exists(Path.Combine(dirInfo.FullName, "KeySmash.sln")))
        {
            dirInfo = dirInfo.Parent;
        }

        var root = dirInfo?.FullName ?? Path.GetFullPath(@"..\..\..\..\..");
        var assetsDir = Path.Combine(root, "assets", "sounds");

        GenerateTypewriter(Path.Combine(assetsDir, "Typewriter"));
        GenerateMechanical(Path.Combine(assetsDir, "Mechanical"));
        GenerateBubble(Path.Combine(assetsDir, "Bubble"));
        GenerateShotgun(Path.Combine(assetsDir, "Shotgun"));

        Assert.True(Directory.Exists(Path.Combine(assetsDir, "Typewriter")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Typewriter", "typewriter_01.wav")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Mechanical", "mechanical_01.wav")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Bubble", "bubble_01.wav")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Shotgun", "shotgun_01.wav")));
    }

    private static void GenerateTypewriter(string dir)
    {
        Directory.CreateDirectory(dir);
        var rand = new Random(42);

        for (int i = 1; i <= 3; i++)
        {
            var duration = 0.08f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            var pitchMod = 1.0f + (i - 2) * 0.08f;

            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var env = MathF.Exp(-t * 85f);
                var noise = ((float)rand.NextDouble() * 2f - 1f) * MathF.Exp(-t * 220f);
                var strike = MathF.Sin(2f * MathF.PI * 1850f * pitchMod * t) * env;
                var ring = MathF.Sin(2f * MathF.PI * 4200f * pitchMod * t) * MathF.Exp(-t * 120f) * 0.4f;

                samples[s] = Math.Clamp((strike * 0.7f + noise * 0.5f + ring) * 0.8f, -1f, 1f);
            }

            WriteWav(Path.Combine(dir, $"typewriter_0{i}.wav"), samples);
        }
    }

    private static void GenerateMechanical(string dir)
    {
        Directory.CreateDirectory(dir);
        var rand = new Random(101);

        for (int i = 1; i <= 3; i++)
        {
            var duration = 0.065f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            var pitchMod = 1.0f + (i - 2) * 0.06f;

            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var clickEnv = MathF.Exp(-t * 280f);
                var thudEnv = MathF.Exp(-t * 70f);
                var click = MathF.Sin(2f * MathF.PI * 3400f * pitchMod * t) * clickEnv * 0.8f;
                var thud = MathF.Sin(2f * MathF.PI * 420f * pitchMod * t) * thudEnv * 0.5f;
                var snapNoise = ((float)rand.NextDouble() * 2f - 1f) * MathF.Exp(-t * 400f) * 0.4f;

                samples[s] = Math.Clamp(click + thud + snapNoise, -1f, 1f);
            }

            WriteWav(Path.Combine(dir, $"mechanical_0{i}.wav"), samples);
        }
    }

    private static void GenerateBubble(string dir)
    {
        Directory.CreateDirectory(dir);

        var pitches = new[] { 480f, 560f, 650f };
        for (int i = 0; i < pitches.Length; i++)
        {
            var duration = 0.09f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            var baseFreq = pitches[i];

            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var env = MathF.Exp(-t * 55f);
                var freq = baseFreq + (t * 2200f);
                var wave = MathF.Sin(2f * MathF.PI * freq * t) * env;

                samples[s] = Math.Clamp(wave * 0.85f, -1f, 1f);
            }

            WriteWav(Path.Combine(dir, $"bubble_0{i + 1}.wav"), samples);
        }
    }

    private static void GenerateShotgun(string dir)
    {
        Directory.CreateDirectory(dir);
        var rand = new Random(777);

        for (int i = 1; i <= 2; i++)
        {
            var duration = 0.12f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            var pitchMod = 1.0f + (i - 1) * 0.12f;

            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var blastEnv = MathF.Exp(-t * 35f);
                var cockEnv = MathF.Exp(-t * 180f);
                var rumble = MathF.Sin(2f * MathF.PI * 120f * pitchMod * t) * blastEnv * 0.7f;
                var cock = MathF.Sin(2f * MathF.PI * 1400f * pitchMod * t) * cockEnv * 0.5f;
                var noise = ((float)rand.NextDouble() * 2f - 1f) * blastEnv * 0.6f;

                samples[s] = Math.Clamp((rumble + cock + noise) * 0.75f, -1f, 1f);
            }

            WriteWav(Path.Combine(dir, $"shotgun_0{i}.wav"), samples);
        }
    }

    private static void WriteWav(string filePath, float[] samples)
    {
        using var stream = File.Create(filePath);
        using var writer = new BinaryWriter(stream);

        int sampleCount = samples.Length;
        int subChunk2Size = sampleCount * 2;
        int chunkSize = 36 + subChunk2Size;

        writer.Write("RIFF"u8);
        writer.Write(chunkSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(subChunk2Size);

        for (int i = 0; i < samples.Length; i++)
        {
            var shortVal = (short)(samples[i] * 32767f);
            writer.Write(shortVal);
        }
    }
}
