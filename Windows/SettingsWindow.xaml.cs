using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TokenPet.Controls;
using TokenPet.Models;
using TokenPet.Services;

namespace TokenPet.Windows;

public partial class SettingsWindow : Window
{
    private readonly App? _app;
    private PetManager? _manager;
    private TokenHistory? _history;
    private ProxyServer? _proxy;
    private PetSprite? _pet;
    private Action? _onScaleChanged;
    private Action? _onStatsChanged;
    private readonly Dictionary<string, Button> _sidebarBtns = new();
    private string _activeTab = "pet";

    public SettingsWindow()
    {
        InitializeComponent();
        _app = Application.Current as App;

        _sidebarBtns["pet"] = PetTabBtn;
        _sidebarBtns["model"] = ModelTabBtn;
        _sidebarBtns["stats"] = StatsTabBtn;
        _sidebarBtns["about"] = AboutTabBtn;

        MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        ShowTab("pet");
    }

    public void SetContext(PetSprite pet, PetManager manager, TokenHistory history, ProxyServer proxy, Action? onScaleChanged = null, Action? onStatsChanged = null)
    {
        _pet = pet;
        _manager = manager;
        _history = history;
        _proxy = proxy;
        _onScaleChanged = onScaleChanged;
        _onStatsChanged = onStatsChanged;
    }

    public new void Show()
    {
        base.Show();
        RefreshCurrentTab();
    }

    private void ShowTab(string name)
    {
        _activeTab = name;
        foreach (var (key, btn) in _sidebarBtns)
            btn.Background = key == name
                ? new SolidColorBrush(Color.FromRgb(70, 70, 70))
                : Brushes.Transparent;
        RefreshCurrentTab();
    }

    private void RefreshCurrentTab()
    {
        TabContent.Children.Clear();
        switch (_activeTab)
        {
            case "pet": BuildPetTab(); break;
            case "model": BuildModelTab(); break;
            case "stats": BuildStatsTab(); break;
            case "about": BuildAboutTab(); break;
        }
    }

