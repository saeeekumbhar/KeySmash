namespace KeySmash.Audio;

public sealed class SoundPack
{
    private readonly Random _random = new();
    private int _lastSampleIndex = -1;

    public string Name { get; }
    public string? DirectoryPath { get; }
    public bool IsBuiltIn { get; }
    public string DisplayType => IsBuiltIn ? "(built-in)" : "(custom)";
    public List<CachedSound> Samples { get; } = new();

    public int LastSampleIndex => _lastSampleIndex;

    public SoundPack(string name, string? directoryPath = null, bool isBuiltIn = false)
    {
        Name = name;
        DirectoryPath = directoryPath;
        IsBuiltIn = isBuiltIn;
    }

    public CachedSound? GetNextSample(bool randomize)
    {
        if (Samples.Count == 0)
            return null;

        if (Samples.Count == 1)
        {
            _lastSampleIndex = 0;
            return Samples[0];
        }

        int nextIndex;

        if (randomize)
        {
            // select a sample different from the previous one
            do
            {
                nextIndex = _random.Next(Samples.Count);
            } while (nextIndex == _lastSampleIndex);
        }
        else
        {
            nextIndex = (_lastSampleIndex + 1) % Samples.Count;
        }

        _lastSampleIndex = nextIndex;
        return Samples[nextIndex];
    }
}
