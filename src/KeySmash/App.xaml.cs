using System.Threading;
using System.Windows;

namespace KeySmash;

public partial class App : Application
{
    private static Mutex? _instanceMutex;

    public static bool StartInBackground { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        const string mutexName = "Local\\KeySmash_SingleInstance_App";
        _instanceMutex = new Mutex(true, mutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            // exit immediately if another instance is already running
            Shutdown();
            return;
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
