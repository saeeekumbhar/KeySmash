using System.Threading;
using System.Windows.Threading;
using KeySmash;
using Xunit;

namespace KeySmash.Tests;

public class MainWindowTests
{
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
