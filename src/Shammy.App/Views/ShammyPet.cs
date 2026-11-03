using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Shammy.Core.Notch;
using Shammy.Interop;
using Shammy.Motion;

namespace Shammy.Views;

/// <summary>
/// Shammy the notch pet: two small eyes on the resting notch whose shape and motion show its mood (see
/// <see cref="PetMoods"/>). At rest it blinks, glances around and follows a nearby pointer; it sways with
/// happy ^ ^ eyes to music, perks up at a notification, bounces when a timer is done, looks up while
/// listening, darts its eyes while thinking, droops on a low battery and dozes late at night.
/// Every timer and animation runs only while it is on screen; floating notes, z's and hearts are spawned
/// one at a time by a timer rather than looping forever, so a sleeping pet is nearly free.
/// </summary>
internal sealed class ShammyPet : Grid
{
    const double Gap = 6;

    static readonly TimeSpan FarPoll = TimeSpan.FromMilliseconds(250), NearPoll = TimeSpan.FromMilliseconds(33);

    readonly Eye _leftEye = new(), _rightEye = new();
    readonly StackPanel _eyes = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
    readonly Canvas _overlay = new() { IsHitTestVisible = false, ClipToBounds = false };
    readonly ScaleTransform _lid = new(1, 1), _perk = new(1, 1);
    readonly RotateTransform _sway = new();
    readonly TranslateTransform _glance = new(), _follow = new(), _bounce = new(), _pose = new();
    readonly SpringValue _followX = new(0, 0.01);
    readonly DispatcherTimer _blinkTimer, _glanceTimer, _pointerTimer, _particleTimer;
    readonly Random _random = new();
    readonly Func<double> _musicLevel;
    IDisposable? _swayFrames;
    double _swayTime;
    bool _following, _subscribed, _alternate;

