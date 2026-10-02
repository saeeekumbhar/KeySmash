using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using KeySmash.Keyboard;
using KeySmash.Settings;
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
    private int _activeVoices;
    private const int MaxConcurrentVoices = 16;
    private SoundPack? _selectedPack;

    public List<SoundPack> SoundPacks { get; } = new();
    public SoundPack? SelectedPack
    {
        get => Volatile.Read(ref _selectedPack);
        set => Volatile.Write(ref _selectedPack, value);
    }
    public bool Randomize { get; set; } = true;
    public bool IsAudioReady { get; private set; }
    public string? AudioErrorMessage { get; private set; }
    public event Action<bool, string?>? AudioStateChanged;

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

        // Soft saturation limiter prevents digital clipping and popping on fast typing bursts
        var limiter = new SoftLimiterSampleProvider(_mixer);

        _volumeProvider = new VolumeSampleProvider(limiter)
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
                Interlocked.Exchange(ref _activeVoices, 0);
                try { _mixer.RemoveAllMixerInputs(); } catch { }

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
                    try
                    {
                        // Fall back to polling WASAPI (~25ms) if event-sync is rejected by driver
                        var wasapiPoll = new WasapiOut(AudioClientShareMode.Shared, useEventSync: false, latency: 25);
                        wasapiPoll.PlaybackStopped += OnPlaybackStopped;
                        wasapiPoll.Init(_volumeProvider);
                        wasapiPoll.Play();
                        _outputDevice = wasapiPoll;
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
                }

                IsAudioReady = true;
                AudioErrorMessage = null;
                AudioStateChanged?.Invoke(true, null);
            }
            catch (Exception)
            {
                // handle systems with no audio device or restricted permissions
                IsAudioReady = false;
                AudioErrorMessage = "Audio output device unavailable";
                _outputDevice = null;
                AudioStateChanged?.Invoke(false, AudioErrorMessage);
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
        if (_isMuted || SelectedPack == null)
            return;

        if (!IsAudioReady)
        {
            // Auto-reconnect if device was plugged in after startup
            var now = Stopwatch.GetTimestamp();
            var elapsedSec = (now - _lastRecoverAttempt) / (double)Stopwatch.Frequency;
            if (elapsedSec >= 2.0)
            {
                Task.Run(RecoverAudioDevice);
            }
            return;
        }

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

        if (!IsAudioReady)
        {
            RecoverAudioDevice();
            if (!IsAudioReady)
                return;
        }

        var sample = targetPack.GetNextSample(Randomize, category);
        if (sample == null)
            return;

        PlaySample(sample);
    }

    private void PlaySample(CachedSound sample)
    {
        lock (_lock)
        {
            if (_outputDevice == null || !IsAudioReady)
                return;

            // clamp concurrent inputs to avoid clipping and high memory churn
            if (Volatile.Read(ref _activeVoices) >= MaxConcurrentVoices)
                return;

            try
            {
                Interlocked.Increment(ref _activeVoices);
                var provider = new CachedSoundSampleProvider(sample, () =>
                {
                    Interlocked.Decrement(ref _activeVoices);
                });
                _mixer.AddMixerInput(provider);
            }
            catch (Exception)
            {
                Interlocked.Decrement(ref _activeVoices);
                Task.Run(RecoverAudioDevice);
            }
        }
    }

    public void LoadPacksFromDirectory(string directoryPath, bool isBuiltIn)
    {
        if (!Directory.Exists(directoryPath))
            return;

        lock (_lock)
        {
            foreach (var subDir in Directory.GetDirectories(directoryPath))
            {
                var rawSubDirName = Path.GetFileName(subDir);
                var packName = isBuiltIn ? rawSubDirName : ResolveSafePackName(rawSubDirName, "Custom");
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
                    .Concat(Directory.GetFiles(directoryPath, "*.aiff"))
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
            .SelectMany(ext => Directory.GetFiles(folderPath, ext, SearchOption.AllDirectories))
            .OrderBy(f => f)
            .ToList();

        // Limit maximum samples loaded per category to prevent unbounded memory churn
        const int maxSamplesPerCategory = 64;

        foreach (var file in files)
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var parentDir = Path.GetFileName(Path.GetDirectoryName(file)) ?? string.Empty;

                bool isSpace = fileName.StartsWith("space", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(parentDir, "space", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(parentDir, "spaces", StringComparison.OrdinalIgnoreCase);

                bool isEnter = fileName.StartsWith("enter", StringComparison.OrdinalIgnoreCase) ||
                               fileName.StartsWith("return", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(parentDir, "enter", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(parentDir, "return", StringComparison.OrdinalIgnoreCase);

                bool isBackspace = fileName.StartsWith("backspace", StringComparison.OrdinalIgnoreCase) ||
                                   fileName.StartsWith("back", StringComparison.OrdinalIgnoreCase) ||
                                   fileName.StartsWith("delete", StringComparison.OrdinalIgnoreCase) ||
                                   fileName.StartsWith("del", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(parentDir, "backspace", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(parentDir, "delete", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(parentDir, "del", StringComparison.OrdinalIgnoreCase);

                if (isSpace && pack.SpaceSamples.Count >= maxSamplesPerCategory) continue;
                if (isEnter && pack.EnterSamples.Count >= maxSamplesPerCategory) continue;
                if (isBackspace && pack.BackspaceSamples.Count >= maxSamplesPerCategory) continue;
                if (!isSpace && !isEnter && !isBackspace && pack.Samples.Count >= maxSamplesPerCategory) continue;

                var cached = new CachedSound(file);
                if (cached.AudioData.Length == 0)
                    continue;

                if (isSpace)
                {
                    pack.SpaceSamples.Add(cached);
                }
                else if (isEnter)
                {
                    pack.EnterSamples.Add(cached);
                }
                else if (isBackspace)
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

    public static string SanitizePackName(string? rawName, string fallback = "CustomPack")
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return fallback;

        var invalidChars = Path.GetInvalidFileNameChars();
        var clean = string.Concat(rawName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
        clean = clean.Trim('.', ' ');

        var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        if (string.IsNullOrWhiteSpace(clean) || reservedNames.Contains(clean))
            return fallback;

        return clean;
    }

    private static readonly HashSet<string> BuiltInPackNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Typewriter", "Mechanical", "Bubble", "Shotgun"
    };

    public static string ResolveSafePackName(string? rawName, string fallback = "CustomPack")
    {
        var clean = SanitizePackName(rawName, fallback);
        if (BuiltInPackNames.Contains(clean))
        {
            return $"{clean} (Custom)";
        }
        return clean;
    }

    public bool ImportCustomSoundFile(string sourceFilePath, string packName, string targetBaseDir)
    {
        if (!File.Exists(sourceFilePath))
            return false;

        try
        {
            // validate audio file readability and clamp max duration (max 15 seconds for typing effect)
            using (var reader = new AudioFileReader(sourceFilePath))
            {
                if (reader.TotalTime.TotalSeconds <= 0 || reader.TotalTime.TotalSeconds > 15)
                    return false;
            }

            var safePackName = ResolveSafePackName(packName, "Custom");
            var packDir = Path.Combine(targetBaseDir, safePackName);
            Directory.CreateDirectory(packDir);

            var fileName = Path.GetFileName(sourceFilePath);
            var destPath = Path.Combine(packDir, fileName);
            File.Copy(sourceFilePath, destPath, overwrite: true);

            var pack = LoadPackFromFolder(packDir, safePackName, isBuiltIn: false);
            if (pack != null && (pack.Samples.Count > 0 || pack.SpaceSamples.Count > 0))
            {
                lock (_lock)
                {
                    SoundPacks.RemoveAll(p => string.Equals(p.Name, pack.Name, StringComparison.OrdinalIgnoreCase));
                    SoundPacks.Add(pack);
                    SortPacks();
                    SelectedPack = pack;
                }
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

        string? packDir = null;
        try
        {
            var rawName = Path.GetFileNameWithoutExtension(zipFilePath)?.Trim();
            var packName = ResolveSafePackName(rawName, "CustomPack");

            packDir = Path.Combine(targetBaseDir, packName);
            var fullDest = Path.GetFullPath(packDir) + Path.DirectorySeparatorChar;

            const long maxTotalUncompressedBytes = 250 * 1024 * 1024; // 250 MB max
            const int maxEntryCount = 500;
            const long maxSingleFileBytes = 50 * 1024 * 1024; // 50 MB max

            var safeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".wav", ".mp3", ".aiff", ".ogg", ".flac", ".txt", ".json", ".md"
            };

            using (var archive = ZipFile.OpenRead(zipFilePath))
            {
                if (archive.Entries.Count > maxEntryCount)
                    return false;

                long totalUncompressed = 0;
                foreach (var entry in archive.Entries)
                {
                    if (entry.Length > maxSingleFileBytes)
                        return false;

                    totalUncompressed += entry.Length;
                    if (totalUncompressed > maxTotalUncompressedBytes)
                        return false;

                    // Zip slip traversal check
                    var entryDest = Path.GetFullPath(Path.Combine(packDir, entry.FullName));
                    if (!entryDest.StartsWith(fullDest, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                Directory.CreateDirectory(packDir);

                // Extract ONLY safe files (audio and metadata), rejecting all executables/scripts
                // Use streaming decompression byte tracking to protect against zip bomb expansion
                int extractedAudioCount = 0;
                long totalStreamedBytes = 0;

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue; // directory entry

                    var ext = Path.GetExtension(entry.Name);
                    if (!safeExtensions.Contains(ext))
                        continue; // skip unsafe files (.exe, .bat, .dll, etc.)

                    var destPath = Path.GetFullPath(Path.Combine(packDir, entry.FullName));
                    var entryDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(entryDir))
                    {
                        Directory.CreateDirectory(entryDir);
                    }

                    using (var sourceStream = entry.Open())
                    using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var copyBuffer = new byte[81920];
                        int readBytes;
                        long fileStreamedBytes = 0;

                        while ((readBytes = sourceStream.Read(copyBuffer, 0, copyBuffer.Length)) > 0)
                        {
                            fileStreamedBytes += readBytes;
                            totalStreamedBytes += readBytes;

                            if (fileStreamedBytes > maxSingleFileBytes || totalStreamedBytes > maxTotalUncompressedBytes)
                            {
                                throw new InvalidDataException("Zip archive exceeded maximum allowable uncompressed size limit.");
                            }

                            destStream.Write(copyBuffer, 0, readBytes);
                        }
                    }

                    if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".aiff", StringComparison.OrdinalIgnoreCase))
                    {
                        extractedAudioCount++;
                    }
                }

                if (extractedAudioCount == 0)
                {
                    if (Directory.Exists(packDir))
                        Directory.Delete(packDir, true);
                    return false;
                }
            }

            // If the zip contained a single nested root folder, flatten it while preserving relative subpaths
            var subDirs = Directory.GetDirectories(packDir);
            var rootFiles = Directory.GetFiles(packDir);
            if (rootFiles.Length == 0 && subDirs.Length == 1)
            {
                var nestedDir = subDirs[0];
                foreach (var file in Directory.GetFiles(nestedDir, "*.*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(nestedDir, file);
                    var dest = Path.Combine(packDir, relative);
                    var parent = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }
                    File.Move(file, dest, overwrite: true);
                }
                try { Directory.Delete(nestedDir, true); } catch { }
            }

            var pack = LoadPackFromFolder(packDir, packName, isBuiltIn: false);
            if (pack != null && (pack.Samples.Count > 0 || pack.SpaceSamples.Count > 0))
            {
                lock (_lock)
                {
                    SoundPacks.RemoveAll(p => string.Equals(p.Name, pack.Name, StringComparison.OrdinalIgnoreCase));
                    SoundPacks.Add(pack);
                    SortPacks();
                    SelectedPack = pack;
                }
                return true;
            }

            if (Directory.Exists(packDir))
            {
                Directory.Delete(packDir, recursive: true);
            }
        }
        catch
        {
            if (!string.IsNullOrEmpty(packDir) && Directory.Exists(packDir))
            {
                try { Directory.Delete(packDir, recursive: true); } catch { }
            }
            return false;
        }

        return false;
    }

    public bool DeleteCustomPack(SoundPack pack, string? baseUserSoundsDir = null)
    {
        if (pack.IsBuiltIn || string.IsNullOrEmpty(pack.DirectoryPath))
            return false;

        try
        {
            var soundsDir = baseUserSoundsDir ?? AppSettings.UserSoundsDirectory;
            var userSoundsRoot = Path.GetFullPath(soundsDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var targetPackDir = Path.GetFullPath(pack.DirectoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            bool isRootSoundsFolder = string.Equals(userSoundsRoot, targetPackDir, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(pack.Name, "Custom Sounds", StringComparison.OrdinalIgnoreCase);

            // Security check: Never delete the sounds root directory itself or a loose sounds folder!
            if (isRootSoundsFolder)
            {
                // Delete only loose audio files in root; NEVER delete the root directory itself!
                var looseFiles = Directory.GetFiles(targetPackDir, "*.wav")
                    .Concat(Directory.GetFiles(targetPackDir, "*.mp3"))
                    .Concat(Directory.GetFiles(targetPackDir, "*.aiff"));
                foreach (var file in looseFiles)
                {
                    try { File.Delete(file); } catch { }
                }

                lock (_lock)
                {
                    SoundPacks.Remove(pack);
                    if (SelectedPack == pack)
                    {
                        SelectedPack = SoundPacks.FirstOrDefault();
                    }
                }
                return true;
            }

            // Security jail: target directory must strictly be a child subdirectory within the user sounds directory
            var expectedPrefix = userSoundsRoot + Path.DirectorySeparatorChar;
            if (!targetPackDir.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Guard against deleting system root or profile directories
            var root = Path.GetPathRoot(targetPackDir)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(root, targetPackDir, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), targetPackDir, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), targetPackDir, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (Directory.Exists(targetPackDir))
            {
                Directory.Delete(targetPackDir, recursive: true);
            }

            lock (_lock)
            {
                SoundPacks.Remove(pack);
                if (SelectedPack == pack)
                {
                    SelectedPack = SoundPacks.FirstOrDefault();
                }
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
            Interlocked.Exchange(ref _activeVoices, 0);
            try { _mixer.RemoveAllMixerInputs(); } catch { }

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

public sealed class SoftLimiterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    public WaveFormat WaveFormat => _source.WaveFormat;

    public SoftLimiterSampleProvider(ISampleProvider source)
    {
        _source = source;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        const float threshold = 0.85f;
        const float margin = 0.15f;
        const float marginSq = margin * margin;

        for (int i = 0; i < read; i++)
        {
            float s = buffer[offset + i];
            // Continuous C1-smooth soft-knee saturation prevents popping and digital clipping
            if (s > threshold)
            {
                float diff = s - threshold;
                buffer[offset + i] = threshold + diff / MathF.Sqrt(1f + (diff * diff) / marginSq);
            }
            else if (s < -threshold)
            {
                float diff = -s - threshold;
                buffer[offset + i] = -(threshold + diff / MathF.Sqrt(1f + (diff * diff) / marginSq));
            }
        }
        return read;
    }
}
