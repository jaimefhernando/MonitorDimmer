using Shadeplay.Platform;
using Velopack;

namespace Shadeplay;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Handles install, update and uninstall hooks and exits early when Velopack runs one
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ =>
            {
                // Don't leave a startup entry pointing at a removed exe
                StartupManager.Apply(false);
            })
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