    public ShammyPet(Func<double> musicLevel)
    {
        _musicLevel = musicLevel;
        Width = 30;
        Height = RestView.RestHeight;
        _rightEye.Margin = new Thickness(Gap, 0, 0, 0);
        _eyes.Children.Add(_leftEye);
        _eyes.Children.Add(_rightEye);
        _eyes.RenderTransformOrigin = new Point(0.5, 0.5);
        _eyes.RenderTransform = new TransformGroup { Children = { _lid, _perk, _sway, _glance, _follow, _bounce, _pose } };
        Children.Add(_eyes);
        Children.Add(_overlay);

        _blinkTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        _blinkTimer.Tick += (_, _) => Blink();
        _glanceTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(7) };
        _glanceTimer.Tick += (_, _) => LookAround();
        _pointerTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FarPoll };
        _pointerTimer.Tick += (_, _) => TrackPointer();
        _particleTimer = new DispatcherTimer(DispatcherPriority.Background);
        _particleTimer.Tick += (_, _) => SpawnParticle();

        IsVisibleChanged += (_, _) => UpdateRunning();
        Screens.PetLook.Changed += () =>
        {
            if (IsVisible) ApplyMood();
        };
        ApplyMood();
    }

    public PetMood Mood { get; private set; } = PetMood.Idle;
    public bool IsFollowing => _following;
    public double FollowOffset => _followX.Target;

    /// <summary>The pointer came within reach (wakes a sleepy pet). Raised on each nearby poll.</summary>
    public event Action? PointerNear;

    public void SetMood(PetMood mood)
    {
        if (mood == Mood) return;
        Mood = mood;
        ApplyMood();
    }

    // ---- Running only while visible ------------------------------------------------------------

    void UpdateRunning()
    {
        if (IsVisible)
        {
            Reschedule(_blinkTimer, EyeLogic.NextBlink(_random, Mood == PetMood.Sleepy));
            Reschedule(_glanceTimer, EyeLogic.NextGlance(_random));
            _pointerTimer.Interval = FarPoll;
            _pointerTimer.Start();
            if (!_subscribed)
            {
                Animator.Frame += OnFrame;
                _subscribed = true;
            }
            ApplyMood();
        }
        else
        {
            _blinkTimer.Stop();
            _glanceTimer.Stop();
            _pointerTimer.Stop();
            _particleTimer.Stop();
            StopSway();
            _following = false;
            _followX.Snap(0);
            _follow.X = 0;
            _overlay.Children.Clear();
            if (_subscribed)
            {
                Animator.Frame -= OnFrame;
                _subscribed = false;
            }
        }
    }

    static void Reschedule(DispatcherTimer timer, TimeSpan interval)
    {
        timer.Stop();
        timer.Interval = interval;
        timer.Start();
    }

    void OnFrame(double dt) => _follow.X = _followX.Value;

    // ---- Moods -----------------------------------------------------------------------------------

    static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    void ApplyMood()
    {
        // Stop whatever the last mood was doing.
        _bounce.BeginAnimation(TranslateTransform.YProperty, null);
        _glance.BeginAnimation(TranslateTransform.XProperty, null);
        _perk.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _perk.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _lid.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _particleTimer.Stop();
        StopSway();
        _sway.Angle = 0;
        _pose.X = _pose.Y = 0;
        _lid.ScaleY = 1;

        // The eye colour and shape chosen in Settings (PetLook); white round pills by default.
        var white = Screens.PetLook.Solid();
        switch (Mood)
        {
            case PetMood.Music:
                SetEyes(EyeShape.Arc, 9, 5, white);
                StartSway();
                StartParticles(TimeSpan.FromMilliseconds(900));
                break;
            case PetMood.Notify:
                SetEyes(EyeShape.Pill, 8, 13, white);
                _pose.X = 2;
                _pose.Y = 1.5;
                Perk();
                break;
            case PetMood.TimerDone:
                SetEyes(EyeShape.Arc, 9, 5, Res("TimerAccent"));
                Repeat(_bounce, TranslateTransform.YProperty, 1000, (0, 0), (200, -4), (400, 0), (600, -2.5), (800, 0));
                break;
            case PetMood.Listening:
                SetEyes(EyeShape.Pill, 8, 12, white);
                _pose.Y = -2;
                break;
            case PetMood.Thinking:
                SetEyes(EyeShape.Pill, 7, 9, white);
                _pose.Y = -1.5;
                Repeat(_glance, TranslateTransform.XProperty, 1200, (0, -2.5), (600, 2.5), (1200, -2.5));
                break;
            case PetMood.MeetingSoon:
                SetEyes(EyeShape.Pill, 7, 11, white);
                _pose.X = 3;
                break;
            case PetMood.Tired:
                SetEyes(EyeShape.Droopy, 7, 6, white);
                _pose.Y = 2;
                break;
            case PetMood.Sleepy:
                SetEyes(EyeShape.Lid, 8, 2.5, white);
                _pose.Y = 3;
                StartParticles(TimeSpan.FromSeconds(4));
                break;
            case PetMood.Happy:
                SetEyes(EyeShape.Arc, 9, 5, white);
                StartParticles(TimeSpan.FromMilliseconds(700));
                SpawnParticle();
                break;
            default:
                SetEyes(EyeShape.Pill, 7, 11, white);
                break;
        }
    }

    void SetEyes(EyeShape shape, double w, double h, Brush brush)
    {
        if (shape == EyeShape.Pill) (w, h, _) = Shammy.Core.Notch.PetLooks.Restyle(Screens.PetLook.Style, w, h);
        _leftEye.Show(shape, w, h, brush);
        _rightEye.Show(shape, w, h, brush);
    }

    static void Repeat(Animatable target, DependencyProperty property, double periodMs, params (double Ms, double Value)[] frames)
    {
        var a = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromMilliseconds(periodMs) };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        foreach (var (ms, value) in frames) a.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), ease));
        target.BeginAnimation(property, a);
    }

    /// <summary>Eyes go wide for a moment.</summary>
    void Perk()
    {
        var a = new DoubleAnimationUsingKeyFrames();
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1.18, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1.06, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400))));
        _perk.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        _perk.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Sways to the beat: the visualizer's level sets how much (it's already running for the music).</summary>
    void StartSway()
    {
        if (!IsVisible || _swayFrames is not null) return;
        _swayFrames = Animator.Subscribe(dt =>
        {
            _swayTime += dt;
            double phase = _swayTime * 2 * Math.PI / 0.9;
            double energy = 0.35 + 0.65 * Math.Clamp(_musicLevel(), 0, 1);
            _sway.Angle = 7 * Math.Sin(phase) * energy;
            _pose.Y = -1.5 * (1 + Math.Sin(phase * 2)) * energy;
        });
    }

    void StopSway()
    {
        _swayFrames?.Dispose();
        _swayFrames = null;
    }

    void StartParticles(TimeSpan every)
    {
        if (!IsVisible) return;
        _particleTimer.Interval = every;
        _particleTimer.Start();
    }

    /// <summary>One note, z or heart floats up and fades; it removes itself when done.</summary>
    void SpawnParticle()
    {
        if (!IsVisible) return;
        _alternate = !_alternate;
        (string text, string brush, double size, double x, double y, double dx, double dy, double ms) = Mood switch
        {
            PetMood.Music => (_alternate ? "♪" : "♫", "MusicAccent", _alternate ? 11.0 : 10.0, _alternate ? 22.0 : 16.0, 6.0, 12.0, -18.0, 1800.0),
            PetMood.Sleepy => ("z", "CardText2", _alternate ? 10.0 : 8.0, 22.0, 6.0, 9.0, -16.0, 2600.0),
            PetMood.Happy => ("♥", "", 11.0, 6.0, 4.0, 0.0, -14.0, 1400.0),
            _ => ("", "", 0, 0, 0, 0, 0, 0),
        };
        if (text.Length == 0) return;
        var particle = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeights.Bold,
            Foreground = brush.Length > 0 ? Res(brush) : new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x8A)),
            RenderTransform = new TranslateTransform(),
            Opacity = 0,
        };
        Canvas.SetLeft(particle, x);
        Canvas.SetTop(particle, y);
        _overlay.Children.Add(particle);

        var duration = TimeSpan.FromMilliseconds(ms);
        var fade = new DoubleAnimationUsingKeyFrames { Duration = duration };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.25)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        fade.Completed += (_, _) => _overlay.Children.Remove(particle);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var move = (TranslateTransform)particle.RenderTransform;
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dx, duration) { EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, dy, duration) { EasingFunction = ease });
        particle.BeginAnimation(OpacityProperty, fade);
    }

    // ---- Idle behaviour: blink, glance, follow ---------------------------------------------------

    bool Blinks => Mood is PetMood.Idle or PetMood.MeetingSoon or PetMood.Listening or PetMood.Notify or PetMood.Tired;

    void Blink()
    {
        _blinkTimer.Interval = EyeLogic.NextBlink(_random, Mood == PetMood.Sleepy);
        if (!Blinks) return;
        var blink = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        blink.KeyFrames.Add(new EasingDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
        blink.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        if (EyeLogic.DoubleBlink(_random))
        {
            blink.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(230))));
            blink.KeyFrames.Add(new EasingDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            blink.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380))));
        }
        _lid.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
    }

    /// <summary>A glance to one side and back, only while idle and not following the pointer.</summary>
    void LookAround()
    {
        _glanceTimer.Interval = EyeLogic.NextGlance(_random);
        if (_following || Mood != PetMood.Idle) return;
        double to = EyeLogic.GlanceDirection(_random);
        var look = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        look.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), ease));
        look.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1100))));
        look.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1400)), ease));
        _glance.BeginAnimation(TranslateTransform.XProperty, look);
    }

    /// <summary>
    /// Polls the cursor slowly while it's far and quickly once it's within reach, so the eyes track smoothly
    /// without a mouse hook. The poll is a single GetCursorPos call.
    /// </summary>
    void TrackPointer()
    {
        if (!IsVisible || PresentationSource.FromVisual(this) is null || !Native.GetCursorPos(out var p)) return;
        var centre = _eyes.PointToScreen(new Point(_eyes.ActualWidth / 2, _eyes.ActualHeight / 2));
        double scale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var look = EyeLogic.Follow((p.X - centre.X) / scale, (p.Y - centre.Y) / scale);
        if (look is not null) PointerNear?.Invoke();
        bool follows = Mood == PetMood.Idle;
        if (look is { } x && follows)
        {
            if (!_following) _glance.BeginAnimation(TranslateTransform.XProperty, null);
            _following = true;
            if (Math.Abs(_followX.Target - x) > 0.05) _followX.AnimateTo(x, 0.25, 0.9);
            _pointerTimer.Interval = NearPoll;
        }
        else if (_following)
        {
            _following = false;
            _followX.AnimateTo(0, 0.5, 1);
            _pointerTimer.Interval = FarPoll;
        }
        else if (look is not null && Mood == PetMood.Sleepy)
        {
            _pointerTimer.Interval = FarPoll;
        }
    }

    /// <summary>A happy little hop (a song started).</summary>
    public void Bounce()
    {
        if (!IsVisible || Mood == PetMood.TimerDone) return;
        var hop = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        var up = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var down = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(-4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), up));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240)), down));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(-2.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(340)), up));
        hop.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(440)), down));
        _bounce.BeginAnimation(TranslateTransform.YProperty, hop);
    }

    enum EyeShape { Pill, Arc, Lid, Droopy }

    /// <summary>One eye: a rounded pill (or lid / droopy pill), or a ^ arc for happy moods.</summary>
    sealed class Eye : Grid
    {
        readonly Border _pill = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly System.Windows.Shapes.Path _arc = new()
        {
            Data = Geometry.Parse("M 0,5 A 4.5,4.5 0 0 1 9,5"),
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        public Eye()
        {
            Width = 9;
            Height = 14;
            Children.Add(_pill);
            Children.Add(_arc);
        }

        public void Show(EyeShape shape, double w, double h, Brush brush)
        {
            bool arc = shape == EyeShape.Arc;
            _arc.Visibility = arc ? Visibility.Visible : Visibility.Collapsed;
            _pill.Visibility = arc ? Visibility.Collapsed : Visibility.Visible;
            _arc.Stroke = brush;
            _pill.Background = brush;
            _pill.Width = w;
            _pill.Height = h;
            _pill.CornerRadius = shape switch
            {
                EyeShape.Droopy => new CornerRadius(2, 2, w / 2, w / 2),
                EyeShape.Lid => new CornerRadius(h / 2),
                _ => new CornerRadius(Screens.PetLook.Style == Shammy.Core.Notch.PetEyeStyle.Pixel ? 1.5 : w / 2),
            };
        }
    }
}
