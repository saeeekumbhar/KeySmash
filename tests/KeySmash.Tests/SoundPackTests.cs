using KeySmash.Audio;
using KeySmash.Keyboard;
using Xunit;

namespace KeySmash.Tests;

public class SoundPackTests
{
    [Fact]
    public void EmptySoundPack_ReturnsNullSample()
    {
        var pack = new SoundPack("EmptyPack");

        var sample = pack.GetNextSample(randomize: true);

        Assert.Null(sample);
    }

    [Fact]
    public void SingleSamplePack_ReturnsOnlySample()
    {
        var pack = new SoundPack("Single");
        var singleSample = new CachedSound(new float[] { 0.1f, 0.2f });
        pack.Samples.Add(singleSample);

        var first = pack.GetNextSample(randomize: true);
        var second = pack.GetNextSample(randomize: true);

        Assert.Same(singleSample, first);
        Assert.Same(singleSample, second);
    }

    [Fact]
    public void MultiSamplePack_NonRepeatingRandomSelection()
    {
        var pack = new SoundPack("Multi");
        var s1 = new CachedSound(new float[] { 0.1f });
        var s2 = new CachedSound(new float[] { 0.2f });
        var s3 = new CachedSound(new float[] { 0.3f });
        pack.Samples.AddRange(new[] { s1, s2, s3 });

        CachedSound? previous = null;
        for (int i = 0; i < 50; i++)
        {
            var current = pack.GetNextSample(randomize: true);
            Assert.NotNull(current);

            if (previous != null)
            {
                // consecutive samples must never be the exact same sample
                Assert.NotSame(previous, current);
            }

            previous = current;
        }
    }

    [Fact]
    public void SequentialSelection_CyclesRoundRobinWhenNotRandom()
    {
        var pack = new SoundPack("Sequential");
        var s1 = new CachedSound(new float[] { 0.1f });
        var s2 = new CachedSound(new float[] { 0.2f });
        var s3 = new CachedSound(new float[] { 0.3f });
        pack.Samples.AddRange(new[] { s1, s2, s3 });

        var first = pack.GetNextSample(randomize: false);
        var second = pack.GetNextSample(randomize: false);
        var third = pack.GetNextSample(randomize: false);
        var fourth = pack.GetNextSample(randomize: false);

        Assert.Same(s1, first);
        Assert.Same(s2, second);
        Assert.Same(s3, third);
        Assert.Same(s1, fourth); // wraps back around
    }

    [Fact]
    public void DisplayType_ReflectsBuiltInStatus()
    {
        var builtIn = new SoundPack("Typewriter", isBuiltIn: true);
        var custom = new SoundPack("MyCustom", isBuiltIn: false);

        Assert.Equal("(built-in)", builtIn.DisplayType);
        Assert.Equal("(custom)", custom.DisplayType);
    }

    [Fact]
    public void KeyCategory_ReturnsCategorySpecificSample_WhenAvailable()
    {
        var pack = new SoundPack("CustomWithSpace");
        var generalSample = new CachedSound(new float[] { 0.1f });
        var spaceSample = new CachedSound(new float[] { 0.9f });
        var enterSample = new CachedSound(new float[] { 0.5f });

        pack.Samples.Add(generalSample);
        pack.SpaceSamples.Add(spaceSample);
        pack.EnterSamples.Add(enterSample);

        var general = pack.GetNextSample(randomize: true, KeyCategory.General);
        var space = pack.GetNextSample(randomize: true, KeyCategory.Space);
        var enter = pack.GetNextSample(randomize: true, KeyCategory.Enter);

        Assert.Same(generalSample, general);
        Assert.Same(spaceSample, space);
        Assert.Same(enterSample, enter);
    }

    [Fact]
    public void KeyCategory_FallsBackToGeneral_WhenCategoryEmpty()
    {
        var pack = new SoundPack("FallbackPack");
        var generalSample = new CachedSound(new float[] { 0.1f });
        pack.Samples.Add(generalSample);

        // No space sample added; should gracefully fall back to general
        var sample = pack.GetNextSample(randomize: true, KeyCategory.Space);

        Assert.Same(generalSample, sample);
    }
}
