using System.Windows;
using System.Windows.Controls;
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

        var faceit = CreateProfileCheck("FACEIT / CS2");
        var riot = CreateProfileCheck("Riot Vanguard / VALORANT");
        var easyAntiCheat = CreateProfileCheck("Easy Anti-Cheat");
        var battleye = CreateProfileCheck("BattlEye");
        var vanguardPreCheck = CreateProfileCheck("Vanguard Pre-Check (Windows 11 25H2+)");
        var choices = new StackPanel { Margin = new Thickness(0, 4, 0, 10) };
        choices.Children.Add(faceit);
        choices.Children.Add(riot);
        choices.Children.Add(easyAntiCheat);
        choices.Children.Add(battleye);
        choices.Children.Add(vanguardPreCheck);
        root.Children.Add(choices);

        var status = Body("Analisando firmware…");
        status.Margin = new Thickness(0, 8, 0, 12);
        root.Children.Add(status);

        var actionRow = new WrapPanel { Orientation = Orientation.Horizontal };
        var analyze = new Button
        {
            Content = "ANALISAR",
            Style = (Style)FindResource("QuietButton"),
            Margin = new Thickness(0, 0, 7, 0)
        };
        var apply = new Button
        {
            Content = "APLICAR BIOS",
            Style = (Style)FindResource("AccentButton"),
            IsEnabled = false,
            Margin = new Thickness(0, 0, 7, 0)
        };
        var openUefi = new Button
        {
            Content = "ENTRAR NA BIOS",
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
            status.Text = "Detectando placa, CPU, TPM, Secure Boot e anti-cheats…";
            try
            {
                snapshot = await FirmwareCompatibilityService.InspectAsync(_hardware);
                var detected = snapshot.DetectedAntiCheats.ToHashSet(StringComparer.OrdinalIgnoreCase);
                faceit.IsChecked = detected.Contains("faceit");
                riot.IsChecked = detected.Contains("riot-vanguard");
                easyAntiCheat.IsChecked = detected.Contains("easyanticheat");
                battleye.IsChecked = detected.Contains("battleye");

                var preCheckSupported = FirmwareCompatibilityService.IsVanguardPreCheckSupportedOS(_hardware);
                vanguardPreCheck.IsEnabled = preCheckSupported;
                vanguardPreCheck.IsChecked = preCheckSupported && detected.Contains("riot-vanguard");

                var tpm = snapshot.TpmPresent
                    ? (snapshot.TpmReady ? "TPM " + snapshot.TpmVersion + " OK" : "TPM detectado, não pronto")
                    : "TPM não detectado";
                var secure = snapshot.SecureBootEnabled ? "Secure Boot ON" : "Secure Boot OFF/não confirmado";
                var virtual = snapshot.CpuVirtualizationEnabled ? "virtualização ON" : "virtualização OFF";
                var vbs = snapshot.VbsEnabled ? "VBS ON" : "VBS OFF";
                var hvci = snapshot.MemoryIntegrityEnabled ? "HVCI ON" : "HVCI OFF";
                var iommu = snapshot.DmaProtectionAvailable ? "proteção DMA disponível" : "proteção DMA não confirmada";
                var writer = snapshot.LenovoWmiAvailable
                    ? (snapshot.CanWriteBios ? "BIOS com gravação Lenovo disponível" : "gravação bloqueada por segurança")
                    : "BIOS analisada; gravação automática não suportada neste modelo";
                status.Text = snapshot.BiosMode + " • " + tpm + " • " + secure + "\n" +
                    virtual + " • " + iommu + " • " + vbs + " • " + hvci + "\n" + writer;
                if (!preCheckSupported)
                    status.Text += "\nVanguard Pre-Check requer Windows 11 25H2 ou mais recente.";
                if (!snapshot.LenovoWmiAvailable)
                    status.Text += "\nEsta BIOS não oferece interface de gravação documentada compatível. Nenhum offset UEFI genérico será escrito.";
                apply.IsEnabled = snapshot.CanWriteBios;
            }
            catch (Exception ex)
            {
                AppLog.Write("Análise de compatibilidade BIOS falhou: " + ex);
                status.Text = "Falha na análise do firmware: " + ex.Message;
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
            if (easyAntiCheat.IsChecked == true) selected.Add("easyanticheat");
            if (battleye.IsChecked == true) selected.Add("battleye");
            if (vanguardPreCheck.IsChecked == true) selected.Add("vanguard-precheck");
            if (selected.Count == 0)
            {
                MessageBox.Show("Marque pelo menos um jogo ou anti-cheat que utiliza.", "ynoez",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                var result = FirmwareCompatibilityService.ApplySelectedProfiles(snapshot, selected);
                status.Text = result.Message;
                if (result.Applied)
                {
                    apply.IsEnabled = false;
                    MessageBox.Show(result.Message, "ynoez • BIOS", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(result.Message, "ynoez • BIOS não alterada",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("Aplicação do perfil BIOS falhou: " + ex);
                status.Text = "A BIOS recusou o perfil: " + ex.Message;
            }
        };
        openUefi.Click += async (_, _) => await RestartToUefiAsync();

        _ = InspectAsync();
        return root;
    }

    private static CheckBox CreateProfileCheck(string label) => new()
    {
        Content = label,
        IsChecked = false,
        Margin = new Thickness(0, 4, 0, 4),
        Foreground = System.Windows.Media.Brushes.White
    };
}
