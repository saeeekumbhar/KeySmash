using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeySmash.Audio;
using KeySmash.Keyboard;
using KeySmash.Settings;
using Forms = System.Windows.Forms;

namespace KeySmash;

public partial class MainWindow : Window
{
    private readonly KeyboardHook _keyboardHook = new();
    private readonly SoundManager _soundManager = new();
    private readonly AppSettings _settings;
    private Forms.NotifyIcon? _notifyIcon;
    private bool _isExiting;
    private bool _isInitializing = true;

    // tray menu items for dynamic updates
    private Forms.ToolStripMenuItem? _trayEnabledItem;
    private Forms.ToolStripMenuItem? _traySoundItem;
    private Forms.ToolStripMenuItem? _trayVolumeItem;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();

        InitializeAudio();
        InitializeTray();
        ApplySettingsToUi();

        _keyboardHook.KeyPressed += OnKeyPressed;

        if (_settings.Enabled)
            _keyboardHook.Start();

        _isInitializing = false;

        if (App.StartInBackground)
        {
            WindowState = WindowState.Minimized;
            Hide();
        }
    }

    private void InitializeAudio()
    {
        // load built-in packs from application directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var builtInPath = Path.Combine(baseDir, "sounds");
        if (!Directory.Exists(builtInPath))
        {
            // fallback for development debugging
            builtInPath = Path.Combine(baseDir, "..", "..", "..", "assets", "sounds");
        }

        _soundManager.LoadPacksFromDirectory(builtInPath, isBuiltIn: true);

        // load user custom sound packs
        var userSoundsPath = AppSettings.UserSoundsDirectory;
        Directory.CreateDirectory(userSoundsPath);
        _soundManager.LoadPacksFromDirectory(userSoundsPath, isBuiltIn: false);

        // apply audio settings
        _soundManager.MasterVolume = _settings.MasterVolume;
        _soundManager.Randomize = _settings.Randomize;

        // select configured pack
        var selected = _soundManager.SoundPacks.FirstOrDefault(p =>
            string.Equals(p.Name, _settings.SelectedSoundPack, StringComparison.OrdinalIgnoreCase))
            ?? _soundManager.SoundPacks.FirstOrDefault();

        _soundManager.SelectedPack = selected;

        if (!_soundManager.IsAudioReady && !string.IsNullOrEmpty(_soundManager.AudioErrorMessage))
        {
            FooterStatusLabel.Text = _soundManager.AudioErrorMessage;
            FooterStatusLabel.Foreground = System.Windows.Media.Brushes.Crimson;
        }
    }

    private void InitializeTray()
    {
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
        System.Drawing.Icon? trayIcon = null;

        if (File.Exists(iconPath))
        {
            trayIcon = new System.Drawing.Icon(iconPath);
        }
        else
        {
            trayIcon = System.Drawing.SystemIcons.Application;
        }

        var contextMenu = new Forms.ContextMenuStrip();

        var titleItem = new Forms.ToolStripMenuItem("KeySmash") { Enabled = false };
        titleItem.Font = new System.Drawing.Font(titleItem.Font, System.Drawing.FontStyle.Bold);

        _trayEnabledItem = new Forms.ToolStripMenuItem("Enabled", null, (s, e) =>
        {
            EnableToggle.IsChecked = !EnableToggle.IsChecked;
        })
        {
            Checked = _settings.Enabled,
            CheckOnClick = false
        };

        _traySoundItem = new Forms.ToolStripMenuItem($"Sound: {_soundManager.SelectedPack?.Name ?? "None"}")
        {
            Enabled = false
        };

        var volPct = (int)Math.Round(_soundManager.MasterVolume * 100);
        _trayVolumeItem = new Forms.ToolStripMenuItem($"Volume: {volPct}%")
        {
            Enabled = false
        };

        var openItem = new Forms.ToolStripMenuItem("Open", null, (s, e) => RestoreFromTray());
        var exitItem = new Forms.ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());

        contextMenu.Items.Add(titleItem);
        contextMenu.Items.Add(_trayEnabledItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(_traySoundItem);
        contextMenu.Items.Add(_trayVolumeItem);
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(openItem);
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = trayIcon,
            Text = "KeySmash - Keyboard Sound Utility",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (s, e) => RestoreFromTray();
    }

    private void ApplySettingsToUi()
    {
        EnableToggle.IsChecked = _settings.Enabled;
        UpdateStatusDisplay(_settings.Enabled);

        SoundPackCombo.ItemsSource = _soundManager.SoundPacks;
        SoundPackCombo.SelectedItem = _soundManager.SelectedPack;

        VolumeSlider.Value = Math.Round(_settings.MasterVolume * 100);
        VolumeValueLabel.Text = $"{(int)VolumeSlider.Value}%";

        RandomizeCheckBox.IsChecked = _settings.Randomize;
        StartupCheckBox.IsChecked = _settings.StartWithWindows;
    }

    private void OnKeyPressed()
    {
        // low-latency audio trigger on keystroke
        _soundManager.PlayKeySound();
    }

    private void EnableToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        _keyboardHook.Start();
        _settings.Enabled = true;
        _settings.Save();

        UpdateStatusDisplay(true);
        UpdateTrayMenu();
    }

    private void EnableToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        _keyboardHook.Stop();
        _settings.Enabled = false;
        _settings.Save();

        UpdateStatusDisplay(false);
        UpdateTrayMenu();
    }

    private void UpdateStatusDisplay(bool enabled)
    {
        if (enabled)
        {
            StatusLabel.Text = "Enabled";
            StatusLabel.Foreground = (SolidColorBrush)FindResource("SuccessBrush");
        }
        else
        {
            StatusLabel.Text = "Disabled";
            StatusLabel.Foreground = (SolidColorBrush)FindResource("TextSecondaryBrush");
        }
    }

    private void SoundPackCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (SoundPackCombo.SelectedItem is SoundPack pack)
        {
            _soundManager.SelectedPack = pack;
            _settings.SelectedSoundPack = pack.Name;
            _settings.Save();

            UpdateTrayMenu();
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var volPct = (int)Math.Round(e.NewValue);
        if (VolumeValueLabel != null)
            VolumeValueLabel.Text = $"{volPct}%";

        if (_isInitializing) return;

        var volFraction = (float)(volPct / 100.0);
        _soundManager.MasterVolume = volFraction;
        _settings.MasterVolume = volFraction;
        _settings.Save();

        UpdateTrayMenu();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        _soundManager.IsMuted = !_soundManager.IsMuted;

        if (_soundManager.IsMuted)
        {
            MuteButton.Content = "Unmute";
            VolumeValueLabel.Text = "Muted";
        }
        else
        {
            MuteButton.Content = "Mute";
            VolumeValueLabel.Text = $"{(int)VolumeSlider.Value}%";
        }
    }

    private void RandomizeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        var randomize = RandomizeCheckBox.IsChecked ?? true;
        _soundManager.Randomize = randomize;
        _settings.Randomize = randomize;
        _settings.Save();
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        _soundManager.PreviewSound();
    }

    private void AddSoundButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Custom Sound File",
            Filter = "Audio Files (*.wav;*.mp3)|*.wav;*.mp3|WAV Files (*.wav)|*.wav|MP3 Files (*.mp3)|*.mp3|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            var fileName = Path.GetFileNameWithoutExtension(dialog.FileName);
            var packName = char.ToUpper(fileName[0]) + (fileName.Length > 1 ? fileName[1..] : "");

            var imported = _soundManager.ImportCustomSoundFile(dialog.FileName, packName, AppSettings.UserSoundsDirectory);
            if (imported)
            {
                // refresh combo items
                SoundPackCombo.ItemsSource = null;
                SoundPackCombo.ItemsSource = _soundManager.SoundPacks;
                SoundPackCombo.SelectedItem = _soundManager.SelectedPack;

                MessageBox.Show($"Imported sound pack '{packName}' successfully.", "KeySmash", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Could not import audio file. Please check that the file is a valid audio format.", "KeySmash", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDir = AppSettings.UserSoundsDirectory;
            Directory.CreateDirectory(userDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = userDir,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore explorer launch errors
        }
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        var startWithWindows = StartupCheckBox.IsChecked ?? false;
        _settings.StartWithWindows = startWithWindows;
        _settings.Save();

        AppSettings.SetStartupRegistration(startWithWindows);
    }

    private void UpdateTrayMenu()
    {
        if (_trayEnabledItem != null)
            _trayEnabledItem.Checked = _settings.Enabled;

        if (_traySoundItem != null)
            _traySoundItem.Text = $"Sound: {_soundManager.SelectedPack?.Name ?? "None"}";

        if (_trayVolumeItem != null)
        {
            var volPct = (int)Math.Round(_soundManager.MasterVolume * 100);
            _trayVolumeItem.Text = $"Volume: {volPct}%";
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            // minimize to system tray
            Hide();
        }

        base.OnStateChanged(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExiting)
        {
            // minimize to tray instead of quitting on close
            e.Cancel = true;
            Hide();
            return;
        }

        // clean shutdown
        _keyboardHook.Stop();
        _keyboardHook.Dispose();

        _soundManager.Dispose();

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        base.OnClosing(e);
    }
}