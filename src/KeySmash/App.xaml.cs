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

    private static void Log(string message)
    {
        try
        {
            var logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KeySmash");
            System.IO.Directory.CreateDirectory(logDir);
            var logPath = System.IO.Path.Combine(logDir, "app.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log($"Unhandled AppDomain exception: {args.ExceptionObject}");
        };
        DispatcherUnhandledException += (s, args) =>
        {
            Log($"Unhandled Dispatcher exception: {args.Exception}");
        };
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log($"Unobserved Task exception: {args.Exception}");
        };

        Log("App startup initiated");
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        bool isNewInstance;
        try
        {
            _instanceMutex = new Mutex(true, MutexName, out isNewInstance);
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed or was killed; we take ownership safely
            isNewInstance = true;
        }

        if (!isNewInstance)
        {
            Log("Another instance is already running. Signaling existing instance and shutting down.");
            _instanceMutex?.Dispose();
            _instanceMutex = null;

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

        Log("First instance detected; continuing initialization.");

        // Create IPC event handle for subsequent instance activation
        try
        {
            long lastRestoreTimestamp = 0;
            _showEventHandle = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            _listenerThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        if (_showEventHandle.WaitOne())
                        {
                            var now = System.Diagnostics.Stopwatch.GetTimestamp();
                            var elapsed = (now - lastRestoreTimestamp) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                            // Rate limit restoration triggers to at most once per 1000ms to eliminate focus-stealing loops
                            if (elapsed < 1000)
                            {
                                continue;
                            }
                            lastRestoreTimestamp = now;

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

        Log("Instantiating MainWindow...");
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        Log($"MainWindow instantiated. StartInBackground={StartInBackground}");

        if (!StartInBackground)
        {
            Log("Calling RestoreFromTray on MainWindow...");
            mainWindow.RestoreFromTray();
            Log("RestoreFromTray finished.");
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        Log("OnSessionEnding triggered.");
        if (MainWindow is MainWindow mw)
        {
            mw.PrepareForShutdown();
        }
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log($"OnExit triggered. ExitCode={e.ApplicationExitCode}");
        if (MainWindow is MainWindow mw)
        {
            mw.PrepareForShutdown();
        }

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
