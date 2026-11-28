using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ynoez.Services;

namespace ynoez;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private int _crashShutdownStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += AppDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: @"Local\ynoez-single-instance", createdNew: out _ownsMutex);
        if (!_ownsMutex)
        {
            MessageBox.Show("O ynoez já está aberto. Use a janela existente para evitar sessões de otimização duplicadas.",
                "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            AppPaths.EnsureStructure();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "O ynoez não conseguiu preparar C:\\ynoez. Verifique as permissões da pasta e tente executar o instalador oficial novamente.\n\n" +
                ex.Message,
                "ynoez — inicialização", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        AppLog.Write("Aplicativo iniciado. Versão: " + UpdateService.GetCurrentVersion());
        try
        {
            var priorityRecovery = ProcessPriorityProfileService.RecoverInterruptedSession();
            var savedPriorityProfile = ProcessPriorityProfileService.LoadProfile();
            if (savedPriorityProfile.Enabled)
                ProcessPriorityProfileService.SaveProfile(savedPriorityProfile with { Enabled = false });

            if (priorityRecovery.Warnings.Count > 0)
                AppLog.Write("O perfil de foco não será retomado automaticamente: a restauração anterior tem itens pendentes. " + priorityRecovery.ToDisplayText());
            else
                AppLog.Write("Sessão anterior de foco restaurada. O perfil salvo exige ativação manual nesta execução.");
        }
        catch (Exception ex)
        {
            AppLog.Write("Não foi possível recuperar prioridades de uma sessão anterior: " + ex);
            try
            {
                var savedPriorityProfile = ProcessPriorityProfileService.LoadProfile();
                ProcessPriorityProfileService.SaveProfile(savedPriorityProfile with { Enabled = false });
            }
            catch { }
        }

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

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write("Exceção não tratada na interface WPF: " + e.Exception);
        if (Interlocked.Exchange(ref _crashShutdownStarted, 1) != 0) return;

        e.Handled = true;
        try
        {
            MessageBox.Show(
                "O ynoez encontrou um erro inesperado e será fechado para evitar continuar com estado interno inconsistente.\n\n" +
                "O diagnóstico foi salvo em C:\\ynoez\\Logs.\n\n" + e.Exception.Message,
                "ynoez — erro inesperado", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
        Shutdown(-1);
    }

    private static void AppDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            AppLog.Write("Exceção fatal fora da interface: " + ex);
        else
            AppLog.Write("Exceção fatal não gerenciada. IsTerminating=" + e.IsTerminating);
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Write("Exceção de Task não observada: " + e.Exception);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { ProcessPriorityProfileService.StopSession(); }
        catch (Exception ex) { AppLog.Write("Falha ao restaurar prioridades de processos durante o encerramento: " + ex); }
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
