using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using KeySmash.Keyboard;
using NAudio.CoreAudioApi;
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
    private long _lastRecoverAttempt;

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

                try
                {
                    // Ultra-low latency event-driven WASAPI (<15ms)
                    var wasapi = new WasapiOut(AudioClientShareMode.Shared, useEventSync: true, latency: 15);
                    wasapi.PlaybackStopped += OnPlaybackStopped;
                    wasapi.Init(_volumeProvider);
                    wasapi.Play();
                    _outputDevice = wasapi;
                }
                catch
                {
                    // Fall back to WaveOutEvent if WASAPI is restricted or unavailable
                    var waveOut = new WaveOutEvent
                    {
                        DesiredLatency = 40,
                        NumberOfBuffers = 2
                    };
                    waveOut.PlaybackStopped += OnPlaybackStopped;
                    waveOut.Init(_volumeProvider);
                    waveOut.Play();
                    _outputDevice = waveOut;
                }

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

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            // Auto-recover if audio endpoint disconnected (e.g. headphones unplugged)
            Task.Run(RecoverAudioDevice);
        }
    }

    public void RecoverAudioDevice()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsedSec = (now - _lastRecoverAttempt) / (double)Stopwatch.Frequency;
        if (elapsedSec < 1.0)
            return;

        _lastRecoverAttempt = now;
        InitializeDevice();
    }

    private void UpdateEffectiveVolume()
    {
        _volumeProvider.Volume = _isMuted ? 0f : _masterVolume;
    }

    public void PlayKeySound(KeyCategory category = KeyCategory.General)
    {
        if (_isMuted || SelectedPack == null || !IsAudioReady)
            return;

        var sample = SelectedPack.GetNextSample(Randomize, category);
        if (sample == null)
            return;

        PlaySample(sample);
    }

    public void PreviewSound(SoundPack? pack = null, KeyCategory category = KeyCategory.General)
    {
        var targetPack = pack ?? SelectedPack;
        if (targetPack == null)
            return;

        var sample = targetPack.GetNextSample(Randomize, category);
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

            try
            {
                _mixer.AddMixerInput(new CachedSoundSampleProvider(sample));
            }
            catch (Exception)
            {
                Task.Run(RecoverAudioDevice);
            }
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
            if (pack != null && (pack.Samples.Count > 0 || pack.SpaceSamples.Count > 0))
            {
                // remove existing with same name if reloading
                SoundPacks.RemoveAll(p => string.Equals(p.Name, pack.Name, StringComparison.OrdinalIgnoreCase));
                SoundPacks.Add(pack);
            }
        }

        // Support loose audio files directly in directoryPath (e.g. user dropped files into sounds folder)
        if (!isBuiltIn)
        {
            var looseFiles = Directory.GetFiles(directoryPath, "*.wav")
                .Concat(Directory.GetFiles(directoryPath, "*.mp3"))
                .ToList();

            if (looseFiles.Count > 0)
            {
                var loosePack = LoadPackFromFolder(directoryPath, "Custom Sounds", isBuiltIn: false);
                if (loosePack != null && (loosePack.Samples.Count > 0 || loosePack.SpaceSamples.Count > 0))
                {
                    SoundPacks.RemoveAll(p => string.Equals(p.Name, loosePack.Name, StringComparison.OrdinalIgnoreCase));
                    SoundPacks.Add(loosePack);
                }
            }
        }

        SortPacks();
    }

    private void SortPacks()
    {
        // sort built-ins first, then custom packs alphabetically
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
                if (cached.AudioData.Length == 0)
                    continue;

                var fileName = Path.GetFileNameWithoutExtension(file);

                if (fileName.StartsWith("space", StringComparison.OrdinalIgnoreCase))
                {
                    pack.SpaceSamples.Add(cached);
                }
                else if (fileName.StartsWith("enter", StringComparison.OrdinalIgnoreCase) ||
                         fileName.StartsWith("return", StringComparison.OrdinalIgnoreCase))
                {
                    pack.EnterSamples.Add(cached);
                }
                else if (fileName.StartsWith("backspace", StringComparison.OrdinalIgnoreCase) ||
                         fileName.StartsWith("back", StringComparison.OrdinalIgnoreCase) ||
                         fileName.StartsWith("delete", StringComparison.OrdinalIgnoreCase))
                {
                    pack.BackspaceSamples.Add(cached);
                }
                else
                {
                    pack.Samples.Add(cached);
                }
            }
            catch
            {
                // skip corrupted or unreadable audio files gracefully
            }
        }

        // If only special samples were loaded, ensure at least one general fallback exists
        if (pack.Samples.Count == 0)
        {
            var fallback = pack.SpaceSamples.FirstOrDefault()
                ?? pack.EnterSamples.FirstOrDefault()
                ?? pack.BackspaceSamples.FirstOrDefault();
            if (fallback != null)
                pack.Samples.Add(fallback);
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
            if (pack != null && (pack.Samples.Count > 0 || pack.SpaceSamples.Count > 0))
            {
                SoundPacks.RemoveAll(p => string.Equals(p.Name, pack.Name, StringComparison.OrdinalIgnoreCase));
                SoundPacks.Add(pack);
                SortPacks();
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

    public bool ImportCustomZip(string zipFilePath, string targetBaseDir)
    {
        if (!File.Exists(zipFilePath))
            return false;

        try
        {
            var packName = Path.GetFileNameWithoutExtension(zipFilePath);
            var packDir = Path.Combine(targetBaseDir, packName);
            Directory.CreateDirectory(packDir);

            ZipFile.ExtractToDirectory(zipFilePath, packDir, overwriteFiles: true);

            var pack = LoadPackFromFolder(packDir, packName, isBuiltIn: false);
            if (pack != null && (pack.Samples.Count > 0 || pack.SpaceSamples.Count > 0))
            {
                SoundPacks.RemoveAll(p => string.Equals(p.Name, pack.Name, StringComparison.OrdinalIgnoreCase));
                SoundPacks.Add(pack);
                SortPacks();
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

    public bool DeleteCustomPack(SoundPack pack)
    {
        if (pack.IsBuiltIn || string.IsNullOrEmpty(pack.DirectoryPath))
            return false;

        try
        {
            if (Directory.Exists(pack.DirectoryPath))
            {
                Directory.Delete(pack.DirectoryPath, recursive: true);
            }

            SoundPacks.Remove(pack);

            if (SelectedPack == pack)
            {
                SelectedPack = SoundPacks.FirstOrDefault();
            }

            return true;
        }
        catch
        {
            return false;
        }
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
