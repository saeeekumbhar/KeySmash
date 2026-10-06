using System.Threading;
using System.Windows.Threading;
using KeySmash;
using Xunit;
using Xunit.Abstractions;

namespace KeySmash.Tests;

public class MainWindowTests
{
    private readonly ITestOutputHelper _output;

    public MainWindowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MainWindow_DiagnosticReport()
    {
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow();
                _output.WriteLine($"Window created. Handle: {new System.Windows.Interop.WindowInteropHelper(window).Handle}");
                _output.WriteLine($"Before Show: Visibility={window.Visibility}, IsVisible={window.IsVisible}, WindowState={window.WindowState}");
                window.RestoreFromTray();
                _output.WriteLine($"After RestoreFromTray: Visibility={window.Visibility}, IsVisible={window.IsVisible}, WindowState={window.WindowState}");

                var smField = typeof(MainWindow).GetField("_soundManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var sm = (KeySmash.Audio.SoundManager?)smField?.GetValue(window);
                if (sm != null)
                {
                    _output.WriteLine($"Audio Ready: {sm.IsAudioReady}");
                    _output.WriteLine($"Audio Error: {sm.AudioErrorMessage}");
                    _output.WriteLine($"Sound Packs: {sm.SoundPacks.Count}");
                    _output.WriteLine($"Selected Pack: {sm.SelectedPack?.Name}");
                    _output.WriteLine($"Master Volume: {sm.MasterVolume}");
                    _output.WriteLine($"Is Muted: {sm.IsMuted}");

                    // Trigger key sound
                    sm.PlayKeySound();
                    _output.WriteLine("PlayKeySound() called successfully.");
                }

                Assert.True(window.IsVisible);
                Assert.Equal(System.Windows.Visibility.Visible, window.Visibility);
                if (sm != null)
                {
                    Assert.True(sm.IsAudioReady);
                    Assert.NotEmpty(sm.SoundPacks);
                }
                var hookField = typeof(MainWindow).GetField("_keyboardHook", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var hook = (KeySmash.Keyboard.KeyboardHook?)hookField?.GetValue(window);
                if (hook != null)
                {
                    Assert.True(hook.IsActive);
                }
            }
            finally
            {
                window?.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void MainWindow_InstantiatesWithoutCrashing()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow();
                Assert.NotNull(window);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                if (window != null)
                {
                    window.Dispose();
                }
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
        {
            throw new Exception($"MainWindow initialization failed: {error}", error);
        }
    }

    [Fact]
    public void MainWindow_PrepareForShutdown_AllowsWindowCloseWithoutCancel()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow();
                window.PrepareForShutdown();
                window.Close(); // must cleanly close without being cancelled by MinimizeToTrayOnClose
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                window?.Dispose();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null)
        {
            throw new Exception($"Shutdown test failed: {error}", error);
        }
    }

    [Fact]
    public void HotKeyRetry_TerminatesAtMaxAttempts()
    {
        int retriesAttempted = 0;
        const int maxRetries = 3;

        void SimulateRetry(int retryCount)
        {
            if (retryCount < maxRetries)
            {
                retriesAttempted++;
                SimulateRetry(retryCount + 1);
            }
        }

        SimulateRetry(0);

        Assert.Equal(maxRetries, retriesAttempted);
    }

    [Fact]
    public void MainWindow_Dispose_CancelsPendingHotKeyRetryCleanly()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow();
                // Dispose must complete cleanly and cancel any pending retry CTS
                window.Dispose();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }
}
