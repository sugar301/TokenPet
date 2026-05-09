using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TokenPet.Services;
using TokenPet.Windows;

namespace TokenPet;

public partial class App : Application
{
    public PetManager PetManager { get; } = new();
    public AppConfig Config { get; } = new();
    private MainWindow? _mainWindow;
    private Timer? _proxyTimer;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;

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

        _trayIcon = new TrayIcon();
        _trayIcon.OnOpenSettings += ShowSettings;
        _trayIcon.OnExit += () => System.Windows.Application.Current.Shutdown();
        _trayIcon.Show("AI Pet", LoadTrayIcon());

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

    private static BitmapSource LoadTrayIcon()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("favicon.ico");
        if (stream == null) return CreateFallbackIcon();
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    private static BitmapSource CreateFallbackIcon()
    {
        const int size = 16;
        var bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = (y * size + x) * 4;
                bool inBody = (x - 8) * (x - 8) + (y - 8) * (y - 8) <= 36;
                if (inBody) { pixels[i] = 0; pixels[i + 1] = 128; pixels[i + 2] = 255; pixels[i + 3] = 255; }
            }
        bmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bmp.Freeze();
        return bmp;
    }
}

// Pure P/Invoke tray icon - no WinForms dependency
internal class TrayIcon : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAY = WM_USER + 1;
    private const uint NIM_ADD = 0;
    private const uint NIM_DELETE = 2;
    private const uint NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1;
    private const uint NIF_ICON = 2;
    private const uint NIF_TIP = 4;
    private const uint NIF_SHOWTIP = 128;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint TPM_RIGHTBUTTON = 2;
    private const uint TPM_RETURNCMD = 0x100;
    private const uint MF_STRING = 0;
    private const uint MF_SEPARATOR = 0x800;

    private uint _id = 1;
    private IntPtr _handle;
    private IntPtr _iconHandle;
    private HwndSource? _source;
    private IntPtr _menu;

    public event Action? OnOpenSettings;
    public event Action? OnExit;

    public void Show(string tip, BitmapSource iconSource)
    {
        _handle = new WindowInteropHelper(Application.Current.MainWindow).Handle;
        _source = PresentationSource.FromVisual(Application.Current.MainWindow) as HwndSource;
        if (_source != null) _source.AddHook(WndProc);

        _iconHandle = BitmapSourceToIconHandle(iconSource);

        var nid = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _handle,
            uID = _id,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
            uCallbackMessage = WM_TRAY,
            hIcon = _iconHandle,
            szTip = tip
        };
        Shell_NotifyIcon(NIM_ADD, ref nid);
        Shell_NotifyIcon(NIM_SETVERSION, ref nid);

        _menu = CreatePopupMenu();
        AppendMenu(_menu, MF_STRING, 1, "打开设置");
        AppendMenu(_menu, MF_SEPARATOR, 0, "");
        AppendMenu(_menu, MF_STRING, 2, "退出");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAY)
        {
            if (lParam == (IntPtr)WM_RBUTTONUP)
            {
                GetCursorPos(out var pt);
                SetForegroundWindow(_handle);
                var cmd = TrackPopupMenu(_menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, 0, _handle, IntPtr.Zero);
                if (cmd == 1) OnOpenSettings?.Invoke();
                else if (cmd == 2) OnExit?.Invoke();
                handled = true;
            }
            else if (lParam == (IntPtr)WM_LBUTTONDBLCLK)
            {
                OnOpenSettings?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_id != 0)
        {
            var nid = new NOTIFYICONDATA { cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _handle, uID = _id };
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _id = 0;
        }
        if (_iconHandle != IntPtr.Zero) { DestroyIcon(_iconHandle); _iconHandle = IntPtr.Zero; }
        if (_menu != IntPtr.Zero) { DestroyMenu(_menu); _menu = IntPtr.Zero; }
        if (_source != null) { _source.RemoveHook(WndProc); _source = null; }
    }

    private static IntPtr BitmapSourceToIconHandle(BitmapSource bmp)
    {
        if (bmp.Format != PixelFormats.Bgra32)
        {
            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            bmp = converted;
        }

        int w = bmp.PixelWidth, h = bmp.PixelHeight;
        int stride = w * 4;
        var pixels = new byte[stride * h];
        bmp.CopyPixels(pixels, stride, 0);

        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // negative = top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };

        var screenDc = GetDC(IntPtr.Zero);
        var colorBitmap = CreateCompatibleBitmap(screenDc, w, h);
        var maskBitmap = CreateBitmap(w, h, 1, 1, IntPtr.Zero);
        SetDIBits(screenDc, colorBitmap, 0, (uint)h, pixels, ref info, 0);

        var maskPixels = new byte[stride * h];
        for (int i = 3; i < pixels.Length; i += 4)
            maskPixels[i / 4] = 0xFF; // fully opaque
        var maskInfo = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            }
        };
        SetDIBits(screenDc, maskBitmap, 0, (uint)h, maskPixels, ref maskInfo, 0);

        var iconInfo = new ICONINFO { fIcon = true, hbmColor = colorBitmap, hbmMask = maskBitmap };
        var hIcon = CreateIconIndirect(ref iconInfo);
        DeleteObject(colorBitmap);
        DeleteObject(maskBitmap);
        ReleaseDC(IntPtr.Zero, screenDc);
        return hIcon;
    }

    [DllImport("shell32.dll")]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint cPlanes, uint cBitsPerPel, IntPtr lpvBits);
    [DllImport("gdi32.dll")]
    private static extern int SetDIBits(IntPtr hdc, IntPtr hbmp, uint start, uint cLines, byte[] lpBits, ref BITMAPINFO lpbmi, uint ColorUse);
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO { public bool fIcon; public uint xHotspot; public uint yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }
}
