using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TokenPet.Services;

namespace TokenPet.Windows;

public class StatsWindow : Window
{
    private readonly TextBlock _tokensLabel = new();
    private readonly TextBlock _callsLabel = new();
    private readonly Button _gearBtn = new();
    private TokenHistory? _history;

    public event Action? OpenSettings;

    public StatsWindow()
    {
        Width = 180;
        Height = 110;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;

        MouseLeftButtonDown += (_, e) => { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };

        var panel = new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(230, 24, 24, 28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 165, 0)),
            BorderThickness = new Thickness(1),
            Child = new Grid()
        };
        var grid = panel.Child as Grid;
        if (grid == null) return;

        //// Gear button
        //_gearBtn.Content = "⚙";
        //_gearBtn.FontSize = 12;
        //_gearBtn.Background = Brushes.Transparent;
        //_gearBtn.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
        //_gearBtn.BorderThickness = new Thickness(0);
        //_gearBtn.Padding = new Thickness(6, 4, 6, 4);
        //_gearBtn.HorizontalAlignment = HorizontalAlignment.Right;
        //_gearBtn.VerticalAlignment = VerticalAlignment.Top;
        //_gearBtn.Cursor = System.Windows.Input.Cursors.Hand;
        //_gearBtn.Click += (_, _) => OpenSettings?.Invoke();
        //grid.Children.Add(_gearBtn);

        // Token count
        _tokensLabel = new TextBlock
        {
            FontSize = 28, FontWeight = FontWeights.Bold,
            Foreground = new LinearGradientBrush(
                Color.FromRgb(255, 180, 30), Color.FromRgb(255, 120, 0), 90),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            Text = "0"
        };
        grid.Children.Add(_tokensLabel);

        // Title
        var titleLabel = new TextBlock
        {
            Text = "今日 Token", FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 150)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 44, 0, 0)
        };
        grid.Children.Add(titleLabel);

        // Calls info
        _callsLabel = new TextBlock
        {
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 120)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 68, 0, 0),
            Text = "0 次 · 累计 0"
        };
        grid.Children.Add(_callsLabel);

        // Bottom line indicator
        var botLine = new Border
        {
            Height = 2, Width = 40,
            Background = new SolidColorBrush(Color.FromArgb(100, 255, 165, 0)),
            CornerRadius = new CornerRadius(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 8)
        };
        grid.Children.Add(botLine);

        Content = panel;
    }

    public void SetHistory(TokenHistory? history)
    {
        _history = history;
        Refresh();
    }

    public void Refresh()
    {
        if (_history == null)
        {
            _tokensLabel.Text = "0";
            _callsLabel.Text = "0 次 · 累计 0";
            return;
        }
        _tokensLabel.Text = FormatNum(_history.GetTodayTotal());
        _callsLabel.Text = $"{_history.GetTodayCalls()} 次 · 累计 {FormatNum(_history.GetCumulativeTokens())}";
    }

    private static string FormatNum(long n)
    {
        if (n >= 1_000_000_000) return $"{n / 1_000_000_000.0:F1}B";
        if (n >= 1_000_000) return $"{n / 1_000_000.0:F1}M";
        if (n >= 1_000) return $"{n / 1_000.0:F1}K";
        return n.ToString();
    }
}
