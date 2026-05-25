using System;
using System.IO;
using System.Windows;

namespace EfmlGen.Wpf;

public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        string? efmlPath = null;
        foreach (var arg in e.Args)
        {
            if (string.IsNullOrWhiteSpace(arg)) continue;
            if (!string.Equals(Path.GetExtension(arg), ".efml", StringComparison.OrdinalIgnoreCase)) continue;
            try { efmlPath = Path.GetFullPath(arg); }
            catch { efmlPath = arg; }
            break;
        }

        var window = new MainWindow(efmlPath);
        window.Show();
    }
}
