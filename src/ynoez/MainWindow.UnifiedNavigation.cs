using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ynoez;

public partial class MainWindow
{
    private void LoginSigil_Formed(object? sender, EventArgs e) => RevealLoginForm();

    private void RevealLoginForm()
    {
        if (LoginForm.Visibility == Visibility.Visible) return;
        LoginForm.Visibility = Visibility.Visible;
        LoginForm.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(650)));
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        // A entrada é propositalmente local/demonstrativa; não é autenticação de segurança.
        LoginOverlay.Visibility = Visibility.Collapsed;
        ShowPage("Home");
    }

    private void NavQuickOptimize_Click(object sender, RoutedEventArgs e) =>
        ShowAddonPage("Otimizar agora", BuildPerformancePage(), NavQuickOptimize, "Tweaks");

    private void NavQuickDebloat_Click(object sender, RoutedEventArgs e) =>
        ShowAddonPage("Debloat automático", BuildDebloatPage(), NavQuickDebloat, "Debloat");

    private void NavGameFocus_Click(object sender, RoutedEventArgs e) =>
        ShowAddonPage("Foco de jogo", BuildProcessFocusPage(), NavGameFocus, "Tweaks");

    private void NavFirmwareCompat_Click(object sender, RoutedEventArgs e) =>
        ShowAddonPage("BIOS / anti-cheat", BuildFirmwarePage(), NavFirmwareCompat, "Motherboard");

    private void ShowAddonPage(string title, UIElement content, Button selected, string relatedPage)
    {
        ResetQuickNavigationButtons();
        SetNavigationState(relatedPage);
        _currentPage = title;
        PageTitle.Text = title;
        PageScroll.ScrollToTop();
        PageHost.Content = content;
        selected.Background = BrushFrom("#262026");
        selected.Foreground = AccentBrush;
    }

    private void ResetQuickNavigationButtons()
    {
        foreach (var button in new[] { NavQuickOptimize, NavQuickDebloat, NavGameFocus, NavFirmwareCompat })
        {
            button.Background = BrushFrom("#121212");
            button.Foreground = BrushFrom("#FFFFFF");
        }
    }

    private Task RestartToUefiAsync()
    {
        if (!ThemedDialog.Confirm(this, "Reiniciar para a BIOS/UEFI?",
            "Salve tudo antes de continuar. O Windows será reiniciado diretamente no firmware, se o computador oferecer suporte a esse caminho. O ynoez não altera a BIOS nesta etapa.",
            "REINICIAR"))
            return Task.CompletedTask;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = "/r /fw /t 0",
                UseShellExecute = true,
                Verb = "runas"
            });
        }
        catch (Exception ex)
        {
            AppLog.Write("Não foi possível solicitar reinicialização para UEFI: " + ex);
            ThemedDialog.Info(this, "Reinicialização não solicitada", ex.Message);
        }
        return Task.CompletedTask;
    }
}
