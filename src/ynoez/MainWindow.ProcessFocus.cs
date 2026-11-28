using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ynoez.Services;

namespace ynoez;

public partial class MainWindow
{
    private sealed record FocusExecutableItem(string FilePath)
    {
        public string Display => $"{Path.GetFileName(FilePath)}  —  {FilePath}";
    }

    private TextBox? _focusExecutableTextBox;
    private ComboBox? _focusCpuPriorityCombo;
    private ComboBox? _backgroundCpuPriorityCombo;
    private ListBox? _backgroundExecutableList;
    private TextBlock? _processFocusStatus;
    private DispatcherTimer? _processFocusTimer;
    private bool _processFocusTickRunning;

    private UIElement BuildProcessFocusPage()
    {
        var settings = ProcessPriorityProfileService.LoadProfile();
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        root.Children.Add(new TextBlock
        {
            Text = "FOCO DE JOGO",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = PrimaryBrush,
            Margin = new Thickness(0, 0, 0, 6)
        });
        root.Children.Add(new TextBlock
        {
            Text = "Escolha o jogo principal e os aplicativos que devem continuar apenas em segundo plano. O ynoez reaplica as prioridades automaticamente a cada dois segundos, inclusive em novas instâncias.",
            FontSize = 11,
            Foreground = SecondaryBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var focusCard = CreateFocusCard();
        var focusPanel = new StackPanel();
        focusPanel.Children.Add(FocusHeading("JOGO PRINCIPAL"));
        focusPanel.Children.Add(FocusDescription("Este aplicativo recebe prioridade de CPU Alta (padrão, com confirmação) ou Acima do normal e prioridade de memória normal. A preferência do Windows para a GPU de alto desempenho também é configurada para o próximo início."));
        var focusPathTextBox = CreateFocusTextBox();
        focusPathTextBox.Text = settings.FocusExecutablePath ?? string.Empty;
        _focusExecutableTextBox = focusPathTextBox;

        var focusPathRow = new Grid { Margin = new Thickness(0, 10, 0, 12) };
        focusPathRow.ColumnDefinitions.Add(new ColumnDefinition());
        focusPathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        focusPathRow.Children.Add(focusPathTextBox);
        var browseFocus = CreateFocusButton("Escolher .exe", false);
        browseFocus.Margin = new Thickness(8, 0, 0, 0);
        browseFocus.Click += (_, _) =>
        {
            var path = ChooseExecutable();
            if (path is not null && _focusExecutableTextBox is not null)
                _focusExecutableTextBox.Text = path;
        };
        Grid.SetColumn(browseFocus, 1);
        focusPathRow.Children.Add(browseFocus);
        focusPanel.Children.Add(focusPathRow);

        focusPanel.Children.Add(FocusHeading("PRIORIDADE DE CPU DO JOGO"));
        _focusCpuPriorityCombo = new ComboBox
        {
            Height = 36,
            MinWidth = 260,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = BrushFrom("#292329"),
            Foreground = PrimaryBrush,
            BorderBrush = BorderBrushSoft,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 8, 0, 0)
        };
        _focusCpuPriorityCombo.Items.Add("Acima do normal — recomendado");
        _focusCpuPriorityCombo.Items.Add("Alta — agressiva");
        _focusCpuPriorityCombo.SelectedIndex = settings.UseHighCpuPriority ? 1 : 0;
        focusPanel.Children.Add(_focusCpuPriorityCombo);
        focusPanel.Children.Add(FocusDescription("A opção Alta é mais agressiva e pode prejudicar a responsividade do Windows ou do áudio em cargas extremas. Realtime nunca é usado.", new Thickness(0, 7, 0, 0)));
        focusCard.Child = focusPanel;
        root.Children.Add(focusCard);

        var backgroundCard = CreateFocusCard();
        var backgroundPanel = new StackPanel();
        backgroundPanel.Children.Add(FocusHeading("SEGUNDO PLANO / AFK"));
        backgroundPanel.Children.Add(FocusDescription("Os executáveis desta lista recebem prioridade de CPU Idle (a menor classe normal de processo) e prioridade de memória Very Low. No modo extremo, recebem CPU na classe Idle, abaixo de todas as classes normais; no modo alternativo, ficam Abaixo do normal. A memória recebe prioridade Very Low."));
        _backgroundExecutableList = new ListBox
        {
            Height = 112,
            Margin = new Thickness(0, 10, 0, 8),
            Background = BrushFrom("#171717"),
            Foreground = PrimaryBrush,
            BorderBrush = BorderBrushSoft,
            BorderThickness = new Thickness(1),
            SelectionMode = SelectionMode.Single,
            DisplayMemberPath = nameof(FocusExecutableItem.Display)
        };
        foreach (var path in settings.BackgroundExecutablePaths)
            _backgroundExecutableList.Items.Add(new FocusExecutableItem(path));
        backgroundPanel.Children.Add(_backgroundExecutableList);
        backgroundPanel.Children.Add(FocusHeading("PRIORIDADE DE CPU DOS BOTS"));
        _backgroundCpuPriorityCombo = new ComboBox
        {
            Height = 36,
            MinWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = BrushFrom("#292329"),
            Foreground = PrimaryBrush,
            BorderBrush = BorderBrushSoft,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(0, 7, 0, 10)
        };
        _backgroundCpuPriorityCombo.Items.Add("Extremamente baixa — Idle (recomendado para AFK)");
        _backgroundCpuPriorityCombo.Items.Add("Baixa — abaixo do normal (se Roblox desconectar)");
        _backgroundCpuPriorityCombo.SelectedIndex = settings.UseLowestBackgroundCpuPriority ? 0 : 1;
        backgroundPanel.Children.Add(_backgroundCpuPriorityCombo);

        var backgroundButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var addBackground = CreateFocusButton("Adicionar executável", false);
        addBackground.Click += (_, _) =>
        {
            var path = ChooseExecutable();
            if (path is null || _backgroundExecutableList is null) return;

            var focusName = Path.GetFileNameWithoutExtension(_focusExecutableTextBox?.Text ?? string.Empty);
            var backgroundName = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(focusName, backgroundName, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("O executável do jogo principal não pode também estar na lista de segundo plano.", "ynoez — Foco de Jogo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_backgroundExecutableList.Items.Cast<FocusExecutableItem>().Any(x =>
                string.Equals(Path.GetFileNameWithoutExtension(x.FilePath), backgroundName, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Esse executável já está na lista. Uma única entrada alcança todas as instâncias com o mesmo nome, incluindo os dois Roblox.", "ynoez — Foco de Jogo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _backgroundExecutableList.Items.Add(new FocusExecutableItem(path));
        };
        backgroundButtons.Children.Add(addBackground);

        var removeBackground = CreateFocusButton("Remover selecionado", false);
        removeBackground.Margin = new Thickness(8, 0, 0, 0);
        removeBackground.Click += (_, _) =>
        {
            if (_backgroundExecutableList?.SelectedItem is FocusExecutableItem item)
                _backgroundExecutableList.Items.Remove(item);
        };
        backgroundButtons.Children.Add(removeBackground);
        backgroundPanel.Children.Add(backgroundButtons);
        backgroundPanel.Children.Add(FocusDescription("Cadastre RobloxPlayerBeta.exe uma única vez: todas as instâncias do mesmo executável serão mantidas em prioridade extremamente baixa. Cadastre também o processo independente do XT, caso ele apareça com outro nome.", new Thickness(0, 9, 0, 0)));
        backgroundCard.Child = backgroundPanel;
        root.Children.Add(backgroundCard);

        var actionsCard = CreateFocusCard();
        var actionsPanel = new StackPanel();
        actionsPanel.Children.Add(FocusHeading("CONTROLE DO PERFIL"));
        actionsPanel.Children.Add(FocusDescription("O perfil fica ativo enquanto o ynoez estiver em execução. As prioridades originais são guardadas e restauradas ao desativar ou encerrar o ynoez. Se o programa fechar inesperadamente, a restauração é tentada na próxima inicialização."));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 10) };
        var activate = CreateFocusButton(settings.Enabled ? "Salvar e reaplicar" : "Ativar perfil de foco", true);
        activate.Click += (_, _) => ActivateProcessFocusProfile();
        actions.Children.Add(activate);
        var deactivate = CreateFocusButton("Desativar e restaurar", false);
        deactivate.Margin = new Thickness(8, 0, 0, 0);
        deactivate.Click += (_, _) => DeactivateProcessFocusProfile();
        actions.Children.Add(deactivate);
        actionsPanel.Children.Add(actions);

        _processFocusStatus = new TextBlock
        {
            Text = settings.Enabled ? "Perfil configurado como ativo. Verificando processos…" : "Perfil desativado. Nenhuma prioridade será alterada.",
            FontSize = 10,
            Foreground = SecondaryBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        };
        actionsPanel.Children.Add(_processFocusStatus);
        actionsCard.Child = actionsPanel;
        root.Children.Add(actionsCard);

        var limitationCard = CreateFocusCard();
        var limitationPanel = new StackPanel();
        limitationPanel.Children.Add(FocusHeading("O QUE O WINDOWS PERMITE CONTROLAR"));
        limitationPanel.Children.Add(FocusDescription("CPU: prioridade de processo configurável. Memória RAM: prioridade de páginas, não reserva nem entrega toda a RAM ao CS. GPU: preferência por adaptador de alto desempenho para o jogo e economia para os bots; isso não reserva VRAM nem cria uma prioridade universal de agendamento da GPU. A preferência gráfica é lida quando o aplicativo inicia, então feche e reabra os jogos depois de mudar o perfil."));
        limitationPanel.Children.Add(FocusDescription("Processos protegidos do Windows e o próprio ynoez são sempre ignorados. Alguns processos de anti-cheat ou de outra conta podem negar acesso; o programa informa quando não consegue aplicar uma prioridade.", new Thickness(0, 8, 0, 0)));
        limitationCard.Child = limitationPanel;
        root.Children.Add(limitationCard);

        if (settings.Enabled)
            StartProcessFocusMonitor();

        return root;
    }

    private void ActivateProcessFocusProfile()
    {
        if (_focusExecutableTextBox is null || _backgroundExecutableList is null || _focusCpuPriorityCombo is null || _backgroundCpuPriorityCombo is null)
            return;

        var focusPath = _focusExecutableTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(focusPath) || !File.Exists(focusPath) || !string.Equals(Path.GetExtension(focusPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Escolha um arquivo .exe existente para o jogo principal.", "ynoez — Foco de Jogo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var backgroundPaths = _backgroundExecutableList.Items
            .Cast<FocusExecutableItem>()
            .Select(x => x.FilePath)
            .Where(File.Exists)
            .ToList();

        if (backgroundPaths.Count != _backgroundExecutableList.Items.Count)
        {
            MessageBox.Show("Um ou mais executáveis da lista de segundo plano não existem mais. Remova-os e selecione os arquivos corretos.", "ynoez — Foco de Jogo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settings = ProcessPriorityProfileService.NormalizeSettings(new ProcessPriorityProfileSettings
        {
            Enabled = true,
            FocusExecutablePath = focusPath,
            BackgroundExecutablePaths = backgroundPaths,
            UseHighCpuPriority = _focusCpuPriorityCombo.SelectedIndex == 1,
            UseLowestBackgroundCpuPriority = _backgroundCpuPriorityCombo.SelectedIndex == 0
        });

        if (settings.BackgroundExecutablePaths.Count != backgroundPaths.Count)
        {
            MessageBox.Show("O jogo principal e um executável de segundo plano não podem ter o mesmo nome. Confira a lista.", "ynoez — Foco de Jogo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (settings.UseHighCpuPriority
            && MessageBox.Show("A prioridade Alta pode permitir que o jogo consuma quase todo o tempo de CPU e causar falhas de áudio ou reduzir a responsividade do Windows. Realtime não será usado. Deseja continuar?", "ynoez — prioridade agressiva", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var summary = ProcessPriorityProfileService.StartSession(settings);
            var isActive = ProcessPriorityProfileService.LoadProfile().Enabled;
            if (isActive)
            {
                StartProcessFocusMonitor();
                if (_processFocusStatus is not null)
                    _processFocusStatus.Text = "Perfil ativado. O ynoez continuará na bandeja enquanto esta configuração estiver ligada." + Environment.NewLine + summary.ToDisplayText();
            }
            else
            {
                _processFocusTimer?.Stop();
                if (_processFocusStatus is not null)
                    _processFocusStatus.Text = "Perfil não ativado: há alterações de uma sessão anterior que ainda precisam ser restauradas. Resolva os avisos antes de tentar novamente." + Environment.NewLine + summary.ToDisplayText();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Falha ao ativar o perfil de foco de jogo: " + ex);
            MessageBox.Show("Não foi possível ativar o perfil.\n\n" + ex.Message, "ynoez — Foco de Jogo", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeactivateProcessFocusProfile()
    {
        try
        {
            var summary = ProcessPriorityProfileService.StopSession();
            _processFocusTimer?.Stop();
            if (_processFocusStatus is not null)
                _processFocusStatus.Text = "Perfil desativado. As prioridades ainda controladas pelo ynoez foram restauradas." + Environment.NewLine + summary.ToDisplayText();
        }
        catch (Exception ex)
        {
            AppLog.Write("Falha ao desativar o perfil de foco de jogo: " + ex);
            MessageBox.Show("A restauração encontrou um erro.\n\n" + ex.Message, "ynoez — Foco de Jogo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StartProcessFocusMonitor()
    {
        if (_processFocusTimer is not null)
        {
            if (!_processFocusTimer.IsEnabled)
                _processFocusTimer.Start();
            return;
        }

        _processFocusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _processFocusTimer.Tick += ProcessFocusTimer_Tick;
        _processFocusTimer.Start();
    }

    private async void ProcessFocusTimer_Tick(object? sender, EventArgs e)
    {
        if (_processFocusTickRunning)
            return;

        _processFocusTickRunning = true;
        try
        {
            var summary = await Task.Run(ProcessPriorityProfileService.ApplyCycle);
            if (_currentPage == "Foco de Jogo" && _processFocusStatus is not null)
            {
                _processFocusStatus.Text = "Monitoramento automático ativo (atualização a cada 2 segundos)." +
                    Environment.NewLine + summary.ToDisplayText();
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("Falha no ciclo de prioridade por aplicativo: " + ex.Message);
            if (_processFocusStatus is not null && _currentPage == "Foco de Jogo")
                _processFocusStatus.Text = "Falha ao aplicar o perfil: " + ex.Message;
        }
        finally
        {
            _processFocusTickRunning = false;
        }
    }

    private string? ChooseExecutable()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione o executável do aplicativo",
            Filter = "Aplicativos (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private static Border CreateFocusCard() => new()
    {
        Background = CardBrush,
        BorderBrush = BorderBrushSoft,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(18),
        Margin = new Thickness(0, 0, 0, 13)
    };

    private static TextBlock FocusHeading(string text) => new()
    {
        Text = text,
        FontSize = 10,
        FontWeight = FontWeights.Bold,
        Foreground = AccentBrush,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static TextBlock FocusDescription(string text, Thickness? margin = null) => new()
    {
        Text = text,
        FontSize = 10,
        Foreground = SecondaryBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0, 2, 0, 0)
    };

    private static TextBox CreateFocusTextBox() => new()
    {
        Height = 36,
        FontSize = 10,
        Padding = new Thickness(10, 8, 10, 8),
        Background = BrushFrom("#171717"),
        Foreground = PrimaryBrush,
        BorderBrush = BorderBrushSoft,
        BorderThickness = new Thickness(1),
        CaretBrush = AccentBrush,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private Button CreateFocusButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 8, 14, 8),
            FontSize = 10,
            MinHeight = 34,
            Style = (Style)FindResource(primary ? "AccentButton" : "QuietButton")
        };
        return button;
    }
}
