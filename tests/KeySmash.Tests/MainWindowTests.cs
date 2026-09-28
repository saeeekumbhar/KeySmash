using System.Threading;
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
            try
            {
                var window = new MainWindow();
                Assert.NotNull(window);
            }
            catch (Exception ex)
            {
                error = ex;
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
}