    private void BuildPetTab()
    {
        var stack = new StackPanel();

        stack.Children.Add(SectionHeader("宠物设置"));

        // Scale slider
        var scaleStack = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        var scaleLabel = new TextBlock
        {
            Text = $"尺寸: {(_app?.Config?.PetScale ?? 0.85):F2}x",
            Foreground = Brushes.LightGray, FontSize = 12, Margin = new Thickness(0, 0, 0, 4)
        };
        var scaleSlider = new Slider
        {
            Minimum = 0.3, Maximum = 2.5, SmallChange = 0.05, LargeChange = 0.1,
            Value = _app?.Config?.PetScale ?? 0.85
        };
        scaleSlider.ValueChanged += (_, _) =>
        {
            scaleLabel.Text = $"尺寸: {scaleSlider.Value:F2}x";
            if (_app?.Config != null) _app.Config.PetScale = scaleSlider.Value;
        };
        scaleSlider.PreviewMouseUp += (_, _) =>
        {
            _app?.Config?.Save();
            _onScaleChanged?.Invoke();
            if (_pet != null)
            {
                _pet.InvalidateMeasure();
                _pet.InvalidateVisual();
                _pet.Play("idle");
            }
        };
        scaleStack.Children.Add(scaleLabel);
        scaleStack.Children.Add(scaleSlider);
        stack.Children.Add(scaleStack);

        // Animation preview
        stack.Children.Add(SectionHeader("动作预览"));
        var animGrid = new WrapPanel { Margin = new Thickness(0, 4, 0, 8), ItemWidth = 70 };
        var animNames = new[] { "idle", "walk", "run_left", "wave", "jump", "fail", "sleep", "sprint", "sit" };
        var animLabels = new[] { "待机", "向右跑", "向左跑", "挥手", "跳跃", "失败", "等待", "奔跑", "审视" };
        for (int i = 0; i < animNames.Length; i++)
        {
            var name = animNames[i];
            var btn = new Button
            {
                Content = animLabels[i], FontSize = 11, Foreground = Brushes.LightGray,
                Background = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                Padding = new Thickness(4), Margin = new Thickness(2), Tag = name
            };
            btn.Click += (_, _) => _pet?.Play(name);
            animGrid.Children.Add(btn);
        }
        stack.Children.Add(animGrid);

        // Pet list
        stack.Children.Add(SectionHeader("形象管理"));
        var petList = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        if (_manager != null)
        {
            foreach (var pet in _manager.Pets)
            {
                var isActive = pet.Id == _manager.ActivePetId;
                var petRow = new Border
                {
                    Background = isActive
                        ? new SolidColorBrush(Color.FromArgb(80, 255, 165, 0))
                        : new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    CornerRadius = new CornerRadius(4), Padding = new Thickness(6), Margin = new Thickness(0, 2, 0, 2),
                    Tag = pet.Id
                };
                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var thumbnail = new Border
                {
                    Width = 36, Height = 39, Background = Brushes.Transparent,
                    Child = new System.Windows.Controls.Image { Width = 32, Height = 35, Stretch = Stretch.Uniform }
                };
                if (thumbnail.Child is System.Windows.Controls.Image img)
                {
                    var petDir = pet.Directory;
                    var spritePath = pet.SpritesheetPath;
                    Task.Run(() =>
                    {
                        var source = SpriteLoader.LoadSpritesheet(Path.Combine(petDir, spritePath));
                        if (source != null && source.PixelWidth >= AnimationDefs.FrameWidth && source.PixelHeight >= AnimationDefs.FrameHeight)
                        {
                            var cropped = new CroppedBitmap(source, new Int32Rect(0, 0, AnimationDefs.FrameWidth, AnimationDefs.FrameHeight));
                            cropped.Freeze();
                            Dispatcher.Invoke(() => img.Source = cropped);
                        }
                    });
                }
                Grid.SetColumn(thumbnail, 0);
                rowGrid.Children.Add(thumbnail);

                var textStack = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
                textStack.Children.Add(new TextBlock { Text = pet.DisplayName, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.Bold });
                textStack.Children.Add(new TextBlock { Text = pet.Description, Foreground = Brushes.Gray, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis });
                if (isActive) textStack.Children.Add(new TextBlock { Text = "✓ 已选中", Foreground = Brushes.Orange, FontSize = 10 });
                Grid.SetColumn(textStack, 1);
                rowGrid.Children.Add(textStack);

                var delBtn = new Button
                {
                    Content = "✕", FontSize = 10, Width = 22, Height = 22,
                    Background = Brushes.Transparent,
                    Foreground = new SolidColorBrush(Color.FromRgb(180, 60, 60)),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand, Padding = new Thickness(2),
                    VerticalAlignment = VerticalAlignment.Center
                };
                delBtn.Click += (_, _) =>
                {
                    var result = MessageBox.Show(
                        $"确定要删除宠物 \"{pet.DisplayName}\" 吗？此操作不可撤销。",
                        "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Yes)
                    {
                        var error = _manager?.DeletePet(pet.Id);
                        if (error != null) MessageBox.Show(error, "删除失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        else RefreshCurrentTab();
                    }
                };
                var delCol = new ColumnDefinition { Width = GridLength.Auto };
                rowGrid.ColumnDefinitions.Add(delCol);
                Grid.SetColumn(delBtn, 2);
                rowGrid.Children.Add(delBtn);

                rowGrid.MouseLeftButtonUp += (_, _) =>
                {
                    _manager?.SetActivePet(pet.Id);
                    RefreshCurrentTab();
                };
                petRow.Child = rowGrid;
                petList.Children.Add(petRow);
            }
        }
        stack.Children.Add(petList);

        // Import/Export/Open dir buttons
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        btnRow.Children.Add(MakeButton("导入", () => ImportPet()));
        btnRow.Children.Add(MakeButton("导出", () => ExportPet()));
        btnRow.Children.Add(MakeButton("打开目录", () => OpenPetDir()));
        stack.Children.Add(btnRow);

        TabContent.Children.Add(stack);
    }

    private void BuildModelTab()
    {
        var stack = new StackPanel();

        stack.Children.Add(SectionHeader("模型配置"));

        // Port
        stack.Children.Add(new TextBlock { Text = "端口", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 4, 0, 2) });
        var portInput = new TextBox { Text = (_app?.Config?.ProxyPort ?? 11435).ToString(), Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)), BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)), Margin = new Thickness(0, 0, 0, 8) };
        stack.Children.Add(portInput);

        // Proxy toggle
        var proxyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var proxyToggle = new CheckBox { Content = "开启代理", Foreground = Brushes.LightGray, IsChecked = _proxy?.IsActive ?? _app?.Config?.ProxyEnabled ?? false };
        var proxyStatus = new TextBlock { Text = _proxy?.IsActive == true ? " ● 运行中" : " ● 已停止", Foreground = _proxy?.IsActive == true ? new SolidColorBrush(Color.FromRgb(60, 200, 60)) : new SolidColorBrush(Color.FromRgb(200, 60, 60)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontSize = 11 };
        proxyToggle.Checked += (_, _) =>
        {
            var port = int.TryParse(portInput.Text, out var p) ? p : 11435;
            proxyStatus.Text = " ● 运行中";
            proxyStatus.Foreground = new SolidColorBrush(Color.FromRgb(60, 200, 60));
            _proxy?.Start(port, _proxy?.Targets ?? new List<ProxyTarget>());
            if (_app?.Config != null) { _app.Config.ProxyEnabled = true; _app.Config.ProxyPort = port; _app.Config.Save(); }
        };
        proxyToggle.Unchecked += (_, _) =>
        {
            _proxy?.Stop();
            proxyStatus.Text = " ● 已停止";
            proxyStatus.Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60));
            if (_app?.Config != null) { _app.Config.ProxyEnabled = false; _app.Config.Save(); }
        };
        proxyToggle.Unchecked += (_, _) =>
        {
            _proxy?.Stop();
            proxyStatus.Text = " ● 已停止";
            proxyStatus.Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60));
        };
        proxyRow.Children.Add(proxyToggle);
        proxyRow.Children.Add(proxyStatus);
        stack.Children.Add(proxyRow);

        // Forward targets (editable)
        stack.Children.Add(SectionHeader("转发目标"));
        var targetsPanel = new StackPanel();
        Action rebuildTargets = null!;
        rebuildTargets = () =>
        {
            targetsPanel.Children.Clear();
            var tlist = _proxy?.Targets ?? new List<ProxyTarget>();
            for (int i = 0; i < tlist.Count; i++)
            {
                var idx = i;
                var t = tlist[i];
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };

                var nameBox = new TextBox { Text = t.Name, Width = 80, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)), BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                nameBox.LostFocus += (_, _) => { if (idx < tlist.Count) { tlist[idx] = new ProxyTarget(nameBox.Text, tlist[idx].Prefix, tlist[idx].Host); _proxy?.SaveTargets(); } };

                var prefixBox = new TextBox { Text = t.Prefix, Width = 50, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)), BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
                prefixBox.LostFocus += (_, _) => { if (idx < tlist.Count) { tlist[idx] = new ProxyTarget(tlist[idx].Name, prefixBox.Text, tlist[idx].Host); _proxy?.SaveTargets(); } };

                var hostBox = new TextBox { Text = t.Host, Width = 150, Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)), BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
                hostBox.LostFocus += (_, _) => { if (idx < tlist.Count) { tlist[idx] = new ProxyTarget(tlist[idx].Name, tlist[idx].Prefix, hostBox.Text); _proxy?.SaveTargets(); } };

                var delBtn = new Button { Content = "✕", Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60)), BorderThickness = new Thickness(0), FontSize = 11, Cursor = Cursors.Hand, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(2) };
                delBtn.Click += (_, _) =>
                {
                    if (idx < tlist.Count) tlist.RemoveAt(idx);
                    _proxy?.SaveTargets();
                    rebuildTargets();
                };

                row.Children.Add(nameBox);
                row.Children.Add(new TextBlock { Text = "/", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) });
                row.Children.Add(prefixBox);
                row.Children.Add(new TextBlock { Text = "→", Foreground = Brushes.Gray, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) });
                row.Children.Add(hostBox);
                row.Children.Add(delBtn);
                targetsPanel.Children.Add(row);
            }

            // Add button
            var addBtn = new Button { Content = "+ 添加目标", FontSize = 11, Foreground = Brushes.LightGray, Background = new SolidColorBrush(Color.FromRgb(55, 55, 55)), BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            addBtn.Click += (_, _) =>
            {
                tlist.Add(new ProxyTarget("新目标", "new", "api.example.com"));
                _proxy?.SaveTargets();
                rebuildTargets();
            };
            targetsPanel.Children.Add(addBtn);

            // Copy URLs
            var copyStack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            copyStack.Children.Add(new TextBlock { Text = "快捷复制", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            foreach (var t in tlist)
            {
                var url = $"http://127.0.0.1:{portInput.Text}/{t.Prefix}";
                var copyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                copyRow.Children.Add(new TextBlock { Text = $"{t.Name}: {url}", Foreground = Brushes.LightGray, FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
                var copyBtn = new Button { Content = "📋", FontSize = 10, Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(100, 180, 255)), BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(4, 0, 0, 0), Tag = url };
                copyBtn.Click += (s, _) =>
                {
                    if (s is Button b && b.Tag is string u)
                    {
                        Clipboard.SetText(u);
                        b.Content = "✓";
                        Task.Delay(1000).ContinueWith(_ => Dispatcher.Invoke(() => b.Content = "📋"));
                    }
                };
                copyRow.Children.Add(copyBtn);
                copyStack.Children.Add(copyRow);
            }
            targetsPanel.Children.Add(copyStack);
        };
        rebuildTargets();
        stack.Children.Add(targetsPanel);

        TabContent.Children.Add(stack);
    }

    private void BuildStatsTab()
    {
        var stack = new StackPanel();
        stack.Children.Add(SectionHeader("用量统计"));

        // Clear button
        var clearBtn = new Button
        {
            Content = "清空统计", FontSize = 11, Foreground = Brushes.LightGray,
            Background = new SolidColorBrush(Color.FromRgb(180, 50, 50)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            Padding = new Thickness(8, 3, 8, 3),
            HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 8)
        };
        clearBtn.Click += (_, _) =>
        {
            var result = MessageBox.Show("确定要清空所有统计记录吗？此操作不可撤销。",
                "确认清空", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _history?.Clear();
                _onStatsChanged?.Invoke();
                RefreshCurrentTab();
            }
        };
        stack.Children.Add(clearBtn);

        if (_history == null)
        {
            stack.Children.Add(new TextBlock { Text = "暂无数据", Foreground = Brushes.Gray, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
        }
        else
        {
            // Header
            var colWidths = new[] { 80, 50, 45, 65, 65, 65 };
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 2) };
            var headerLabels = new[] { "日期", "平台", "调用", "输入", "输出", "合计" };
            for (int i = 0; i < headerLabels.Length; i++)
                header.Children.Add(new TextBlock { Text = headerLabels[i], Foreground = Brushes.Gray, FontSize = 10, Width = colWidths[i] });
            stack.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)), BorderThickness = new Thickness(0, 0, 0, 1), Child = header });

            // Rows
            var records = _history.GetDailyRecords().Take(50);
            foreach (var r in records)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                AddCell(row, r.Date, colWidths[0], Brushes.White);
                AddCell(row, r.Platform, colWidths[1], Brushes.LightGray);
                AddCell(row, r.Calls.ToString(), colWidths[2], Brushes.LightGray);
                AddCell(row, FormatNum(r.InputTokens), colWidths[3], Brushes.LightGray);
                AddCell(row, FormatNum(r.OutputTokens), colWidths[4], Brushes.LightGray);
                AddCell(row, FormatNum(r.Total), colWidths[5], Brushes.Orange);
                stack.Children.Add(row);
            }
        }

        TabContent.Children.Add(stack);
    }

    private void BuildAboutTab()
    {
        var stack = new StackPanel();
        stack.Children.Add(SectionHeader("关于"));
        stack.Children.Add(new TextBlock { Text = "TokenPet V1.0.1", Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4) });
        stack.Children.Add(new TextBlock { Text = "基于 .NET 8.0 + WPF · 开源 MIT", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new TextBlock { Text = "https://github.com/sugar301/TokenPet", Foreground = new SolidColorBrush(Color.FromRgb(100, 160, 220)), FontSize = 11, Margin = new Thickness(0, 0, 0, 2), Cursor = System.Windows.Input.Cursors.Hand });
        stack.Children.Add(new TextBlock { Text = "一个可爱的桌面宠物，支持动画、拖拽、AI Token 代理统计和多形象切换。", Foreground = Brushes.LightGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });

        stack.Children.Add(SectionHeader("精灵图 规格说明"));
        stack.Children.Add(new TextBlock { Text = "精灵表格式: 1536×1872 像素\n9行(动画) × 8列(帧)\n每帧: 192×208 像素", Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(new TextBlock { Text = "行号对应动画:\n  0-待机(6帧) 1-向右跑(8帧) 2-向左跑(8帧)\n  3-挥手(4帧) 4-跳跃(5帧) 5-失败(8帧)\n  6-等待(6帧) 7-奔跑(6帧) 8-审视(6帧)", Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(0, 0, 0, 8) });

        stack.Children.Add(SectionHeader("形象描述 JSON"));
        var jsonText = @"{
  ""id"": ""my_pet"",
  ""displayName"": ""我的宠物"",
  ""description"": ""描述文字"",
  ""spritesheetPath"": ""spritesheet.webp""
}";
        stack.Children.Add(new TextBlock { Text = jsonText, Foreground = Brushes.LightGray, FontSize = 10, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Background = new SolidColorBrush(Color.FromRgb(35, 35, 40)), Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 4) });
        stack.Children.Add(new TextBlock { Text = "ZIP 包内需包含 pet.json + spritesheet.webp/png", Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(0, 0, 0, 8) });

        stack.Children.Add(SectionHeader("动作触发说明"));
        var triggerText = @"待机 — 启动默认，状态恢复
向右跑 — 拖拽时鼠标右移
向左跑 — 拖拽时鼠标左移
审视 — 待机 18 秒无操作自动触发 (5秒后回待机)
等待 — 待机 45 秒无操作自动触发 (10秒后回待机)
挥手 / 审视 — 收到代理请求时随机触发 (5秒)
跳跃 / 奔跑 — 收到成功响应时随机触发 (5秒)
失败 — 收到错误响应时触发 (5秒)
双击 — 切换统计面板
右键托盘 — 打开设置 / 退出";
        stack.Children.Add(new TextBlock { Text = triggerText, Foreground = Brushes.Gray, FontSize = 10, FontFamily = new System.Windows.Media.FontFamily("Consolas"), LineHeight = 18 });

        TabContent.Children.Add(stack);
    }

    private Border? MakeThumbnail(PetInfo pet)
    {
        var spritePath = Path.Combine(pet.Directory, pet.SpritesheetPath);
        var source = SpriteLoader.LoadSpritesheet(spritePath);
        if (source == null) return null;

        int fw = AnimationDefs.FrameWidth, fh = AnimationDefs.FrameHeight;
        if (source.PixelWidth < fw || source.PixelHeight < fh) return null;

        var cropped = new CroppedBitmap(source, new Int32Rect(0, 0, fw, fh));
        return new Border
        {
            Width = 36, Height = 39, Background = Brushes.Transparent,
            Child = new System.Windows.Controls.Image
            {
                Source = cropped, Width = 32, Height = 35, Stretch = Stretch.Uniform
            }
        };
    }

    private void ImportPet()
    {
        var dlg = new OpenFileDialog { Filter = "ZIP 文件|*.zip", Title = "导入宠物包" };
        if (dlg.ShowDialog() == true)
        {
            var error = _manager?.ImportPet(dlg.FileName);
            if (error != null) MessageBox.Show(error, "导入失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            else RefreshCurrentTab();
        }
    }

    private void ExportPet()
    {
        if (_manager == null || string.IsNullOrEmpty(_manager.ActivePetId)) return;
        var dlg = new SaveFileDialog { Filter = "ZIP 文件|*.zip", Title = "导出宠物包", FileName = $"{_manager.ActivePetId}.zip" };
        if (dlg.ShowDialog() == true)
        {
            var error = _manager.ExportPet(_manager.ActivePetId, dlg.FileName);
            if (error != null) MessageBox.Show(error, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenPetDir()
    {
        var dir = _manager?.PetsDir ?? "";
        if (Directory.Exists(dir)) Process.Start("explorer.exe", dir);
        else if (_manager != null) { Directory.CreateDirectory(dir); Process.Start("explorer.exe", dir); }
    }

    private static Button MakeButton(string text, Action action)
    {
        var btn = new Button
        {
            Content = text, FontSize = 11, Foreground = Brushes.LightGray,
            Background = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 4, 0)
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    private static TextBlock SectionHeader(string text) => new()
    {
        Text = text, Foreground = Brushes.Orange, FontSize = 12, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 4)
    };

    private static void AddCell(StackPanel row, string text, double width, Brush color)
    {
        row.Children.Add(new TextBlock { Text = text, Foreground = color, FontSize = 10, Width = width });
    }

    private static string FormatNum(long n)
    {
        if (n >= 1_000_000_000) return $"{n / 1_000_000_000.0:F1}B";
        if (n >= 1_000_000) return $"{n / 1_000_000.0:F1}M";
        if (n >= 1_000) return $"{n / 1_000.0:F1}K";
        return n.ToString();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Hide();
    private void PetTabBtn_Click(object sender, RoutedEventArgs e) => ShowTab("pet");
    private void ModelTabBtn_Click(object sender, RoutedEventArgs e) => ShowTab("model");
    private void StatsTabBtn_Click(object sender, RoutedEventArgs e) => ShowTab("stats");
    private void AboutTabBtn_Click(object sender, RoutedEventArgs e) => ShowTab("about");
}
