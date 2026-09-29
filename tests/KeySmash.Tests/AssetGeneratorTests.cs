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
        Assert.True(File.Exists(Path.Combine(assetsDir, "Typewriter", "space_01.wav")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Typewriter", "enter_01.wav")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Typewriter", "backspace_01.wav")));

        Assert.True(File.Exists(Path.Combine(assetsDir, "Mechanical", "mechanical_01.wav")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Mechanical", "space_01.wav")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Mechanical", "enter_01.wav")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Mechanical", "backspace_01.wav")));

        Assert.True(File.Exists(Path.Combine(assetsDir, "Bubble", "bubble_01.wav")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Bubble", "space_01.wav")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Bubble", "enter_01.wav")));

        Assert.True(File.Exists(Path.Combine(assetsDir, "Shotgun", "shotgun_01.wav")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Shotgun", "space_01.wav")));
        Assert.True(File.Exists(Path.Combine(assetsDir, "Shotgun", "enter_01.wav")));
    }

    private static void GenerateTypewriter(string dir)
    {
        Directory.CreateDirectory(dir);
        var rand = new Random(42);

        // General letters
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

        // Spacebar: deeper mechanical carriage advance
        {
            var duration = 0.09f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var env = MathF.Exp(-t * 70f);
                var thud = MathF.Sin(2f * MathF.PI * 480f * t) * env * 0.8f;
                var ratchet = ((float)rand.NextDouble() * 2f - 1f) * MathF.Exp(-t * 160f) * 0.45f;
                var body = MathF.Sin(2f * MathF.PI * 1100f * t) * MathF.Exp(-t * 100f) * 0.4f;
                samples[s] = Math.Clamp(thud + ratchet + body, -1f, 1f);
            }
            WriteWav(Path.Combine(dir, "space_01.wav"), samples);
        }

        // Enter: typewriter carriage bell ring + return latch
        {
            var duration = 0.22f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var bellEnv = MathF.Exp(-t * 16f);
                var bell1 = MathF.Sin(2f * MathF.PI * 2800f * t) * bellEnv * 0.6f;
                var bell2 = MathF.Sin(2f * MathF.PI * 5600f * t) * MathF.Exp(-t * 24f) * 0.3f;
                var clunk = MathF.Sin(2f * MathF.PI * 320f * t) * MathF.Exp(-t * 80f) * 0.5f;
                var noise = ((float)rand.NextDouble() * 2f - 1f) * MathF.Exp(-t * 140f) * 0.25f;
                samples[s] = Math.Clamp(bell1 + bell2 + clunk + noise, -1f, 1f);
            }
            WriteWav(Path.Combine(dir, "enter_01.wav"), samples);
        }

        // Backspace: escapement wheel reverse ratchet
        {
            var duration = 0.07f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var env = MathF.Exp(-t * 110f);
                var click = MathF.Sin(2f * MathF.PI * 2400f * t) * env * 0.7f;
                var noise = ((float)rand.NextDouble() * 2f - 1f) * MathF.Exp(-t * 260f) * 0.5f;
                samples[s] = Math.Clamp(click + noise, -1f, 1f);
            }
            WriteWav(Path.Combine(dir, "backspace_01.wav"), samples);
        }
    }

    private static void GenerateMechanical(string dir)
    {
        Directory.CreateDirectory(dir);
        var rand = new Random(101);

        // General letters
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

        // Spacebar: deep stabilizer "thock" bottom-out
        {
            var duration = 0.085f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var lowThock = MathF.Sin(2f * MathF.PI * 140f * t) * MathF.Exp(-t * 45f) * 0.85f;
                var stabClack = MathF.Sin(2f * MathF.PI * 1800f * t) * MathF.Exp(-t * 200f) * 0.5f;
                var stabRattle = ((float)rand.NextDouble() * 2f - 1f) * MathF.Exp(-t * 250f) * 0.35f;
                samples[s] = Math.Clamp(lowThock + stabClack + stabRattle, -1f, 1f);
            }
            WriteWav(Path.Combine(dir, "space_01.wav"), samples);
        }

        // Enter: heavy punchy switch bottom-out
        {
            var duration = 0.075f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var punch = MathF.Sin(2f * MathF.PI * 260f * t) * MathF.Exp(-t * 55f) * 0.8f;
                var clack = MathF.Sin(2f * MathF.PI * 2600f * t) * MathF.Exp(-t * 220f) * 0.65f;
                var noise = ((float)rand.NextDouble() * 2f - 1f) * MathF.Exp(-t * 320f) * 0.3f;
                samples[s] = Math.Clamp(punch + clack + noise, -1f, 1f);
            }
            WriteWav(Path.Combine(dir, "enter_01.wav"), samples);
        }

        // Backspace: crisp tactile return snap
        {
            var duration = 0.06f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var snap = MathF.Sin(2f * MathF.PI * 3100f * t) * MathF.Exp(-t * 260f) * 0.85f;
                var thud = MathF.Sin(2f * MathF.PI * 380f * t) * MathF.Exp(-t * 80f) * 0.4f;
                samples[s] = Math.Clamp(snap + thud, -1f, 1f);
            }
            WriteWav(Path.Combine(dir, "backspace_01.wav"), samples);
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

        // Spacebar: deep bass bubble
        {
            var duration = 0.12f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var env = MathF.Exp(-t * 35f);
                var freq = 220f + (t * 1100f);
                var wave = MathF.Sin(2f * MathF.PI * freq * t) * env;
                samples[s] = Math.Clamp(wave * 0.95f, -1f, 1f);
            }
            WriteWav(Path.Combine(dir, "space_01.wav"), samples);
        }

        // Enter: bright musical splash bubble
        {
            var duration = 0.13f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var env = MathF.Exp(-t * 40f);
                var freq1 = 700f + (t * 3200f);
                var freq2 = 1050f + (t * 1800f);
                var wave = (MathF.Sin(2f * MathF.PI * freq1 * t) * 0.6f + MathF.Sin(2f * MathF.PI * freq2 * t) * 0.4f) * env;
                samples[s] = Math.Clamp(wave * 0.85f, -1f, 1f);
            }
            WriteWav(Path.Combine(dir, "enter_01.wav"), samples);
        }

        // Backspace: quick mini-blip
        {
            var duration = 0.05f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var env = MathF.Exp(-t * 90f);
                var freq = 900f + (t * 1500f);
                var wave = MathF.Sin(2f * MathF.PI * freq * t) * env;
                samples[s] = Math.Clamp(wave * 0.75f, -1f, 1f);
            }
            WriteWav(Path.Combine(dir, "backspace_01.wav"), samples);
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

        // Spacebar: heavy metallic pump action rack
        {
            var duration = 0.14f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var rack1 = MathF.Sin(2f * MathF.PI * 1800f * t) * MathF.Exp(-t * 90f) * 0.5f;
                var rack2 = MathF.Sin(2f * MathF.PI * 720f * t) * MathF.Exp(-t * 50f) * 0.6f;
                var metal = ((float)rand.NextDouble() * 2f - 1f) * MathF.Exp(-t * 100f) * 0.4f;
                samples[s] = Math.Clamp(rack1 + rack2 + metal, -1f, 1f);
            }
            WriteWav(Path.Combine(dir, "space_01.wav"), samples);
        }

        // Enter: deep blast boom
        {
            var duration = 0.20f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var blastEnv = MathF.Exp(-t * 22f);
                var sub = MathF.Sin(2f * MathF.PI * 75f * t) * blastEnv * 0.9f;
                var blast = ((float)rand.NextDouble() * 2f - 1f) * blastEnv * 0.7f;
                samples[s] = Math.Clamp(sub + blast, -1f, 1f);
            }
            WriteWav(Path.Combine(dir, "enter_01.wav"), samples);
        }

        // Backspace: shell casing eject
        {
            var duration = 0.08f;
            var numSamples = (int)(SampleRate * duration);
            var samples = new float[numSamples];
            for (int s = 0; s < numSamples; s++)
            {
                var t = (float)s / SampleRate;
                var clink = MathF.Sin(2f * MathF.PI * 3800f * t) * MathF.Exp(-t * 80f) * 0.65f;
                var noise = ((float)rand.NextDouble() * 2f - 1f) * MathF.Exp(-t * 180f) * 0.35f;
                samples[s] = Math.Clamp(clink + noise, -1f, 1f);
            }
            WriteWav(Path.Combine(dir, "backspace_01.wav"), samples);
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
