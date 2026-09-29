using System.Threading;
using System.Windows;

namespace KeySmash;

public partial class App : Application
{
    private const string MutexName = "Local\\KeySmash_SingleInstance_App";
    private const string EventName = "Local\\KeySmash_SingleInstance_ShowEvent";

    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _showEventHandle;
    private static Thread? _listenerThread;

    public static bool StartInBackground { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _instanceMutex = new Mutex(true, MutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            // Signal the existing running instance to bring its window to the foreground
            try
            {
                if (EventWaitHandle.TryOpenExisting(EventName, out var existingEvent))
                {
                    existingEvent.Set();
                    existingEvent.Dispose();
                }
            }
            catch
            {
                // ignore IPC errors
            }

            Shutdown();
            return;
        }

        // Create IPC event handle for subsequent instance activation
        try
        {
            _showEventHandle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            _listenerThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_showEventHandle.WaitOne())
                        {
                            Current?.Dispatcher?.Invoke(() =>
                            {
                                if (Current.MainWindow is MainWindow mw)
                                {
                                    mw.RestoreFromTray();
                                }
                            });
                        }
                    }
                    catch (ThreadAbortException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch
                    {
                        // ignore and continue listener loop
                    }
                }
            })
            {
                IsBackground = true,
                Name = "KeySmash_SingleInstanceListener"
            };
            _listenerThread.Start();
        }
        catch
        {
            // fallback if event wait handle is restricted
        }

        if (e.Args.Length > 0)
        {
            foreach (var arg in e.Args)
            {
                if (arg.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase))
                {
                    StartInBackground = true;
                    break;
                }
            }
        }

        base.OnStartup(e);

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;

        if (!StartInBackground)
        {
            mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEventHandle?.Dispose();

        if (_instanceMutex != null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
                _instanceMutex.Dispose();
            }
            catch
            {
                // ignore shutdown mutex release errors
            }
        }

        base.OnExit(e);
    }
}
