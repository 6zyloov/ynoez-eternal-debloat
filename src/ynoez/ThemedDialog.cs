using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ynoez;

/// <summary>Diálogos escuros e consistentes com o tema do ynoez.</summary>
internal static class ThemedDialog
{
    public static bool Confirm(Window? owner, string title, string message, string confirmText = "CONFIRMAR") =>
        Show(owner, title, message, null, confirmText, true);

    public static void Info(Window? owner, string title, string message, string? detail = null) =>
        _ = Show(owner, title, message, detail, "ENTENDI", false);

    private static bool Show(Window? owner, string title, string message, string? detail, string actionText, bool confirm)
    {
        var window = new Window
        {
            Title = title,
            Width = 540,
            MinHeight = 250,
            MaxHeight = 620,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(7, 7, 7)),
            Foreground = Brushes.White,
            ShowInTaskbar = false,
            Owner = owner
        };

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(22)
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "YNOEZ  /  " + title.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 128, 255)),
            Margin = new Thickness(0, 0, 0, 13)
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        });
        if (!string.IsNullOrWhiteSpace(detail))
        {
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(18, 18, 18)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 12, 0, 0),
                Child = new ScrollViewer
                {
                    MaxHeight = 190,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        Text = detail,
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            });
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        if (confirm)
        {
            var cancel = CreateButton("CANCELAR", false);
            cancel.Click += (_, _) => window.DialogResult = false;
            actions.Children.Add(cancel);
        }
        var accept = CreateButton(actionText, true);
        accept.Margin = new Thickness(confirm ? 8 : 0, 0, 0, 0);
        accept.Click += (_, _) => window.DialogResult = true;
        actions.Children.Add(accept);
        panel.Children.Add(actions);
        border.Child = panel;
        window.Content = border;
        return window.ShowDialog() == true;
    }

    private static Button CreateButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 100,
            Height = 34,
            Padding = new Thickness(13, 5, 13, 5),
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(primary ? Color.FromRgb(65, 28, 65) : Color.FromRgb(25, 25, 25)),
            BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(255, 128, 255) : Color.FromRgb(65, 65, 65)),
            BorderThickness = new Thickness(1)
        };
        return button;
    }
}
