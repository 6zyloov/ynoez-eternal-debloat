using System.Windows;
using System.Windows.Controls;
using ynoez.Services;

namespace ynoez;

public partial class MainWindow
{
    private UIElement BuildPerformancePage()
    {
        var root = PageContainer();
        root.Children.Add(Heading("OTIMIZAÇÃO", 18));

        var status = Body("Um clique. Aplica o perfil competitivo suportado pelo Windows e pela máquina.");
        status.Margin = new Thickness(0, 5, 0, 18);
        root.Children.Add(status);

        var apply = new Button
        {
            Content = "OTIMIZAR AGORA",
            Style = (Style)FindResource("AccentButton"),
            Height = 44,
            MinWidth = 210,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var focus = new Button
        {
            Content = "FOCO DE JOGO",
            Style = (Style)FindResource("QuietButton"),
            Height = 40,
            MinWidth = 150,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        root.Children.Add(apply);
        root.Children.Add(focus);

        var busy = false;
        apply.Click += async (_, _) =>
        {
            if (busy) return;
            if (_autoProfileCts is not null)
            {
                MessageBox.Show("Desative os perfis automáticos antes de aplicar o perfil manual.", "ynoez",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(
                "Aplicar ajustes de jogo do Windows, perfil MMCSS Games e plano Alto desempenho?",
                "ynoez • perfil competitivo", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            busy = true;
            apply.IsEnabled = false;
            status.Text = "Aplicando ajustes e verificando cada alteração…";
            try
            {
                var result = await PerformancePresetService.ApplyAsync();
                status.Text = result.Summary;
                if (!result.Succeeded)
                    MessageBox.Show(result.Summary, "ynoez • ajustes parciais",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                else
                    MessageBox.Show(result.Summary, "ynoez • otimização concluída",
                        MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppLog.Write("Perfil competitivo falhou: " + ex);
                status.Text = "Não foi possível concluir a otimização: " + ex.Message;
            }
            finally
            {
                busy = false;
                apply.IsEnabled = true;
            }
        };
        focus.Click += (_, _) => ShowAddonPage("Foco de Jogo", BuildProcessFocusPage(), NavGameFocus, "Tweaks");
        return root;
    }
}
