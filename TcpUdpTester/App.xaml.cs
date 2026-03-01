using System.Windows;
using TcpUdpTester.Core;

namespace TcpUdpTester;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        var idx = Array.IndexOf(args, "--profile");
        if (idx >= 0 && idx + 1 < args.Length)
            SettingsService.ProfileName = args[idx + 1];
    }
}
