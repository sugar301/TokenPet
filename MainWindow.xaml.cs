using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TokenPet.Controls;
using TokenPet.Services;
using TokenPet.Windows;

namespace TokenPet;

public enum PetState { Idle, Sleeping, Inspecting, Dragged }

public partial class MainWindow : Window
{
    private readonly App _app;
    private PetState _state = PetState.Idle;
    private double _sleepTimer;
    private double _inspectTimer;
    private DateTime _lastFrame = DateTime.Now;
    private DateTime _lastClick = DateTime.MinValue;

    // Drag
    private bool _isDragging;
    private Point _dragStartMouseScreen;
    private Point _dragStartWindowPos;
    private double _lastDragScreenX;

    private readonly DispatcherTimer _animTimer;
    private StatsWindow? _statsWindow;
    private DateTime _reactiveEnd;
    private PetState _stateBeforeReactive;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    public Controls.PetSprite PetSpriteControl => PetSprite;
    public TokenHistory History { get; }
    public ProxyServer Proxy { get; }

    public void RefreshStats() => _statsWindow?.Refresh();

    public MainWindow()
    {
        InitializeComponent();
        _app = (Application.Current as App)!;

        History = new TokenHistory();
        History.Load();
        Proxy = new ProxyServer();
        Proxy.TokenUsed += (input, output, target) =>
        {
            Dispatcher.Invoke(() =>
            {
                History.Record(target, input, output);
                _statsWindow?.Refresh();
            });
        };

        Proxy.RequestReceived += _ =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_state == PetState.Idle)
                {
                    var r = Random.Shared.Next(2);
                    _reactiveAnim(r == 0 ? "sit" : "wave", 5);
                }
            });
        };

        Proxy.ResponseFinished += (status, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_state == PetState.Idle || _state == PetState.Sleeping || _state == PetState.Inspecting)
                {
                    if (status >= 400)
                        _reactiveAnim("fail", 5);
                    else
                    {
                        var r = Random.Shared.Next(2);
                        _reactiveAnim(r == 0 ? "jump" : "sprint", 5);
                    }
                }
            });
        };

        _animTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render,
            (_, _) => OnTick(), Dispatcher);
        _animTimer.Start();

        Loaded += OnLoaded;
        Closing += OnClosing;
        MouseLeftButtonDown += OnMouseDown;
        MouseLeftButtonUp += OnMouseUp;
        MouseMove += OnMouseMove;
        KeyDown += OnKeyDown;
        PetSprite.SizeChanged += (_, _) => SnapWindowToPet();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _app.PetManager.PetChanged += OnPetChanged;

        if (!string.IsNullOrEmpty(_app.Config.ActivePetId))
            _app.PetManager.SetActivePet(_app.Config.ActivePetId);

        LoadPetSprite();
        SnapWindowToPet();
        PositionWindow();
        _transitionTo(PetState.Idle);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _app.Config.WindowX = Left;
        _app.Config.WindowY = Top;
        _app.Config.TotalCalls = History.GetTotalCalls();
        _app.Config.TotalTokens = History.GetCumulativeTokens();
        _app.Config.Save();
        Proxy.Stop();
        _statsWindow?.Close();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var now = DateTime.Now;
        if ((now - _lastClick).TotalMilliseconds < 350)
            ToggleStats();
        _lastClick = now;

        GetCursorPos(out var pt);
        _dragStartMouseScreen = new Point(pt.X, pt.Y);
        _dragStartWindowPos = new Point(Left, Top);
        _lastDragScreenX = pt.X;
        _isDragging = true;
        _transitionTo(PetState.Dragged);
        CaptureMouse();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ReleaseMouseCapture();
        _transitionTo(PetState.Idle);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        GetCursorPos(out var cur);
        var deltaX = cur.X - _dragStartMouseScreen.X;
        var deltaY = cur.Y - _dragStartMouseScreen.Y;
        Left = _dragStartWindowPos.X + deltaX;
        Top = _dragStartWindowPos.Y + deltaY;

        var dx = cur.X - _lastDragScreenX;
        if (dx > 2)
            PetSprite.Play("walk");
        else if (dx < -2)
            PetSprite.Play("run_left");
        _lastDragScreenX = cur.X;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); }
    }

    private void OnPetChanged(string petId)
    {
        _app.Config.ActivePetId = petId;
        _app.Config.Save();
        LoadPetSprite();
        _transitionTo(PetState.Idle);
    }

    private void ToggleStats()
    {
        if (_statsWindow == null)
        {
            _statsWindow = new StatsWindow();
            _statsWindow.SetHistory(History);
            _statsWindow.OpenSettings += OpenSettings;
            _statsWindow.Closed += (_, _) => _statsWindow = null;
        }

        if (_statsWindow.IsVisible)
        {
            _statsWindow.Hide();
        }
        else
        {
            _statsWindow.Left = Left + (Width - _statsWindow.Width) / 2;
            _statsWindow.Top = Top - _statsWindow.Height - 6;
            _statsWindow.Refresh();
            _statsWindow.Show();
        }
    }

    internal void OpenSettings()
    {
        _app.ShowSettings();
    }

    internal void LoadPetSprite()
    {
        var scale = _app.Config.PetScale;
        var fw = Models.AnimationDefs.FrameWidth * scale;
        var fh = Models.AnimationDefs.FrameHeight * scale;
        PetSprite.Width = fw;
        PetSprite.Height = fh;

        var spritePath = _app.PetManager.GetActiveSpritePath();
        if (spritePath != null)
            PetSprite.LoadSpriteSheet(spritePath);
        else
            PetSprite.LoadProceduralSprites();
    }

    internal void SnapWindowToPet()
    {
        Width = PetSprite.Width;
        Height = PetSprite.Height;
    }

    private void PositionWindow()
    {
        if (_app.Config.WindowX >= 0 && _app.Config.WindowY >= 0)
        {
            Left = _app.Config.WindowX;
            Top = _app.Config.WindowY;
        }
        else
        {
            var screen = SystemParameters.WorkArea;
            Left = screen.Right - Width - 60;
            Top = screen.Bottom - Height - 80;
        }
    }

    // Matches original: two independent accumulating timers
    public void OnTick()
    {
        var now = DateTime.Now;
        var delta = (now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        PetSprite.Tick();

        if (_isDragging) return;

        // Check reactive animation timeout
        if (_reactiveEnd > DateTime.MinValue && now > _reactiveEnd)
        {
            _reactiveEnd = DateTime.MinValue;
            _transitionTo(_stateBeforeReactive);
            return;
        }
        if (_reactiveEnd > DateTime.MinValue) return;

        switch (_state)
        {
            case PetState.Idle:
                _sleepTimer += delta;
                _inspectTimer += delta;
                if (_sleepTimer >= 45.0)
                    _transitionTo(PetState.Sleeping);
                else if (_inspectTimer >= 18.0)
                    _transitionTo(PetState.Inspecting);
                break;

            case PetState.Sleeping:
                _sleepTimer += delta;
                if (_sleepTimer >= 10.0)
                    _transitionTo(PetState.Idle);
                break;

            case PetState.Inspecting:
                _inspectTimer += delta;
                if (_inspectTimer >= 5.0)
                    _transitionTo(PetState.Idle);
                break;
        }
    }

    private void _reactiveAnim(string anim, int seconds)
    {
        _stateBeforeReactive = _state;
        _reactiveEnd = DateTime.Now.AddSeconds(seconds);
        PetSprite.Play(anim);
    }

    private void _transitionTo(PetState newState)
    {
        _state = newState;
        switch (newState)
        {
            case PetState.Idle:
                _sleepTimer = 0;
                _inspectTimer = 0;
                PetSprite.Play("idle");
                break;
            case PetState.Sleeping:
                _sleepTimer = 0;
                PetSprite.Play("sleep");
                break;
            case PetState.Inspecting:
                _inspectTimer = 0;
                PetSprite.Play("sit");
                break;
        }
    }
}
