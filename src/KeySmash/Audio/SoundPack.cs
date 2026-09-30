using KeySmash.Keyboard;

namespace KeySmash.Audio;

public sealed class SoundPack
{
    private int _lastSampleIndex = -1;
    private int _lastSpaceIndex = -1;
    private int _lastEnterIndex = -1;
    private int _lastBackspaceIndex = -1;

    public string Name { get; }
    public string? DirectoryPath { get; }
    public bool IsBuiltIn { get; }
    public string DisplayType => IsBuiltIn ? "(built-in)" : "(custom)";

    public List<CachedSound> Samples { get; } = new();
    public List<CachedSound> SpaceSamples { get; } = new();
    public List<CachedSound> EnterSamples { get; } = new();
    public List<CachedSound> BackspaceSamples { get; } = new();

    public int LastSampleIndex => _lastSampleIndex;

    public SoundPack(string name, string? directoryPath = null, bool isBuiltIn = false)
    {
        Name = name;
        DirectoryPath = directoryPath;
        IsBuiltIn = isBuiltIn;
    }

    public CachedSound? GetNextSample(bool randomize, KeyCategory category = KeyCategory.General)
    {
        List<CachedSound> targetList;
        int lastIdx;

        switch (category)
        {
            case KeyCategory.Space when SpaceSamples.Count > 0:
                targetList = SpaceSamples;
                lastIdx = _lastSpaceIndex;
                break;
            case KeyCategory.Enter when EnterSamples.Count > 0:
                targetList = EnterSamples;
                lastIdx = _lastEnterIndex;
                break;
            case KeyCategory.Backspace when BackspaceSamples.Count > 0:
                targetList = BackspaceSamples;
                lastIdx = _lastBackspaceIndex;
                break;
            default:
                targetList = Samples;
                lastIdx = _lastSampleIndex;
                break;
        }

        if (targetList.Count == 0)
        {
            targetList = Samples;
            lastIdx = _lastSampleIndex;
            if (targetList.Count == 0)
                return null;
        }

        if (targetList.Count == 1)
        {
            UpdateLastIndex(category, targetList, 0);
            return targetList[0];
        }

        int nextIndex;
        if (randomize)
        {
            do
            {
                nextIndex = Random.Shared.Next(targetList.Count);
            } while (nextIndex == lastIdx);
        }
        else
        {
            nextIndex = (lastIdx + 1) % targetList.Count;
        }

        UpdateLastIndex(category, targetList, nextIndex);
        return targetList[nextIndex];
    }

    private void UpdateLastIndex(KeyCategory category, List<CachedSound> activeList, int index)
    {
        if (ReferenceEquals(activeList, SpaceSamples))
            _lastSpaceIndex = index;
        else if (ReferenceEquals(activeList, EnterSamples))
            _lastEnterIndex = index;
        else if (ReferenceEquals(activeList, BackspaceSamples))
            _lastBackspaceIndex = index;
        else
            _lastSampleIndex = index;
    }
}
