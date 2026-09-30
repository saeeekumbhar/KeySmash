using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KeySmash.Audio;

public sealed class CachedSound
{
    public float[] AudioData { get; }
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

    public CachedSound(string filePath)
    {
        using var reader = new AudioFileReader(filePath);

        ISampleProvider provider = reader;

        // resample to standard 44.1khz for uniform mixing
        if (reader.WaveFormat.SampleRate != 44100)
            provider = new WdlResamplingSampleProvider(provider, 44100);

        // convert mono inputs to stereo
        if (provider.WaveFormat.Channels == 1)
            provider = new MonoToStereoSampleProvider(provider);

        var sampleList = new List<float>();
        var buffer = new float[4096];
        int count;

        while ((count = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < count; i++)
                sampleList.Add(buffer[i]);
        }

        AudioData = sampleList.ToArray();
    }

    public CachedSound(float[] audioData)
    {
        AudioData = audioData;
    }
}

public sealed class CachedSoundSampleProvider : ISampleProvider
{
    private readonly CachedSound _cachedSound;
    private readonly Action? _onCompleted;
    private long _position;
    private int _completedReported;

    public WaveFormat WaveFormat => _cachedSound.WaveFormat;

    public CachedSoundSampleProvider(CachedSound cachedSound, Action? onCompleted = null)
    {
        _cachedSound = cachedSound;
        _onCompleted = onCompleted;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var availableSamples = _cachedSound.AudioData.Length - _position;
        var samplesToCopy = Math.Min(availableSamples, count);

        if (samplesToCopy <= 0)
        {
            NotifyCompleted();
            return 0;
        }

        Array.Copy(_cachedSound.AudioData, _position, buffer, offset, samplesToCopy);
        _position += samplesToCopy;

        if (_position >= _cachedSound.AudioData.Length)
        {
            NotifyCompleted();
        }

        return (int)samplesToCopy;
    }

    private void NotifyCompleted()
    {
        if (Interlocked.Exchange(ref _completedReported, 1) == 0)
        {
            _onCompleted?.Invoke();
        }
    }
}
