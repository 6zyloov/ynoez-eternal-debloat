using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ynoez;

/// <summary>
/// Caixa de diálogo escura no mesmo estilo do painel, usada no lugar da MessageBox clara do Windows.
/// Criada somente em código para não depender de XAML adicional.
/// </summary>
internal static class ThemedDialog
{
    /// <summary>Pede confirmação. Retorna true somente quando o usuário clica no botão de confirmar.</summary>
    public static bool Confirm(
        Window? owner, string title, string message,
        string confirmText = "CONTINUAR", string cancelText = "CANCELAR") =>
        Show(owner, title, message, detail: null, confirmText, cancelText);

    /// <summary>Mostra um resultado. O texto de detalhe (relatório) aparece em uma área rolável.</summary>
    public static void Info(Window? owner, string title, string message, string? detail = null) =>
        Show(owner, title, message, detail, "OK", cancelText: null);

    private static bool Show(
        Window? owner, string title, string message, string? detail,
        string confirmText, string? cancelText)
    {
        var confirmed = false;
        var app = Application.Current;

        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.Height,
            Width = 520,
            MaxHeight = 680,
            Background = Brushes.Black,
            Foreground = Brushes.White,
            FontFamily = (FontFamily)app.FindResource("AppFont")
        };

        if (owner is { IsLoaded: true })
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 12)
        };
        stack.Children.Add(titleBlock);

        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 11,
            Foreground = (Brush)app.FindResource("SecondaryText"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 17
        });

        if (!string.IsNullOrWhiteSpace(detail))
        {
            var detailBlock = new TextBlock
            {
                Text = detail,
                FontSize = 10,
                Foreground = (Brush)app.FindResource("MutedText"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 16
            };
            stack.Children.Add(new Border
            {
                Margin = new Thickness(0, 14, 0, 0),
                Padding = new Thickness(12),
                Background = (Brush)app.FindResource("CardBackground"),
                BorderBrush = (Brush)app.FindResource("CardBorder"),
                BorderThickness = new Thickness(1),
                Child = new ScrollViewer
                {
                    MaxHeight = 300,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = detailBlock
                }
            });
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };

        if (cancelText is not null)
        {
            var cancel = new Button
            {
                Content = cancelText,
                Style = (Style)app.FindResource("QuietButton"),
                MinWidth = 110,
                Height = 36,
                Margin = new Thickness(0, 0, 10, 0),
                IsCancel = true
            };
            cancel.Click += (_, _) => window.Close();
            buttons.Children.Add(cancel);
        }

        var confirm = new Button
        {
            Content = confirmText,
            Style = (Style)app.FindResource("AccentButton"),
            MinWidth = 120,
            Height = 36,
            IsDefault = cancelText is null
        };
        confirm.Click += (_, _) =>
        {
            confirmed = true;
            window.Close();
        };
        buttons.Children.Add(confirm);
        stack.Children.Add(buttons);

        window.Content = new Border
        {
            BorderBrush = (Brush)app.FindResource("CardBorder"),
            BorderThickness = new Thickness(1),
            Background = Brushes.Black,
            Child = stack
        };

        window.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try { window.DragMove(); }
                catch (InvalidOperationException) { /* o botão foi solto antes do início do arraste */ }
            }
        };
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) window.Close();
        };

        window.ShowDialog();
        return confirmed;
    }
}
