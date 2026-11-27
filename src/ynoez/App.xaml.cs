using System.Windows;
using ynoez.Services;

namespace ynoez;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureStructure();
        AppLog.Write("Aplicativo iniciado.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLog.Write("Aplicativo encerrado.");
        base.OnExit(e);
    }
}
