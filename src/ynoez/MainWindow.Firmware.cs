using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ynoez.Services;

namespace ynoez;

public partial class MainWindow
{
    private UIElement BuildFirmwarePage()
    {
        var root = PageContainer();
        root.Children.Add(Heading("BIOS / PLACA-MÃE", 18));

        var hardwareLine = Body(_hardware.BoardManufacturer + " " + _hardware.BoardProduct +
            "  •  " + _hardware.Cpu);
        hardwareLine.Margin = new Thickness(0, 5, 0, 12);
        root.Children.Add(hardwareLine);

        var faceit = new CheckBox
        {
            Content = "FACEIT Anti-Cheat",
            IsChecked = false,
            Margin = new Thickness(0, 4, 0, 4)
        };
        var riot = new CheckBox
        {
            Content = "Riot Vanguard",
            IsChecked = false,
            Margin = new Thickness(0, 4, 0, 4)
        };
        var vanguardPreCheck = new CheckBox
        {
            Content = "Vanguard Pre-Check (Windows 11 25H2+)",
            IsChecked = false,
            Margin = new Thickness(0, 4, 0, 4)
        };

        var choices = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        choices.Children.Add(faceit);
        choices.Children.Add(riot);
        choices.Children.Add(vanguardPreCheck);
        root.Children.Add(choices);

        var status = Body("Analisando firmware…");
        status.Margin = new Thickness(0, 8, 0, 12);
        root.Children.Add(status);

        var actionRow = new WrapPanel { Orientation = Orientation.Horizontal };
        var analyze = new Button
        {
            Content = "Analisar BIOS",
            Style = (Style)FindResource("QuietButton"),
            Margin = new Thickness(0, 0, 7, 0)
        };
        var apply = new Button
        {
            Content = "Aplicar perfil compatível",
            Style = (Style)FindResource("AccentButton"),
            IsEnabled = false,
            Margin = new Thickness(0, 0, 7, 0)
        };
        var openUefi = new Button
        {
            Content = "Entrar na BIOS",
            Style = (Style)FindResource("QuietButton")
        };
        actionRow.Children.Add(analyze);
        actionRow.Children.Add(apply);
        actionRow.Children.Add(openUefi);
        root.Children.Add(actionRow);

        FirmwareCompatibilitySnapshot? snapshot = null;
        var busy = false;

        async Task InspectAsync()
        {
            if (busy) return;
            busy = true;
            analyze.IsEnabled = false;
            apply.IsEnabled = false;
            status.Text = "Lendo placa, CPU, TPM, Secure Boot e requisitos dos anti-cheats…";
            try
            {
                snapshot = await FirmwareCompatibilityService.InspectAsync(_hardware);
                var detected = snapshot.DetectedAntiCheats.ToHashSet(StringComparer.OrdinalIgnoreCase);
                faceit.IsChecked = detected.Contains("faceit");
                riot.IsChecked = detected.Contains("riot-vanguard");
                vanguardPreCheck.IsChecked = false;

                var tpm = snapshot.TpmPresent
                    ? (snapshot.TpmReady ? "TPM " + snapshot.TpmVersion + " OK" : "TPM detectado, não pronto")
                    : "TPM não detectado";
                var secure = snapshot.SecureBootEnabled ? "Secure Boot ON" : "Secure Boot OFF/não confirmado";
                var virtual = snapshot.CpuVirtualizationEnabled ? "virtualização ON" : "virtualização OFF";
                var vbs = snapshot.VbsEnabled ? "VBS ON" : "VBS OFF";
                var hvci = snapshot.MemoryIntegrityEnabled ? "HVCI ON" : "HVCI OFF";
                var iommu = snapshot.DmaProtectionAvailable ? "proteção DMA disponível" : "proteção DMA não confirmada";
                var writer = snapshot.LenovoWmiAvailable
                    ? (snapshot.CanWriteBios ? "BIOS com gravação Lenovo disponível" : "BIOS Lenovo detectada, gravação bloqueada por segurança")
                    : "gravação automática não suportada por esta BIOS";
                status.Text = snapshot.BiosMode + "  •  " + tpm + "  •  " + secure + "\n" +
                    virtual + "  •  " + iommu + "  •  " + vbs + "  •  " + hvci + "\n" +
                    writer;

                apply.IsEnabled = snapshot.CanWriteBios;
                if (!snapshot.LenovoWmiAvailable)
                    status.Text += "\nO ynoez não grava offsets NVRAM genéricos. Use Entrar na BIOS para aplicar apenas o que foi detectado como necessário.";
            }
            catch (Exception ex)
            {
                AppLog.Write("Análise de compatibilidade BIOS falhou: " + ex);
                status.Text = "Não foi possível analisar o firmware: " + ex.Message;
            }
            finally
            {
                busy = false;
                analyze.IsEnabled = true;
            }
        }

        analyze.Click += async (_, _) => await InspectAsync();
        apply.Click += (_, _) =>
        {
            if (snapshot is null || busy) return;
            var selected = new List<string>();
            if (faceit.IsChecked == true) selected.Add("faceit");
            if (riot.IsChecked == true) selected.Add("riot-vanguard");
            if (vanguardPreCheck.IsChecked == true) selected.Add("vanguard-precheck");
            if (selected.Count == 0)
            {
                MessageBox.Show("Marque o anti-cheat que você utiliza.", "ynoez", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var result = FirmwareCompatibilityService.ApplySelectedProfiles(snapshot, selected);
                status.Text = result.Message;
                if (result.Applied)
                    apply.IsEnabled = false;
            }
            catch (Exception ex)
            {
                AppLog.Write("Aplicação de perfil BIOS falhou: " + ex);
                status.Text = "A BIOS recusou o perfil: " + ex.Message;
            }
        };
        openUefi.Click += async (_, _) => await RestartToUefiAsync();

        _ = InspectAsync();
        return root;
    }
}
