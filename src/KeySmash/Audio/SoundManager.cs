using System.IO;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace KeySmash.Audio;

public sealed class SoundManager : IDisposable
{
    private readonly MixingSampleProvider _mixer;
    private readonly VolumeSampleProvider _volumeProvider;
    private IWavePlayer? _outputDevice;
    private float _masterVolume = 0.75f;
    private bool _isMuted;
    private readonly object _lock = new();

    public List<SoundPack> SoundPacks { get; } = new();
    public SoundPack? SelectedPack { get; set; }
    public bool Randomize { get; set; } = true;
    public bool IsAudioReady { get; private set; }
    public string? AudioErrorMessage { get; private set; }

    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Math.Clamp(value, 0f, 1f);
            UpdateEffectiveVolume();
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            UpdateEffectiveVolume();
        }
    }

    public SoundManager()
    {
        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        _mixer = new MixingSampleProvider(waveFormat)
        {
            // keep device output stream open continuously for zero-latency response
            ReadFully = true
        };

        _volumeProvider = new VolumeSampleProvider(_mixer)
        {
            Volume = _masterVolume
        };

        InitializeDevice();
    }

    public void InitializeDevice()
    {
        lock (_lock)
        {
            try
            {
                _outputDevice?.Stop();
                _outputDevice?.Dispose();
                _outputDevice = null;

                var waveOut = new WaveOutEvent
                {
                    DesiredLatency = 50,
                    NumberOfBuffers = 2
                };

                waveOut.Init(_volumeProvider);
                waveOut.Play();

                _outputDevice = waveOut;
                IsAudioReady = true;
                AudioErrorMessage = null;
            }
            catch (Exception)
            {
                // handle systems with no audio device or restricted permissions
                IsAudioReady = false;
                AudioErrorMessage = "Audio output device unavailable";
                _outputDevice = null;
            }
        }
    }

    private void UpdateEffectiveVolume()
    {
        _volumeProvider.Volume = _isMuted ? 0f : _masterVolume;
    }

    public void PlayKeySound()
    {
        if (_isMuted || SelectedPack == null || !IsAudioReady)
            return;

        var sample = SelectedPack.GetNextSample(Randomize);
        if (sample == null)
            return;

        PlaySample(sample);
    }

    public void PreviewSound(SoundPack? pack = null)
    {
        var targetPack = pack ?? SelectedPack;
        if (targetPack == null)
            return;

        var sample = targetPack.GetNextSample(Randomize);
        if (sample == null)
            return;

        PlaySample(sample);
    }

    private void PlaySample(CachedSound sample)
    {
        lock (_lock)
        {
            if (_outputDevice == null)
                return;

            // clamp concurrent inputs to avoid clipping and high memory churn
            if (_mixer.MixerInputs.Count() > 16)
                return;

            _mixer.AddMixerInput(new CachedSoundSampleProvider(sample));
        }
    }

    public void LoadPacksFromDirectory(string directoryPath, bool isBuiltIn)
    {
        if (!Directory.Exists(directoryPath))
            return;

        foreach (var subDir in Directory.GetDirectories(directoryPath))
        {
            var packName = Path.GetFileName(subDir);
            var pack = LoadPackFromFolder(subDir, packName, isBuiltIn);
            if (pack != null && pack.Samples.Count > 0)
            {
                // remove existing with same name if reloading
                SoundPacks.RemoveAll(p => string.Equals(p.Name, pack.Name, StringComparison.OrdinalIgnoreCase));
                SoundPacks.Add(pack);
            }
        }

        // sort built-ins first, then user packs
        SoundPacks.Sort((a, b) =>
        {
            if (a.IsBuiltIn != b.IsBuiltIn)
                return a.IsBuiltIn ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    public SoundPack? LoadPackFromFolder(string folderPath, string packName, bool isBuiltIn)
    {
        if (!Directory.Exists(folderPath))
            return null;

        var pack = new SoundPack(packName, folderPath, isBuiltIn);
        var supportedExtensions = new[] { "*.wav", "*.mp3", "*.aiff" };

        var files = supportedExtensions
            .SelectMany(ext => Directory.GetFiles(folderPath, ext))
            .OrderBy(f => f)
            .ToList();

        foreach (var file in files)
        {
            try
            {
                var cached = new CachedSound(file);
                if (cached.AudioData.Length > 0)
                    pack.Samples.Add(cached);
            }
            catch
            {
                // skip corrupted or unreadable audio files gracefully
            }
        }

        return pack;
    }

    public bool ImportCustomSoundFile(string sourceFilePath, string packName, string targetBaseDir)
    {
        if (!File.Exists(sourceFilePath))
            return false;

        try
        {
            // validate audio file readability first
            using (var reader = new AudioFileReader(sourceFilePath))
            {
                if (reader.TotalTime.TotalSeconds <= 0)
                    return false;
            }

            var packDir = Path.Combine(targetBaseDir, packName);
            Directory.CreateDirectory(packDir);

            var fileName = Path.GetFileName(sourceFilePath);
            var destPath = Path.Combine(packDir, fileName);
            File.Copy(sourceFilePath, destPath, overwrite: true);

            var pack = LoadPackFromFolder(packDir, packName, isBuiltIn: false);
            if (pack != null && pack.Samples.Count > 0)
            {
                SoundPacks.RemoveAll(p => string.Equals(p.Name, pack.Name, StringComparison.OrdinalIgnoreCase));
                SoundPacks.Add(pack);
                SelectedPack = pack;
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try
            {
                _outputDevice?.Stop();
                _outputDevice?.Dispose();
            }
            catch
            {
                // ignore shutdown disposal exceptions
            }
            finally
            {
                _outputDevice = null;
            }
        }
    }
}
