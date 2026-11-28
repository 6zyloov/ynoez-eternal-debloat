using System.Windows;
using System.Windows.Controls;
using ynoez.Services;

namespace ynoez;

public partial class MainWindow
{
    private UIElement BuildDebloatPage()
    {
        var root = PageContainer();
        root.Children.Add(Heading("DEBLOAT", 18));

        var status = Body("Remove aplicativos Windows opcionais, desativa serviços/tarefas dispensáveis e limpa caches conhecidos.");
        status.Margin = new Thickness(0, 5, 0, 18);
        root.Children.Add(status);

        var run = new Button
        {
            Content = "DEBLOAT AUTOMÁTICO",
            Style = (Style)FindResource("AccentButton"),
            Height = 44,
            MinWidth = 230,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var busy = false;
        run.Click += async (_, _) =>
        {
            if (busy) return;
            if (!ThemedDialog.Confirm(this, "Executar o Debloat?",
                "• Remove somente aplicativos opcionais de uma lista permitida (Notícias, Clima, Solitaire, Dicas etc.).\n" +
                "• Desativa serviços e tarefas de telemetria que não são essenciais.\n" +
                "• Limpa temporários e caches do Windows, Brave e Discord.\n\n" +
                "Atenção: a remoção de aplicativos NÃO tem restauração automática; para recuperar, reinstale pela Microsoft Store. " +
                "Serviços e tarefas desativados guardam backup e podem ser restaurados.\n\n" +
                "Ficam preservados: Microsoft Store, Xbox, Calculadora, Bloco de Notas, Pesquisa, drivers, runtimes, anti-cheats, " +
                "Defender, Firewall, Windows Update, rede, áudio e impressão.",
                "EXECUTAR DEBLOAT"))
                return;

            busy = true;
            run.IsEnabled = false;
            try
            {
                var result = await DebloatProfileService.RunAsync(message =>
                {
                    if (!Dispatcher.HasShutdownStarted)
                        Dispatcher.Invoke(() => status.Text = message);
                });
                status.Text = result.Summary;
                ThemedDialog.Info(this,
                    result.Failures.Count > 0 ? "Debloat parcial" : "Debloat concluído",
                    result.Failures.Count > 0
                        ? result.Summary + "\n\nAlgumas operações não puderam ser concluídas. Consulte o log local."
                        : result.Summary,
                    result.ReportText);
            }
            catch (Exception ex)
            {
                AppLog.Write("Debloat automático falhou: " + ex);
                status.Text = "Debloat não concluído: " + ex.Message;
            }
            finally
            {
                busy = false;
                run.IsEnabled = true;
            }
        };
        root.Children.Add(run);
        return root;
    }
}
