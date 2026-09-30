using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace KeySmash.Settings;

public sealed class AppSettings
{
    private const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "KeySmash";

    public bool Enabled { get; set; } = true;
    public float MasterVolume { get; set; } = 0.75f;
    public string SelectedSoundPack { get; set; } = "Typewriter";
    public bool Randomize { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool SuppressHeldKeyRepeats { get; set; } = true;

    [JsonIgnore]
    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeySmash");

    [JsonIgnore]
    public static string SettingsFilePath =>
        Path.Combine(SettingsDirectory, "settings.json");

    [JsonIgnore]
    public static string UserSoundsDirectory =>
        Path.Combine(SettingsDirectory, "sounds");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var settings = JsonSerializer.Deserialize<AppSettings>(json, options);
                if (settings != null)
                {
                    // sanitize values
                    settings.MasterVolume = Math.Clamp(settings.MasterVolume, 0f, 1f);
                    return settings;
                }
            }
        }
        catch
        {
            // fall back to defaults if file is corrupt or unreadable
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            var tempFilePath = SettingsFilePath + ".tmp";
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, SettingsFilePath, overwrite: true);
        }
        catch
        {
            // silently handle disk write errors
        }
    }

    public static bool IsStartupRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, writable: false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartupRegistration(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, writable: true);
            if (key == null)
                return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AppName, $"\"{exePath}\" --background");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // ignore registry access restrictions
        }
    }
}
