using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MenuItem = System.Windows.Controls.MenuItem;

namespace BPet;

public partial class MainWindow : Window
{
    private enum Act { Idle, Walk, Sit, Sleep, Happy, Climb, Fall, Crawl, Play }

    private readonly AppServices _services;
    private readonly DispatcherTimer _speechTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly DispatcherTimer _animationTimer = new(DispatcherPriority.Render)
    {
        Interval = TimeSpan.FromSeconds(1.0 / 60)
    };
    private readonly DispatcherTimer _topmostTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(650)
    };
    private readonly System.Diagnostics.Stopwatch _animationClock = new();
    private TimeSpan _lastFrameTime;
    private double _frameDelta;
    private readonly Dictionary<string, BitmapSource[]> _clips = new();
    private readonly Random _random = new();
    private bool _dragging;
    private bool _pressed;
    private bool _dragged;
    private bool _exitRequested;
    private System.Windows.Point _grab;
    private Act _act = Act.Idle;
    private DateTime _actUntil = DateTime.Now;
    private int _dir = -1;
    private string _clip = "idle";
    private double _vx;
    private double _vy;
    private double _phase;
    private double _clock;
    private bool IsChibi => _services.Settings.General.CharacterId == "bpet-my";
    private double DesignWidth => IsChibi ? 280 : 250;
    private double DesignHeight => IsChibi ? 366 : 390;
    private double DesignImage => IsChibi ? 360 : 384;
    private ChatDock? _dock;
    private DateTime _nextFx = DateTime.Now.AddSeconds(8);
    private DateTime _nextMoodEffect = DateTime.Now.AddSeconds(5);
    private DateTime _nextTripCheck = DateTime.Now.AddSeconds(10);
    private DateTime _cryingUntil = DateTime.MinValue;
    private int _moodToken;
    private int _heldFrame = -1;
    private double _climbTop;
    private bool _fallWillCry;
    private BubbleWindow? _bubble;
    private const int GwlExStyle = -20, WsExTransparent = 0x20;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010, SwpShowWindow = 0x0040;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        _services = services;
        SourceInitialized += (_, _) =>
        {
            // Use a stable software surface for this small per-pixel transparent window.
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
        };
        _animationTimer.Tick += (_, _) => Animate();
        _topmostTimer.Tick += (_, _) => KeepAboveApps();
        Loaded += (_, _) =>
        {
            LoadClips();
            RestoreVisiblePosition();
            ApplyOptions();
            ApplyCharacter();
            Show();
            KeepAboveApps();
            Begin(Act.Idle, 2);
        };
        LocationChanged += (_, _) => { _services.Settings.General.Left = Left; _services.Settings.General.Top = Top; PlaceBubble(); };
        _speechTimer.Tick += (_, _) => { if (_bubble is not null) _bubble.Hide(); SpeechBubble.Visibility = Visibility.Collapsed; _speechTimer.Stop(); };
        IsVisibleChanged += (_, _) =>
        {
            _animationTimer.Stop();
            _animationClock.Reset();
            _lastFrameTime = TimeSpan.Zero;
            if (IsVisible)
            {
                _animationClock.Start();
                _animationTimer.Start();
                if (_services.Settings.General.AlwaysOnTop) _topmostTimer.Start();
                KeepAboveApps();
            }
        };
        LostMouseCapture += (_, _) => FinishDrag();
    }

    private void LoadClips()
    {
        _clips.Clear();
        if (IsChibi)
        {
            foreach (var name in ChibiAnimation.Names) _clips[name] = ChibiAnimation.Load(name);
            _clips["sit"] = _clips["idle"];
            _clips["happy"] = _clips["idle"];
            _clips["raised"] = _clips["play"];
        }
        else
        {
            _clips["idle"] = new[] { LoadFrame("idle") };
            _clips["walk"] = new[] { LoadFrame("walk1"), LoadFrame("walk2") };
            _clips["sit"] = new[] { LoadFrame("sit") };
            _clips["sleep"] = new[] { LoadFrame("sleep") };
            _clips["happy"] = new[] { LoadFrame("happy") };
            _clips["raised"] = new[] { LoadFrame("raised") };
            _clips["climb"] = new[] { LoadFrame("climb") };
            _clips["play"] = new[] { LoadFrame("happy") };
            _clips["crawl"] = _clips["walk"];
        }
        var clip = string.IsNullOrEmpty(_clip) ? "idle" : _clip;
        _clip = "";
        SetClip(clip);
    }

    private BitmapImage LoadFrame(string name)
    {
        var folder = _services.Settings.General.CharacterId is "bpet-my" or "bpet-my-classic" ? "Assets/my" : "Assets";
        foreach (var path in new[] { $"{folder}/{name}.png", $"Assets/{name}.png" })
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri($"pack://application:,,,/BPet;component/{path}", UriKind.Absolute);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch { /* Fall back to the default sprite if a frame is missing. */ }
        }
        throw new IOException($"Thiếu ảnh {name}.");
    }

    public void ApplyOptions()
    {
        Topmost = _services.Settings.General.AlwaysOnTop;
        if (_bubble is not null) _bubble.Topmost = Topmost;
        if (Topmost) _topmostTimer.Start();
        else _topmostTimer.Stop();
        ApplySize();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, _services.Settings.General.ClickThrough ? style | WsExTransparent : style & ~WsExTransparent);
        KeepAboveApps();
    }

    private void KeepAboveApps()
    {
        if (!_services.Settings.General.AlwaysOnTop || !IsVisible) return;
        PinTopmost(this);
        if (_bubble is { IsVisible: true } bubble) PinTopmost(bubble);
    }

    private static void PinTopmost(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        window.Topmost = true;
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    public void SetSize(int percent)
    {
        percent = Math.Clamp(percent, 35, 140);
        _services.Settings.General.SizePercent = percent;
        ApplySize();
        _services.Save();
        ShowSpeech($"Cỡ {percent}%.");
    }

    private void ApplySize()
    {
        var percent = _services.Settings.General.SizePercent;
        if (percent is < 35 or > 140) percent = 48;
        var scale = percent / 100.0;
        Width = DesignWidth * scale;
        Height = DesignHeight * scale;
        PetImage.Width = DesignWidth * scale;
        PetImage.Height = DesignImage * scale;
        PetGhost.Width = DesignWidth * scale;
        PetGhost.Height = DesignImage * scale;
        GroundShadow.Width = 92 * scale;
        GroundShadow.Height = Math.Max(6, 14 * scale);
        SpeechBubble.MaxWidth = Math.Max(120, 230 * scale);
        if (!IsLoaded || _dragging) return;
        var area = WorkAreaDip();
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        Top = area.Bottom - Height;
    }

    private void RestoreVisiblePosition()
    {
        var area = SystemParameters.WorkArea;
        var width = double.IsFinite(Width) && Width > 1 ? Width : DesignWidth * 0.48;
        var height = double.IsFinite(Height) && Height > 1 ? Height : DesignHeight * 0.48;
        var desiredLeft = _services.Settings.General.Left;
        var left = double.IsFinite(desiredLeft) ? desiredLeft : area.Right - width - 28;
        var rightLimit = Math.Max(area.Left, area.Right - width);
        Left = Math.Clamp(left, area.Left, rightLimit);
        Top = area.Bottom - height;
        _services.Settings.General.Left = Left;
        _services.Settings.General.Top = Top;
    }

    public void ApplyCharacter()
    {
        var selected = _services.Characters.Find(_services.Settings.General.CharacterId) ?? _services.Characters.All.FirstOrDefault();
        if (selected is null) return;
        _services.Settings.General.CharacterId = selected.Id;
        if (IsLoaded)
        {
            LoadClips();
            ApplySize();
            _phase = 0;
            PetFlip.ScaleX = _dir > 0 ? (IsChibi ? 1 : -1) : (IsChibi ? -1 : 1);
        }
    }

    public void ShowSpeech(string text, PetState state = PetState.Talking, int seconds = 7)
    {
        _bubble ??= new BubbleWindow { Owner = this, Topmost = Topmost };
        if (!_bubble.IsVisible) _bubble.Show();
        _bubble.SetText(text);
        PlaceBubble();
        _speechTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 4, 20));
        _speechTimer.Stop();
        _speechTimer.Start();
        SpeechBubble.Visibility = Visibility.Collapsed;
        if (state == PetState.Sleep) Begin(Act.Sleep, 8);
        else if (state == PetState.Happy) Begin(Act.Happy, 2.4);
    }

    private void PlaceBubble()
    {
        if (_bubble is null || !_bubble.IsVisible) return;
        _bubble.Left = Left + (Width - _bubble.Width) / 2;
        var top = Top - _bubble.Height - 6;
        var area = SystemParameters.WorkArea;
        if (top < area.Top) top = Top + Height + 6;
        _bubble.Top = top;
    }

    public void RequestExit() => _exitRequested = true;

    private void Animate()
    {
        // Update before rendering, never move a layered HWND inside Rendering.
        if (!IsLoaded || !IsVisible) return;
        var now = _animationClock.Elapsed;
        var elapsed = (now - _lastFrameTime).TotalSeconds;
        _lastFrameTime = now;
        if (elapsed <= 0) return;
        Move(Math.Min(elapsed, 0.05));
    }

    private void Move(double dt)
    {
        _frameDelta = dt;
        _clock += dt;
        var scale = Math.Max(0.35, Height / DesignHeight);
        if (_dragging)
        {
            PoseDrag(scale);
            return;
        }
        if (_pressed) return; // Keep the grab point still until the drag threshold is crossed.
        var area = WorkAreaDip();
        var floor = area.Bottom - Height;
        switch (_act)
        {
            case Act.Walk:
                _vx = Approach(_vx, DateTime.Now >= _actUntil.AddSeconds(-0.65) ? 0 : _dir * 150 * scale, dt, 260 * scale);
                Left += _vx * dt;
                _phase += dt * Math.Abs(_vx) / (150 * scale);
                Top = floor;
                Face(_vx);
                PoseWalk();
                if (Left <= area.Left + 1) StartClimb(1, area);
                else if (Left >= area.Right - Width - 1) StartClimb(-1, area);
                else if (DateTime.Now >= _actUntil && Math.Abs(_vx) < 1) Begin(Act.Idle, 1.6);
                break;
            case Act.Crawl:
                _vx = Approach(_vx, DateTime.Now >= _actUntil.AddSeconds(-0.45) ? 0 : _dir * 72 * scale, dt, 180 * scale);
                Left += _vx * dt;
                _phase += dt * Math.Abs(_vx) / (72 * scale);
                Top = floor;
                Face(_vx);
                PoseCrawl();
                if (Left <= area.Left + 1) StartClimb(1, area);
                else if (Left >= area.Right - Width - 1) StartClimb(-1, area);
                else if (DateTime.Now >= _actUntil && Math.Abs(_vx) < 1) Begin(Act.Play, 2.4);
                break;
            case Act.Play:
                _vx = Approach(_vx, 0, dt, 420 * scale);
                if (Math.Abs(_vx) > 1) Left += _vx * dt;
                Top = floor;
                _phase += dt;
                PosePlay();
                if (DateTime.Now >= _actUntil) Begin(Act.Idle, 1.8);
                break;
            case Act.Climb:
                Top -= 78 * scale * dt;
                _phase += dt;
                PoseClimb();
                if (Top <= _climbTop || DateTime.Now >= _actUntil)
                {
                    _dir = Left <= area.Left + Width ? 1 : -1;
                    _vx = _dir * 40 * scale;
                    Begin(Act.Fall, 2);
                }
                break;
            case Act.Fall:
                _vy = Math.Min(_vy + 1700 * scale * dt, 900 * scale);
                Top = Math.Min(Top + _vy * dt, floor);
                Left += _vx * dt;
                Pose(1, 1, _dir * -3, 0);
                if (Top >= floor - 0.5)
                {
                    _vy = 0;
                    _vx = 0;
                    if (_fallWillCry)
                    {
                        _fallWillCry = false;
                        _cryingUntil = DateTime.Now.AddSeconds(3.8);
                        ShowMood("Ui da... huhu...", 4);
                        AddFloatingText("huhu...", System.Windows.Media.Color.FromRgb(86, 166, 255), scale, -10);
                        Begin(Act.Play, 3.8);
                    }
                    else
                    {
                        _act = Act.Idle;
                        Begin(Act.Idle, 2.2);
                    }
                }
                break;
            default:
                _phase += dt;
                _vx = Approach(_vx, 0, dt, 340 * scale);
                if (Math.Abs(_vx) > 1) Left += _vx * dt;
                Top = floor;
                PoseRest(scale);
                if (DateTime.Now >= _actUntil && Math.Abs(_vx) < 10) Decide();
                break;
        }
        MaybeTrip(scale);
        AmbientEffects(scale);
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        GroundShadow.Width = (92 * scale) * PetSquash.ScaleX;
    }

    private void MaybeTrip(double scale)
    {
        if (_act is not (Act.Walk or Act.Crawl) || Math.Abs(_vx) < 35 || DateTime.Now < _nextTripCheck) return;
        _nextTripCheck = DateTime.Now.AddSeconds(_random.Next(9, 18));
        if (_random.Next(100) >= 18) return;
        TripAndCry(scale);
    }

    private void TripAndCry(double scale)
    {
        _fallWillCry = true;
        _vy = 0;
        _vx = _dir * 70 * scale;
        ShowMood("Ối, vấp rồi!", 3);
        AddFloatingText("vấp!", System.Windows.Media.Color.FromRgb(255, 130, 130), scale, 0);
        Begin(Act.Fall, 1.4);
    }

    private void AmbientEffects(double scale)
    {
        if (DateTime.Now < _nextMoodEffect) return;
        if (_act == Act.Walk && Math.Abs(_vx) > 45)
        {
            var hums = new[] { "la la la...", "ngân nga...", "hừm hừm..." };
            var text = hums[_random.Next(hums.Length)];
            ShowMood(text, 4);
            AddFloatingText(text, System.Windows.Media.Color.FromRgb(255, 174, 74), scale, 0);
            _nextMoodEffect = DateTime.Now.AddSeconds(_random.Next(6, 12));
        }
        else if (_act == Act.Sleep)
        {
            ShowMood("Khò... khò...", 5);
            AddFloatingText("zZz", System.Windows.Media.Color.FromRgb(126, 154, 255), scale, 8);
            _nextMoodEffect = DateTime.Now.AddSeconds(_random.Next(4, 8));
        }
        else if (_act == Act.Play && DateTime.Now < _cryingUntil)
        {
            var cries = new[] { "huhu...", "đau quá...", "oa oa..." };
            var text = cries[_random.Next(cries.Length)];
            ShowMood(text, 3);
            AddFloatingText(text, System.Windows.Media.Color.FromRgb(86, 166, 255), scale, -8);
            _nextMoodEffect = DateTime.Now.AddSeconds(1.4);
        }
        else
        {
            _nextMoodEffect = DateTime.Now.AddSeconds(2);
        }
    }

    private void ShowMood(string text, int seconds)
    {
        var token = ++_moodToken;
        SpeechText.Text = text;
        SpeechBubble.BeginAnimation(OpacityProperty, null);
        SpeechBubble.Opacity = 1;
        SpeechBubble.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280))
        {
            BeginTime = TimeSpan.FromSeconds(Math.Clamp(seconds, 2, 8)),
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) =>
        {
            if (token != _moodToken) return;
            SpeechBubble.Visibility = Visibility.Collapsed;
            SpeechBubble.Opacity = 1;
        };
        SpeechBubble.BeginAnimation(OpacityProperty, fade);
        KeepAboveApps();
    }

    private void FlailAndCry(double seconds)
    {
        _cryingUntil = DateTime.Now.AddSeconds(seconds);
        ShowMood("huhu...", (int)Math.Ceiling(seconds));
        AddFloatingText("huhu...", System.Windows.Media.Color.FromRgb(86, 166, 255), Math.Max(0.35, Height / DesignHeight), -8);
        _nextMoodEffect = DateTime.Now.AddSeconds(1.2);
        Begin(Act.Play, seconds);
    }

    private static double Approach(double value, double target, double dt, double accel)
    {
        var delta = target - value;
        var step = accel * dt;
        if (Math.Abs(delta) <= step) return target;
        return value + Math.Sign(delta) * step;
    }

    private void Face(double velocity)
    {
        if (Math.Abs(velocity) < 8) return;
        PetFlip.ScaleX = velocity > 0 ? (IsChibi ? 1 : -1) : (IsChibi ? -1 : 1);
    }

    private void PoseWalk() => Hold("walk", 6, 1, 1, 0);
    private void PoseCrawl() => Hold("crawl", 4, IsChibi ? 1 : 1.03, IsChibi ? 1 : 0.92, 0);
    private void PosePlay()
    {
        if (IsChibi) { Hold("play", 1, 1, 1, 0); return; }
        var sway = Math.Sin(_phase * Math.PI * 2);
        Hold("play", 1, 1, 1 + sway * 0.015, sway * 3);
    }

    private void Hold(string clip, double fps, double scaleX, double scaleY, double tilt)
    {
        if (_clips.TryGetValue(clip, out var frames) && frames.Length > 0)
        {
            var index = IsChibi ? ChibiAnimation.FrameIndex(clip, _phase) : (int)Math.Floor(_phase * fps) % frames.Length;
            if (_clip != clip || index != _heldFrame)
            {
                _clip = clip;
                _heldFrame = index;
                PetImage.Source = frames[index];
            }
        }
        // Frame changes must not kick the sprite upward or leave offset duplicates.
        Pose(scaleX, scaleY, tilt, 0);
    }

    private void Pose(double scaleX, double scaleY, double tilt, double bob)
    {
        // Exponential smoothing is independent of monitor refresh rate.
        var blend = 1 - Math.Exp(-14 * _frameDelta);
        PetSquash.ScaleX += (scaleX - PetSquash.ScaleX) * blend;
        PetSquash.ScaleY += (scaleY - PetSquash.ScaleY) * blend;
        PetTilt.Angle += (tilt - PetTilt.Angle) * blend;
        PetBob.X = 0;
        PetBob.Y += (bob - PetBob.Y) * blend;
        PetGhost.Opacity = 0;
    }

    private void PoseRest(double scale)
    {
        if (IsChibi)
            Hold(_act == Act.Sleep ? "sleep" : "idle", 1, 1, 1, 0);
        else Pose(1, 1, 0, Math.Sin(_clock * 1.4) * -0.6 * scale);
    }

    private void PoseClimb()
    {
        if (IsChibi) { Hold("climb", 1, 1, 1, 0); return; }
        var sway = Math.Sin(_phase * Math.PI * 2 * 1.2);
        Pose(1, 1, _dir * sway * 3, sway * 0.8);
    }

    private void PoseDrag(double scale)
    {
        if (IsChibi)
        {
            _phase += _frameDelta;
            Hold("raised", 1, 1, 1, Math.Sin(_clock * 3) * 1.5);
        }
        else Pose(1, 1, Math.Sin(_clock * 3) * 1.5, -0.5 * scale);
    }

    private void Decide()
    {
        if (!_services.Settings.PetBehavior.AutoMovement)
        {
            Begin(Act.Idle, 4);
            return;
        }
        var style = _services.Settings.General.StyleId;
        if (style is "kiem-hiep" or "giang-ho" && DateTime.Now >= _nextFx && _random.Next(100) < 55)
        {
            PlayFlourish();
            _nextFx = DateTime.Now.AddSeconds(_random.Next(8, 14));
        }
        var roll = _random.Next(100);
        if (roll < 28)
        {
            _dir = _random.Next(2) == 0 ? -1 : 1;
            Begin(Act.Walk, _random.Next(3, 7));
        }
        else if (roll < 50)
        {
            _dir = _random.Next(2) == 0 ? -1 : 1;
            Begin(Act.Crawl, _random.Next(3, 6));
        }
        else if (roll < 66) Begin(Act.Play, _random.Next(3, 6));
        else if (roll < 82) Begin(IsChibi ? Act.Sleep : Act.Sit, _random.Next(8, 15));
        else if (roll < 90)
        {
            var area = WorkAreaDip();
            Left = _random.Next(2) == 0 ? area.Left : area.Right - Width;
            StartClimb(Left <= area.Left + 1 ? 1 : -1, area);
        }
        else Begin(Act.Idle, _random.Next(2, 5));
    }

    private void StartClimb(int leaveDir, Rect area)
    {
        _dir = leaveDir;
        _vx = 0;
        Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
        PetFlip.ScaleX = leaveDir > 0 ? -1 : 1;
        _climbTop = Math.Max(area.Top + 24, Top - _random.Next(110, 200));
        Begin(Act.Climb, 7);
    }

    private void Begin(Act act, double seconds)
    {
        // Reactions while airborne must not snap the window back to the floor.
        if ((_act is Act.Climb or Act.Fall) && act != Act.Fall) return;
        var sameAction = _act == act;
        _act = act;
        _actUntil = DateTime.Now.AddSeconds(seconds);
        if (act == Act.Fall) _vy = 0;
        if (act is Act.Walk or Act.Crawl) PetFlip.ScaleX = _dir > 0 ? (IsChibi ? 1 : -1) : (IsChibi ? -1 : 1);
        if (!sameAction) _phase = 0;
        _heldFrame = -1;
        SetClip(act switch
        {
            Act.Walk => "walk",
            Act.Crawl => "crawl",
            Act.Play => "play",
            Act.Sit => "sit",
            Act.Sleep => "sleep",
            Act.Happy => "happy",
            Act.Climb => "climb",
            Act.Fall => "raised",
            _ => "idle"
        });
    }

    private void SetClip(string clip)
    {
        if (_clip == clip && PetImage.Source is not null) return;
        _clip = clip;
        _heldFrame = -1;
        if (_clips.TryGetValue(clip, out var frames) && frames.Length > 0) PetImage.Source = frames[0];
    }

    private Rect WorkAreaDip()
    {
        // Screen coordinates are physical pixels; Left/Top are WPF DIPs.
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return SystemParameters.WorkArea;
        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var topLeft = fromDevice.Transform(new System.Windows.Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    public void OpenChat()
    {
        if (_dock is null)
        {
            _dock = new ChatDock(_services);
            _dock.Closed += (_, _) => _dock = null;
            _dock.Show();
        }
        else
        {
            if (!_dock.IsVisible) _dock.Show();
            _dock.Place();
            _dock.Activate();
        }
    }

    public void RunDailyTask(DailyTask task)
    {
        var persona = _services.Settings.Personality;
        var user = string.IsNullOrWhiteSpace(persona.UserPronoun) ? "bạn" : persona.UserPronoun.Trim();
        var ask = task.Kind switch
        {
            "reply" => $"Đến giờ rep tin nhắn. Soạn giúp {user} 2 hoặc 3 câu trả lời tự nhiên, giữ đúng cách xưng hô. Ghi chú: {task.Note}",
            "report" => $"Đến giờ điền báo cáo. Soạn bản nháp ngắn để {user} chép vào báo cáo, không tự gửi đi. Ghi chú: {task.Note}",
            _ => $"Đến giờ việc «{task.Name}». Nhắc {user} và soạn giúp phần viết được. Ghi chú: {task.Note}"
        };
        OpenChat();
        _dock?.Ask(ask);
    }

    public void PlayFlourish()
    {
        if (_services.Settings.General.StyleId == "giang-ho") PalmBlast();
        else Slash();
    }

    private void Slash()
    {
        var w = Math.Max(48, ActualWidth);
        var h = Math.Max(48, ActualHeight);
        AddStroke($"M {w * 0.1},{h * 0.68} C {w * 0.4},{h * 0.16} {w * 0.62},{h * 0.2} {w * 0.92},{h * 0.6}", System.Windows.Media.Color.FromRgb(244, 250, 255), System.Windows.Media.Color.FromRgb(120, 196, 255));
        AddStroke($"M {w * 0.16},{h * 0.34} C {w * 0.48},{h * 0.78} {w * 0.7},{h * 0.7} {w * 0.9},{h * 0.3}", System.Windows.Media.Color.FromRgb(255, 255, 255), System.Windows.Media.Color.FromRgb(186, 214, 255));
    }

    private void AddStroke(string data, System.Windows.Media.Color stroke, System.Windows.Media.Color glow)
    {
        var path = new System.Windows.Shapes.Path
        {
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 3.4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse(data),
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = glow, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.95 }
        };
        Fx.Children.Add(path);
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(560)) { BeginTime = TimeSpan.FromMilliseconds(90) };
        fade.Completed += (_, _) => Fx.Children.Remove(path);
        path.BeginAnimation(OpacityProperty, fade);
    }

    private void AddFloatingText(string text, System.Windows.Media.Color color, double scale, double xOffset)
    {
        var label = new System.Windows.Controls.TextBlock
        {
            Text = text,
            FontSize = Math.Clamp(18 * scale, 12, 28),
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(color),
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.White,
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.9
            }
        };
        var move = new TranslateTransform();
        label.RenderTransform = move;
        label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = Math.Max(48, ActualWidth > 0 ? ActualWidth : Width);
        var height = Math.Max(48, ActualHeight > 0 ? ActualHeight : Height);
        var x = (width - label.DesiredSize.Width) / 2 + xOffset + _random.Next(-10, 11);
        var y = Math.Max(8, height * 0.42 + _random.Next(-8, 9));
        System.Windows.Controls.Canvas.SetLeft(label, x);
        System.Windows.Controls.Canvas.SetTop(label, y);
        Fx.Children.Add(label);
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1300))
        {
            BeginTime = TimeSpan.FromMilliseconds(300)
        };
        fade.Completed += (_, _) => Fx.Children.Remove(label);
        label.BeginAnimation(OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -34 * scale, TimeSpan.FromMilliseconds(1600)));
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, _dir * 10 * scale, TimeSpan.FromMilliseconds(1600)));
    }

    private void PalmBlast()
    {
        var size = Math.Max(ActualWidth * 0.34, 28);
        var ring = new Ellipse
        {
            Width = size,
            Height = size,
            Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 186, 46)),
            StrokeThickness = 3,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 120, 30)),
            IsHitTestVisible = false,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };
        System.Windows.Controls.Canvas.SetLeft(ring, (ActualWidth - size) / 2);
        System.Windows.Controls.Canvas.SetTop(ring, ActualHeight * 0.38);
        Fx.Children.Add(ring);
        var fade = new DoubleAnimation(0.95, 0, TimeSpan.FromMilliseconds(520));
        fade.Completed += (_, _) => Fx.Children.Remove(ring);
        ring.BeginAnimation(OpacityProperty, fade);
        ring.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 2.6, TimeSpan.FromMilliseconds(520)));
        ring.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 2.6, TimeSpan.FromMilliseconds(520)));
    }

    private void Pet_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _dragged = false;
        _grab = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    private void Pet_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
        if (!_dragged)
        {
            var here = e.GetPosition(this);
            if (Math.Abs(here.X - _grab.X) + Math.Abs(here.Y - _grab.Y) < 10) return;
            _dragged = true;
            _dragging = true;
            _vx = 0;
            _vy = 0;
            SetClip("raised");
        }
        var cursor = CursorDip();
        Left = cursor.X - _grab.X;
        Top = cursor.Y - _grab.Y;
    }

    private void FinishDrag()
    {
        var wasDragged = _dragged;
        _pressed = false;
        _dragging = false;
        _dragged = false;
        if (wasDragged) Begin(Act.Fall, 2);
    }

    private void Pet_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        var wasDragged = _dragged;
        FinishDrag();
        ReleaseMouseCapture();
        if (!wasDragged) OpenChat();
    }

    private void Pet_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        ShowPetMenu();
        e.Handled = true;
    }

    private void ShowPetMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Chat", OpenChat));
        menu.Items.Add(Item("Ra chiêu", PlayFlourish));
        menu.Items.Add(Item("Cài đặt", () => new SettingsWindow(_services).Show()));
        var chars = new MenuItem { Header = "Nhân vật" };
        foreach (var character in _services.Characters.All.Where(x => x.IsBuiltIn))
        {
            var id = character.Id;
            chars.Items.Add(Item(character.Name, () =>
            {
                _services.Settings.General.CharacterId = id;
                _services.Settings.General.PickedMy = true;
                ApplyCharacter();
                _services.Save();
            }));
        }
        menu.Items.Add(chars);
        var motions = new MenuItem { Header = "Động tác" };
        foreach (var (label, action, seconds) in new[]
        {
            ("Đứng / chớp mắt", Act.Idle, 8.0), ("Đi bộ", Act.Walk, 8.0),
            ("Bò", Act.Crawl, 8.0), ("Nằm ngủ", Act.Sleep, 30.0)
        })
            motions.Items.Add(Item(label, () => Begin(action, seconds)));
        motions.Items.Add(Item("Giãy đành đạch", () => FlailAndCry(5)));
        motions.Items.Add(Item("Vấp ngã / khóc", () => TripAndCry(Math.Max(0.35, Height / DesignHeight))));
        motions.Items.Add(Item("Leo cạnh màn hình", () =>
        {
            var area = WorkAreaDip();
            _act = Act.Idle;
            Left = _dir > 0 ? area.Right - Width : area.Left;
            StartClimb(_dir > 0 ? -1 : 1, area);
        }));
        menu.Items.Add(motions);
        var size = new MenuItem { Header = $"Kích thước ({NormalizedSize()}%)" };
        foreach (var preset in new[] { 40, 55, 70, 100 }) size.Items.Add(Item(preset + "%", () => SetSize(preset)));
        size.Items.Add(Item("Nhỏ hơn", () => SetSize(_services.Settings.General.SizePercent - 8)));
        size.Items.Add(Item("To hơn", () => SetSize(_services.Settings.General.SizePercent + 8)));
        menu.Items.Add(size);
        var style = new MenuItem { Header = "Phong cách" };
        style.Items.Add(Item("Thường", () => SetStyle("thuong")));
        style.Items.Add(Item("Kiếm hiệp", () => SetStyle("kiem-hiep")));
        style.Items.Add(Item("Giang hồ", () => SetStyle("giang-ho")));
        menu.Items.Add(style);
        menu.Items.Add(Item("Kiểm tra cập nhật", () => new UpdateWindow().Show()));
        menu.PlacementTarget = PetImage;
        menu.IsOpen = true;
    }

    private void SetStyle(string style)
    {
        _services.Settings.General.StyleId = style;
        _services.Save();
        ShowSpeech(style switch { "kiem-hiep" => "Kiếm hiệp.", "giang-ho" => "Giang hồ.", _ => "Bình thường." });
        if (style != "thuong") PlayFlourish();
    }

    private System.Windows.Point CursorDip()
    {
        var point = System.Windows.Forms.Cursor.Position;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return new System.Windows.Point(point.X, point.Y);
        return source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(point.X, point.Y));
    }

    private int NormalizedSize()
    {
        var percent = _services.Settings.General.SizePercent;
        return percent is < 35 or > 140 ? 48 : percent;
    }

    private static MenuItem Item(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); return item; }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exitRequested) { e.Cancel = true; Hide(); if (_bubble is not null) _bubble.Hide(); return; }
        _bubble?.Close();
        _animationTimer.Stop();
        _topmostTimer.Stop();
        _animationClock.Stop();
        _speechTimer.Stop();
        _services.Save();
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
