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

        // convert mono inputs to stereo
        if (provider.WaveFormat.Channels == 1)
        {
            provider = new MonoToStereoSampleProvider(provider);
        }
        // downmix multi-channel surround inputs (>2 channels) to stereo
        else if (provider.WaveFormat.Channels > 2)
        {
            provider = new MultiChannelToStereoSampleProvider(provider);
        }

        // resample to standard 44.1khz for uniform mixing
        if (provider.WaveFormat.SampleRate != 44100)
            provider = new WdlResamplingSampleProvider(provider, 44100);

        // Cap maximum sample length to 5 seconds to prevent unbounded memory usage or UI freezes
        const int maxDurationSeconds = 5;
        const int maxSamples = 44100 * 2 * maxDurationSeconds;

        var sampleList = new List<float>(Math.Min(44100 * 2, maxSamples));
        var buffer = new float[4096];
        int count;

        while ((count = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            var take = Math.Min(count, maxSamples - sampleList.Count);
            for (var i = 0; i < take; i++)
            {
                float val = buffer[i];
                if (float.IsNaN(val) || float.IsInfinity(val))
                    val = 0f;
                sampleList.Add(val);
            }

            if (sampleList.Count >= maxSamples)
                break;
        }

        AudioData = sampleList.ToArray();
    }

    public CachedSound(float[] audioData)
    {
        for (int i = 0; i < audioData.Length; i++)
        {
            if (float.IsNaN(audioData[i]) || float.IsInfinity(audioData[i]))
            {
                audioData[i] = 0f;
            }
        }
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

public sealed class MultiChannelToStereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private readonly float[] _sourceBuffer;

    public WaveFormat WaveFormat { get; }

    public MultiChannelToStereoSampleProvider(ISampleProvider source)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        _sourceBuffer = new float[4096 * Math.Max(2, _sourceChannels)];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int stereoFramesRequested = count / 2;
        int sourceSamplesToRead = stereoFramesRequested * _sourceChannels;
        if (sourceSamplesToRead > _sourceBuffer.Length)
            sourceSamplesToRead = _sourceBuffer.Length;

        int sourceSamplesRead = _source.Read(_sourceBuffer, 0, sourceSamplesToRead);
        int framesRead = sourceSamplesRead / _sourceChannels;

        int destIndex = offset;
        for (int frame = 0; frame < framesRead; frame++)
        {
            int srcIndex = frame * _sourceChannels;
            float left = _sourceBuffer[srcIndex];
            float right = _sourceBuffer[srcIndex + 1];

            for (int ch = 2; ch < _sourceChannels; ch++)
            {
                float extra = _sourceBuffer[srcIndex + ch] * 0.5f;
                left += extra;
                right += extra;
            }

            buffer[destIndex++] = left;
            buffer[destIndex++] = right;
        }

        return framesRead * 2;
    }
}
