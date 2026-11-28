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
            if (MessageBox.Show(
                "O ynoez vai remover somente aplicativos opcionais de uma lista permitida, desativar serviços/tarefas não essenciais e limpar caches. " +
                "Xbox, Store, drivers, runtimes, anti-cheats, Defender, Firewall, Windows Update, rede, áudio, impressão e pesquisa serão mantidos. Continuar?",
                "ynoez • Debloat", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            busy = true;
            run.IsEnabled = false;
            try
            {
                var progress = new Progress<string>(message => status.Text = message);
                var result = await DebloatProfileService.RunAsync(message =>
                {
                    if (!Dispatcher.HasShutdownStarted)
                        Dispatcher.Invoke(() => status.Text = message);
                });
                status.Text = result.Summary;
                if (result.Failures.Count > 0)
                    MessageBox.Show(result.Summary + "\n\nAlgumas operações não puderam ser concluídas. Consulte o log local.",
                        "ynoez • Debloat parcial", MessageBoxButton.OK, MessageBoxImage.Warning);
                else
                    MessageBox.Show(result.Summary, "ynoez • Debloat concluído",
                        MessageBoxButton.OK, MessageBoxImage.Information);
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
