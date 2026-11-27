using System.Threading;
using System.Windows;
using ynoez.Services;

namespace ynoez;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: @"Local\ynoez-single-instance", createdNew: out _ownsMutex);
        if (!_ownsMutex)
        {
            MessageBox.Show("O ynoez já está aberto. Use a janela existente para evitar sessões de otimização duplicadas.",
                "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppPaths.EnsureStructure();
        AppLog.Write("Aplicativo iniciado.");
        try
        {
            PowerProfileService.RecoverInterruptedAutomaticSession();
        }
        catch (Exception ex)
        {
            AppLog.Write("A recuperação de uma sessão automática anterior falhou: " + ex);
            MessageBox.Show(
                "O ynoez encontrou uma sessão automática anterior que não pôde ser restaurada. O backup foi preservado.\n\n" +
                ex.Message + "\n\nNão inicie uma nova automação até resolver a restauração.",
                "ynoez — recuperação de energia", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { PowerProfileService.RecoverInterruptedAutomaticSession(); }
        catch (Exception ex) { AppLog.Write("A sessão automática permaneceu pendente durante o encerramento: " + ex); }
        PowerProfileService.ClearExecutionRequirement();
        AppLog.Write("Aplicativo encerrado.");
        if (_ownsMutex)
        {
            try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
