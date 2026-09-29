using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using KeySmash.Audio;
using KeySmash.Keyboard;
using KeySmash.Settings;
using Forms = System.Windows.Forms;

namespace KeySmash;

public partial class MainWindow : Window
{
    private const int MuteHotKeyId = 0x9001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint VkM = 0x4D;

    private readonly KeyboardHook _keyboardHook = new();
    private readonly SoundManager _soundManager = new();
    private readonly AppSettings _settings;
    private Forms.NotifyIcon? _notifyIcon;
    private bool _isExiting;
    private bool _isInitializing = true;

    // tray menu items for dynamic updates
    private Forms.ToolStripMenuItem? _trayEnabledItem;
    private Forms.ToolStripMenuItem? _trayMuteItem;
    private Forms.ToolStripMenuItem? _traySoundItem;
    private Forms.ToolStripMenuItem? _trayVolumeItem;

    public MainWindow()
    {
        InitializeComponent();

        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
            }
            catch
            {
                // ignore icon decoding errors
            }
        }

        _settings = AppSettings.Load();

        InitializeAudio();
        InitializeTray();
        ApplySettingsToUi();

        _keyboardHook.KeyPressed += OnKeyPressed;

        if (_settings.Enabled)
            _keyboardHook.Start();

        _isInitializing = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            var helper = new WindowInteropHelper(this);
            RegisterHotKey(helper.Handle, MuteHotKeyId, ModControl | ModShift, VkM);
            var source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(HwndHook);
        }
        catch
        {
            // ignore hotkey registration failure if already occupied
        }
    }

    private nint HwndHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        const int WmHotkey = 0x0312;
        if (msg == WmHotkey && wParam.ToInt32() == MuteHotKeyId)
        {
            ToggleMute();
            handled = true;
        }
        return nint.Zero;
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

        _trayMuteItem = new Forms.ToolStripMenuItem(_soundManager.IsMuted ? "Unmute" : "Mute", null, (s, e) =>
        {
            Dispatcher.Invoke(ToggleMute);
        });

        _traySoundItem = new Forms.ToolStripMenuItem($"Sound: {_soundManager.SelectedPack?.Name ?? "None"}")
        {
            Enabled = false
        };

        var volPct = (int)Math.Round(_soundManager.MasterVolume * 100);
        _trayVolumeItem = new Forms.ToolStripMenuItem(_soundManager.IsMuted ? $"Volume: {volPct}% (Muted)" : $"Volume: {volPct}%")
        {
            Enabled = false
        };

        var openItem = new Forms.ToolStripMenuItem("Open", null, (s, e) => RestoreFromTray());
        var exitItem = new Forms.ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());

        contextMenu.Items.Add(titleItem);
        contextMenu.Items.Add(_trayEnabledItem);
        contextMenu.Items.Add(_trayMuteItem);
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
        DeletePackButton.IsEnabled = _soundManager.SelectedPack is { IsBuiltIn: false };

        VolumeSlider.Value = Math.Round(_settings.MasterVolume * 100);
        VolumeValueLabel.Text = $"{(int)VolumeSlider.Value}%";

        RandomizeCheckBox.IsChecked = _settings.Randomize;
        StartupCheckBox.IsChecked = AppSettings.IsStartupRegistered();
    }

    private void OnKeyPressed(KeyCategory category)
    {
        // low-latency audio trigger on keystroke with key-specific realism
        _soundManager.PlayKeySound(category);
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

            DeletePackButton.IsEnabled = !pack.IsBuiltIn;
            UpdateTrayMenu();
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var volPct = (int)Math.Round(e.NewValue);

        if (_isInitializing)
        {
            if (VolumeValueLabel != null)
                VolumeValueLabel.Text = $"{volPct}%";
            return;
        }

        var volFraction = (float)(volPct / 100.0);
        _soundManager.MasterVolume = volFraction;
        _settings.MasterVolume = volFraction;

        // Auto-unmute when the user increases volume while muted
        if (_soundManager.IsMuted && volPct > 0)
        {
            _soundManager.IsMuted = false;
            MuteButton.Content = "Mute";
            if (VolumeValueLabel != null)
                VolumeValueLabel.Text = $"{volPct}%";
        }
        else if (volPct == 0 && !_soundManager.IsMuted)
        {
            _soundManager.IsMuted = true;
            MuteButton.Content = "Unmute";
            if (VolumeValueLabel != null)
                VolumeValueLabel.Text = "Muted";
        }
        else if (_soundManager.IsMuted)
        {
            if (VolumeValueLabel != null)
                VolumeValueLabel.Text = "Muted";
        }
        else
        {
            if (VolumeValueLabel != null)
                VolumeValueLabel.Text = $"{volPct}%";
        }

        _settings.Save();
        UpdateTrayMenu();
    }

    private void ToggleMute()
    {
        _soundManager.IsMuted = !_soundManager.IsMuted;

        if (_soundManager.IsMuted)
        {
            MuteButton.Content = "Unmute";
            VolumeValueLabel.Text = "Muted";
        }
        else
        {
            // If volume was zero when unmuting, bump to default audible level
            if (VolumeSlider.Value < 5)
            {
                VolumeSlider.Value = 25;
            }
            MuteButton.Content = "Mute";
            VolumeValueLabel.Text = $"{(int)VolumeSlider.Value}%";
        }

        UpdateTrayMenu();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMute();
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
            Title = "Import Custom Sound File or ZIP Sound Pack",
            Filter = "Supported Formats (*.wav;*.mp3;*.zip)|*.wav;*.mp3;*.zip|Audio Files (*.wav;*.mp3)|*.wav;*.mp3|ZIP Archives (*.zip)|*.zip|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            bool imported;
            string packName;

            if (ext == ".zip")
            {
                packName = Path.GetFileNameWithoutExtension(dialog.FileName);
                imported = _soundManager.ImportCustomZip(dialog.FileName, AppSettings.UserSoundsDirectory);
            }
            else
            {
                var fileName = Path.GetFileNameWithoutExtension(dialog.FileName);
                packName = char.ToUpper(fileName[0]) + (fileName.Length > 1 ? fileName[1..] : "");
                imported = _soundManager.ImportCustomSoundFile(dialog.FileName, packName, AppSettings.UserSoundsDirectory);
            }

            if (imported)
            {
                RefreshSoundPackList();
                MessageBox.Show($"Imported sound pack '{packName}' successfully.", "KeySmash", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Could not import sound file. Please verify that the audio file or ZIP is valid.", "KeySmash", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void DeletePackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_soundManager.SelectedPack is not { IsBuiltIn: false } pack)
        {
            MessageBox.Show("Built-in sound packs cannot be deleted.", "KeySmash", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"Are you sure you want to delete the sound pack '{pack.Name}'?", "KeySmash", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            _soundManager.DeleteCustomPack(pack);
            RefreshSoundPackList();
            UpdateTrayMenu();
        }
    }

    private void RefreshPacksButton_Click(object sender, RoutedEventArgs e)
    {
        InitializeAudio();
        RefreshSoundPackList();
        UpdateTrayMenu();
        MessageBox.Show("Sound packs refreshed from disk.", "KeySmash", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshSoundPackList()
    {
        SoundPackCombo.ItemsSource = null;
        SoundPackCombo.ItemsSource = _soundManager.SoundPacks;
        SoundPackCombo.SelectedItem = _soundManager.SelectedPack;
        DeletePackButton.IsEnabled = _soundManager.SelectedPack is { IsBuiltIn: false };
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

        if (_trayMuteItem != null)
            _trayMuteItem.Text = _soundManager.IsMuted ? "Unmute" : "Mute";

        if (_traySoundItem != null)
            _traySoundItem.Text = $"Sound: {_soundManager.SelectedPack?.Name ?? "None"}";

        if (_trayVolumeItem != null)
        {
            var volPct = (int)Math.Round(_soundManager.MasterVolume * 100);
            _trayVolumeItem.Text = _soundManager.IsMuted
                ? $"Volume: {volPct}% (Muted)"
                : $"Volume: {volPct}%";
        }
    }

    public void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
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

        try
        {
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, MuteHotKeyId);
        }
        catch
        {
            // ignore hotkey cleanup errors
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}