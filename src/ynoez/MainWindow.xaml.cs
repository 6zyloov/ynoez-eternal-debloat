using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Interop;
using Microsoft.Win32;
using ynoez.Services;

namespace ynoez;

public partial class MainWindow : Window
{
    private static readonly Brush PageBrush = BrushFrom("#000000");
    private static readonly Brush CardBrush = BrushFrom("#080808");
    private static readonly Brush AccentBrush = BrushFrom("#FFFFFF");
    private static readonly Brush PrimaryBrush = BrushFrom("#FFFFFF");
    private static readonly Brush SecondaryBrush = BrushFrom("#D2D2D2");
    private static readonly Brush BorderBrushSoft = BrushFrom("#242424");

    private HardwareSnapshot _hardware = new();
    private string _currentPage = "Home";
    private bool _isBusy;
    private bool _loginRevealed;
    private bool _allowClose;
    private bool _sessionEnding;
    private bool _trayIconVisible;
    private HwndSource? _windowSource;
    private bool _trayHookAttached;

    private const uint TrayNimAdd = 0x00000000;
    private const uint TrayNimDelete = 0x00000002;
    private const uint TrayNifMessage = 0x00000001;
    private const uint TrayNifIcon = 0x00000002;
    private const uint TrayNifTip = 0x00000004;
    private const int TrayCallbackMessage = 0x8000 + 41;
    private static readonly int TaskbarCreatedMessage = unchecked((int)RegisterWindowMessage("TaskbarCreated"));
    private const int WmLeftButtonDoubleClick = 0x0203;
    private const int WmRightButtonUp = 0x0205;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string ToolTip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadIconW", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadSystemIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    public MainWindow()
    {
        InitializeComponent();
        SetNavigationState("Home");

        // Garantia: se a animação de abertura não puder avisar que terminou, o formulário aparece mesmo assim.
        var revealFailSafe = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        revealFailSafe.Tick += (_, _) =>
        {
            revealFailSafe.Stop();
            RevealLoginForm();
        };
        revealFailSafe.Start();
        Application.Current.SessionEnding += Application_SessionEnding;
        Closed += (_, _) =>
        {
            Application.Current.SessionEnding -= Application_SessionEnding;
            TimerResolutionService.Disable();
            RemoveTrayIcon();
            if (_trayHookAttached) _windowSource?.RemoveHook(TrayWindowProc);
            _trayHookAttached = false;
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        if (_windowSource is not null)
        {
            _windowSource.AddHook(TrayWindowProc);
            _trayHookAttached = true;
        }
        await RefreshHardwareAsync();
        ShowPage("Home");
        if (ProcessPriorityProfileService.LoadProfile().Enabled)
            StartProcessFocusMonitor();
    }

    private async Task RefreshHardwareAsync()
    {
        if (_isBusy) return;
        _isBusy = true;
        try
        {
            _hardware = await HardwareService.GetSnapshotAsync();
            AppLog.Write("Inventário de hardware atualizado.");
            if (_currentPage == "Home" || _currentPage == "Motherboard") ShowPage(_currentPage);
        }
        catch (Exception ex)
        {
            AppLog.Write("Falha ao atualizar hardware: " + ex.Message);
            MessageBox.Show("Não foi possível atualizar todas as informações do hardware.\n\n" + ex.Message, "ynoez", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _isBusy = false; }
    }

    private void LoginSigil_Formed(object? sender, EventArgs e) => RevealLoginForm();

    private void RevealLoginForm()
    {
        if (_loginRevealed) return;
        _loginRevealed = true;
        LoginForm.IsHitTestVisible = true;
        LoginForm.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0,
            TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            }
        });
        LoginUserBox.Focus();
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        // This is a local visual gate only. It deliberately does not authenticate,
        // store credentials, or transmit any entered text.
        LoginOverlay.Visibility = Visibility.Collapsed;
        AppLog.Write("Acesso local iniciado; o painel de login não valida credenciais.");
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var page = button.Name switch
        {
            "NavHome" => "Home",
            "NavTweaks" => "Tweaks",
            "NavDebloat" => "Debloat",
            "NavProcessFocus" => "Foco de Jogo",
            "NavBoard" => "Motherboard",
            _ => "Home"
        };
        ShowPage(page);
    }

    private void ShowPage(string page)
    {
        _currentPage = page;
        PageTitle.Text = page switch
        {
            "Tweaks" => "Otimizações",
            "Motherboard" => "BIOS / Placa-mãe",
            "Foco de Jogo" => "Foco de Jogo",
            _ => page
        };
        SetNavigationState(page);
        PageScroll.ScrollToTop();
        PageHost.Content = page switch
        {
            "Home" => BuildHome(),
            "Foco de Jogo" => BuildProcessFocusPage(),
            "Tweaks" => BuildTweaks(),
            "Debloat" => BuildDebloat(),
            "Motherboard" => BuildMotherboard(),
            _ => BuildHome()
        };
    }

    private void SetNavigationState(string page)
    {
        var map = new Dictionary<string, Button>
        {
            ["Home"] = NavHome,
            ["Tweaks"] = NavTweaks,
            ["Debloat"] = NavDebloat,
            ["Foco de Jogo"] = NavProcessFocus,
            ["Motherboard"] = NavBoard,
        };
        foreach (var item in map)
        {
            item.Value.Background = item.Key == page ? BrushFrom("#1D1D1D") : PageBrush;
            item.Value.Foreground = PrimaryBrush;
        }
    }

    private UIElement BuildHome()
    {
        var root = PageContainer();
        var brand = Heading("ynoez", 28);
        brand.Margin = new Thickness(0, 0, 0, 18);
        root.Children.Add(brand);

        var actions = new UniformGrid { Columns = 2 };
        actions.Children.Add(HomeAction("OTIMIZAR", "Tweaks"));
        actions.Children.Add(HomeAction("DEBLOAT", "Debloat"));
        actions.Children.Add(HomeAction("FOCO DE JOGO", "Foco de Jogo"));
        actions.Children.Add(HomeAction("BIOS / PLACA-MÃE", "Motherboard"));
        root.Children.Add(actions);
        return root;
    }

    private Button HomeAction(string label, string page)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)FindResource("QuietButton"),
            Height = 74,
            Margin = new Thickness(0, 0, 10, 10),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12,
            FontWeight = FontWeights.Bold
        };
        button.Click += (_, _) => ShowPage(page);
        return button;
    }

    private UIElement BuildTweaks() => BuildPerformancePage();

    private UIElement BuildDebloat() => BuildDebloatPage();

    private UIElement BuildMotherboard() => BuildFirmwarePage();

    private Task RestartToUefiAsync()
    {
        if (MessageBox.Show(
            "O Windows será reiniciado diretamente na UEFI/BIOS. Salve seus arquivos antes de continuar.",
            "Entrar na UEFI", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return Task.CompletedTask;

        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /fw /t 0") { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private static StackPanel PageContainer() => new() { Orientation = Orientation.Vertical, MinWidth = 0 };

    private static TextBlock Heading(string text, double size) => new()
    {
        Text = text, FontSize = size, FontWeight = FontWeights.SemiBold,
        Foreground = PrimaryBrush, TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6)
    };

    private static TextBlock Body(string text) => new()
    {
        Text = text, FontSize = 10.5, Foreground = SecondaryBrush,
        TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 1, 0, 0)
    };

    private static Brush BrushFrom(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        DependencyObject? current = e.OriginalSource as DependencyObject;
        while (current is not null)
        {
            if (current is Button) return;
            current = VisualTreeHelper.GetParent(current);
        }
        DragMove();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && !_sessionEnding && !Environment.HasShutdownStarted && HasBackgroundWork())
        {
            if (!EnsureTrayIcon())
            {
                AppLog.Write("Não foi possível criar o ícone da bandeja; a janela não será ocultada.");
                return;
            }

            e.Cancel = true;
            Hide();
            AppLog.Write("Janela ocultada na bandeja; operações em segundo plano continuam ativas.");
            return;
        }

        RemoveTrayIcon();
    }

    private void Application_SessionEnding(object? sender, SessionEndingCancelEventArgs e) =>
        _sessionEnding = true;

    private bool HasBackgroundWork() =>
        ProcessPriorityProfileService.LoadProfile().Enabled;

    private bool EnsureTrayIcon()
    {
        if (_trayIconVisible) return true;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return false;

        _windowSource ??= HwndSource.FromHwnd(handle);
        if (_windowSource is null)
        {
            AppLog.Write("Não foi possível obter a origem da janela para registrar os eventos da bandeja.");
            return false;
        }
        if (!_trayHookAttached)
        {
            _windowSource.AddHook(TrayWindowProc);
            _trayHookAttached = true;
        }

        var icon = LoadSystemIcon(IntPtr.Zero, new IntPtr(32512));
        if (icon == IntPtr.Zero)
        {
            AppLog.Write("O Windows não forneceu um ícone padrão para a bandeja.");
            return false;
        }

        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = handle,
            Id = 1,
            Flags = TrayNifMessage | TrayNifIcon | TrayNifTip,
            CallbackMessage = TrayCallbackMessage,
            Icon = icon,
            ToolTip = "ynoez — operações continuam em segundo plano",
            Info = string.Empty,
            InfoTitle = string.Empty,
            ItemGuid = Guid.Empty
        };

        if (!ShellNotifyIcon(TrayNimAdd, ref data))
        {
            AppLog.Write("Shell_NotifyIcon falhou ao registrar a bandeja. Win32=" + Marshal.GetLastWin32Error());
            return false;
        }

        _trayIconVisible = true;
        return true;
    }

    private void RemoveTrayIcon()
    {
        if (!_trayIconVisible) return;
        var handle = new WindowInteropHelper(this).Handle;
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = handle,
            Id = 1,
            ToolTip = string.Empty,
            Info = string.Empty,
            InfoTitle = string.Empty,
            ItemGuid = Guid.Empty
        };
        ShellNotifyIcon(TrayNimDelete, ref data);
        _trayIconVisible = false;
    }

    private IntPtr TrayWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == TaskbarCreatedMessage)
        {
            if (_trayIconVisible)
            {
                _trayIconVisible = false;
                if (!EnsureTrayIcon())
                    AppLog.Write("O Explorer reiniciou e o ícone do ynoez não pôde ser registrado novamente.");
            }
            handled = true;
            return IntPtr.Zero;
        }

        if (message != TrayCallbackMessage || !_trayIconVisible)
            return IntPtr.Zero;

        var notification = unchecked((int)lParam.ToInt64());
        if (notification == WmLeftButtonDoubleClick)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(RestoreFromTray));
        }
        else if (notification == WmRightButtonUp)
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(ShowTrayMenu));
        }
        return IntPtr.Zero;
    }

    private void ShowTrayMenu()
    {
        var menu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint,
            StaysOpen = false
        };
        var status = new MenuItem
        {
            Header = HasBackgroundWork() ? "ynoez está trabalhando em segundo plano" : "ynoez está aberto",
            IsEnabled = false
        };
        var open = new MenuItem { Header = "Abrir ynoez" };
        open.Click += (_, _) => RestoreFromTray();
        var exit = new MenuItem { Header = "Encerrar ynoez e interromper operações" };
        exit.Click += (_, _) => ExitFromTray();
        menu.Items.Add(status);
        menu.Items.Add(new Separator());
        menu.Items.Add(open);
        menu.Items.Add(exit);
        SetForegroundWindow(new WindowInteropHelper(this).Handle);
        menu.IsOpen = true;
    }

    private void RestoreFromTray()
    {
        RemoveTrayIcon();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    private void ExitFromTray()
    {
        _allowClose = true;
        RemoveTrayIcon();
        Close();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
