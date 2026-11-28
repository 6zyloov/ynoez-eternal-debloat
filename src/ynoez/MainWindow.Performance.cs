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
        var undo = new Button
        {
            Content = "DESFAZER OTIMIZAÇÃO",
            Style = (Style)FindResource("QuietButton"),
            Height = 40,
            MinWidth = 150,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        root.Children.Add(apply);
        root.Children.Add(focus);
        root.Children.Add(undo);

        var busy = false;
        apply.Click += async (_, _) =>
        {
            if (busy) return;
            if (!ThemedDialog.Confirm(this, "Aplicar otimização para jogos?",
                "• A captura em segundo plano do Xbox / Game DVR será desativada.\n" +
                "• Alguns efeitos visuais (animações e transparência) serão reduzidos.\n" +
                "• O perfil MMCSS \"Games\" será ajustado e o plano de energia passará para Alto desempenho.\n\n" +
                "NVIDIA Instant Replay (Alt+Z), OBS, drivers, Defender, Windows Update e anti-cheats não serão alterados. " +
                "Cada ajuste é verificado depois de aplicado e guarda um backup do valor original.",
                "OTIMIZAR"))
                return;

            busy = true;
            apply.IsEnabled = false;
            status.Text = "Aplicando ajustes e verificando cada alteração…";
            try
            {
                var result = await PerformancePresetService.ApplyAsync();
                status.Text = result.Summary;
                ThemedDialog.Info(this,
                    result.Succeeded ? "Otimização concluída" : "Otimização parcial",
                    result.Summary);
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
        undo.Click += async (_, _) =>
        {
            if (busy) return;
            if (!ThemedDialog.Confirm(this, "Desfazer a otimização?",
                "Os ajustes de jogo, o perfil MMCSS \"Games\" e o plano de energia voltam aos valores originais guardados pelo ynoez. " +
                "Só é restaurado o que tiver backup; o resto é mantido como está.",
                "DESFAZER"))
                return;

            busy = true;
            apply.IsEnabled = false;
            undo.IsEnabled = false;
            status.Text = "Restaurando os valores originais…";
            try
            {
                var summary = await PerformancePresetService.RestoreAsync();
                status.Text = summary;
                ThemedDialog.Info(this, "Desfazer otimização", summary);
            }
            catch (Exception ex)
            {
                AppLog.Write("Desfazer otimização falhou: " + ex);
                status.Text = "Não foi possível desfazer: " + ex.Message;
            }
            finally
            {
                busy = false;
                apply.IsEnabled = true;
                undo.IsEnabled = true;
            }
        };
        focus.Click += (_, _) => ShowPage("Foco de Jogo");
        return root;
    }
}
