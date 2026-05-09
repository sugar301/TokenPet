using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using TokenPet.Services;
using TokenPet.Windows;
using WinForms = System.Windows.Forms;

namespace TokenPet;

public partial class App : Application
{
    public PetManager PetManager { get; } = new();
    public AppConfig Config { get; } = new();
    private MainWindow? _mainWindow;
    private Timer? _proxyTimer;
    private SettingsWindow? _settingsWindow;
    private WinForms.NotifyIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        BootstrapData();

        Config.Load();
        PetManager.Setup();
        if (!string.IsNullOrEmpty(Config.ActivePetId))
            PetManager.SetActivePet(Config.ActivePetId);

        _mainWindow = new MainWindow();
        _mainWindow.Show();

        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "TokenPet",
            Visible = true
        };
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => ShowSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => System.Windows.Application.Current.Shutdown());
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        if (Config.ProxyEnabled)
            _mainWindow.Proxy.Start(Config.ProxyPort, _mainWindow.Proxy.Targets);

        _proxyTimer = new Timer(_ => _mainWindow?.Proxy.Poll(), null, 0, 100);
    }

    private static void BootstrapData()
    {
        var exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var petsDir = Path.Combine(exeDir, "pet_data", "pets");
        if (Directory.Exists(petsDir)) return;

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.StartsWith("pets/")) continue;
                var relative = name[5..];
                var dest = Path.Combine(petsDir, relative);
                var destDir = Path.GetDirectoryName(dest);
                if (destDir != null) Directory.CreateDirectory(destDir);

                using var src = asm.GetManifestResourceStream(name);
                if (src == null) continue;
                using var dst = File.Create(dest);
                src.CopyTo(dst);
            }
        }
        catch { }
    }

    public void ShowSettings()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.SetContext(_mainWindow!.PetSpriteControl, PetManager, _mainWindow.History, _mainWindow.Proxy,
                onScaleChanged: () =>
                {
                    _mainWindow.LoadPetSprite();
                    _mainWindow.SnapWindowToPet();
                },
                onStatsChanged: () => _mainWindow.RefreshStats());
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        if (_settingsWindow.IsVisible)
            _settingsWindow.Hide();
        else
        {
            _settingsWindow.Left = _mainWindow!.Left + _mainWindow.Width + 10;
            _settingsWindow.Top = _mainWindow.Top;
            _settingsWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _proxyTimer?.Dispose();
        _settingsWindow?.Close();
        Config.WindowX = _mainWindow?.Left ?? -1;
        Config.WindowY = _mainWindow?.Top ?? -1;
        Config.Save();
        _mainWindow?.Proxy.Stop();
        base.OnExit(e);
    }

    private static Icon LoadTrayIcon()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("favicon.ico");
        if (stream != null) return new Icon(stream);
        // Fallback: simple 16x16 blue icon
        var bmp = new Bitmap(16, 16);
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 16; y++)
                if ((x - 8) * (x - 8) + (y - 8) * (y - 8) <= 36)
                    bmp.SetPixel(x, y, System.Drawing.Color.FromArgb(0, 128, 255));
        return Icon.FromHandle(bmp.GetHicon());
    }
}
