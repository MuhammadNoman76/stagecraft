using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Stagecraft.Core.Animation;
using Stagecraft.Core.Model;
using Stagecraft.Core.Rendering;
using Stagecraft.Services;

namespace Stagecraft.Views.Show;

/// <summary>What a player is asked to do (animations, clicks and the control bar).</summary>
internal enum MediaAction { Play, Pause, Stop, Toggle, Seek, ToggleMute }

/// <summary>
/// Playback settings of one surface: whether it makes sound (the audience window does; the presenter view's copy of
/// the slide plays muted) and the background music that keeps playing across slides.
/// </summary>
internal sealed class MediaPlayback
{
    public MediaPlayback(bool sound)
    {
        Sound = sound;
    }

    /// <summary>This surface's players make sound.</summary>
    public bool Sound { get; set; }

    /// <summary>
    /// Every player is silent: the off-screen test mode and screenshots (never play test audio out loud).
    /// </summary>
    public static bool ForceMute { get; set; } = MainWindow.IsTestOffscreen || Environment.GetEnvironmentVariable("STAGECRAFT_MUTE") == "1";

    public bool Audible => Sound && !ForceMute;

    /// <summary>Sounds playing across slides (element id → player), owned by the surface between slides.</summary>
    public Dictionary<string, CarriedSound> Background { get; } = new();

    /// <summary>Raised when any player starts, pauses or stops (the presenter view and control bars update).</summary>
    public event Action<MediaHost>? StateChanged;

    public void RaiseStateChanged(MediaHost host) => StateChanged?.Invoke(host);
}

/// <summary>A sound that keeps playing after its slide (background music) and when it should stop.</summary>
internal sealed class CarriedSound
{
    public CarriedSound(MediaPlayer player, AudioElement clip, DispatcherTimer timer)
    {
        Player = player;
        Clip = clip;
        Timer = timer;
        // The slide it started on counts as the first.
        SlidesLeft = clip.StopAfterSlides - 1;
    }

    public MediaPlayer Player { get; }
    public AudioElement Clip { get; }
    public DispatcherTimer Timer { get; }

    /// <summary>Slides it still plays on (null: until the show ends).</summary>
    public int? SlidesLeft { get; set; }
}

/// <summary>
/// A video or sound on a slide in the show. Plays through WPF (Windows Media Foundation): trimmed, with fades, loop
/// and volume; reports when Windows can't play the file instead of failing silently.
/// </summary>
internal abstract class MediaHost : Canvas
{
    protected readonly MediaPlayback Playback;
    protected readonly DispatcherTimer Ticker = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private AnimState _anim = AnimState.Shown(default);

    protected MediaHost(MediaClipElement clip, RenderContext ctx, MediaPlayback playback)
    {
        Element = clip;
        Playback = playback;
        Width = ctx.Deck.Width;
        Height = ctx.Deck.Height;
        IsHitTestVisible = false;
        Path = ctx.Media.ResolvePlayback(clip);
        if (Path == null) Error = clip.IsLinked ? "The linked file can't be found: " + (clip.LinkAbsolute ?? clip.LinkPath) : "The file is missing from the presentation.";
        Ticker.Tick += (_, _) => OnTick();
    }

    public MediaClipElement Element { get; }

    /// <summary>The file played (null when it can't be found).</summary>
    public string? Path { get; }

    /// <summary>Why it can't play, for people.</summary>
    public string? Error { get; protected set; }

    public abstract bool IsPlaying { get; }

    /// <summary>Seconds from the start of the file.</summary>
    public abstract double Position { get; }

    /// <summary>The file's length in seconds (0 until known).</summary>
    public abstract double FileLength { get; }

    public double Start => Math.Max(0, Element.TrimStart);

    public double End => Element.TrimEnd is { } e && e > Start ? (FileLength > 0 ? Math.Min(e, FileLength) : e) : FileLength;

    /// <summary>Visible on the slide right now (animations and "hide when not playing").</summary>
    public virtual bool IsShown => _anim.Visible;

    public abstract void Play();
    public abstract void Pause();
    public abstract void Stop();

    /// <summary>Jumps to <paramref name="seconds"/> from the start of the file.</summary>
    public abstract void Seek(double seconds);

    public abstract void Release();

    public void Toggle()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    /// <summary>Muted from the control bar (for this show only; the deck isn't changed).</summary>
    public bool UserMuted { get; set; }

    public void Run(MediaAction action, double? position = null)
    {
        switch (action)
        {
            case MediaAction.Play: Play(); break;
            case MediaAction.Pause: Pause(); break;
            case MediaAction.Stop: Stop(); break;
            case MediaAction.Toggle: Toggle(); break;
            case MediaAction.Seek: if (position is { } p) Seek(p); break;
            case MediaAction.ToggleMute:
                UserMuted = !UserMuted;
                OnTick();
                Changed();
                break;
        }
    }

    public static MediaAction ActionFor(EffectKind cue) => cue switch
    {
        EffectKind.Pause => MediaAction.Pause,
        EffectKind.Stop => MediaAction.Stop,
        _ => MediaAction.Play,
    };

    /// <summary>Volume with the fades applied (0..1).</summary>
    protected double CurrentVolume()
    {
        if (!Playback.Audible || UserMuted) return 0;
        return MathEx.Clamp(Element.Volume, 0, 1) * FadeFactor();
    }

    /// <summary>0..1 during fade in and fade out.</summary>
    protected double FadeFactor()
    {
        var pos = Position;
        var f = 1.0;
        if (Element.FadeIn > 0.01) f = Math.Min(f, MathEx.Clamp((pos - Start) / Element.FadeIn, 0, 1));
        if (Element.FadeOut > 0.01 && End > 0) f = Math.Min(f, MathEx.Clamp((End - pos) / Element.FadeOut, 0, 1));
        return f;
    }

    protected virtual void OnTick()
    {
        if (!IsPlaying) return;
        if (End > 0 && Position >= End - 0.03) Ended();
    }

    /// <summary>Reached the end (or the trim end): loop or stop.</summary>
    protected void Ended()
    {
        if (Element.Loop)
        {
            Seek(Start);
            return;
        }
        Stop();
    }

    public virtual void ApplyAnimation(AnimState s, double cacheScale)
    {
        _anim = s;
        Visibility = s.Visible ? Visibility.Visible : Visibility.Hidden;
        if (!s.Visible) return;
        Opacity = MathEx.Clamp(s.Opacity, 0, 1);
        RenderTransform = AnimGeometry.Transform(s);
        Clip = AnimGeometry.Clip(s, 60);
    }

    protected void Changed() => Playback.RaiseStateChanged(this);

    /// <summary>The element's box as a frame on the slide (rotated like the element).</summary>
    protected static void Place(FrameworkElement frame, Element e)
    {
        Canvas.SetLeft(frame, e.X);
        Canvas.SetTop(frame, e.Y);
        frame.Width = Math.Max(1, e.Width);
        frame.Height = Math.Max(1, e.Height);
        frame.RenderTransformOrigin = new Point(0.5, 0.5);
        frame.RenderTransform = Math.Abs(e.Rotation % 360) > 0.001 ? new RotateTransform(e.Rotation) : Transform.Identity;
    }
}

/// <summary>A video: the poster until it plays, then the picture; full screen, loop, fades, hidden when not playing.</summary>
internal sealed class VideoHost : MediaHost
{
    private readonly VideoElement _video;
    private readonly Grid _frame = new();
    private readonly MediaElement _player = new()
    {
        LoadedBehavior = MediaState.Manual,
        UnloadedBehavior = MediaState.Manual,
        ScrubbingEnabled = true,
        Stretch = Stretch.UniformToFill,
        IsHitTestVisible = false,
        Visibility = Visibility.Hidden,
    };
    private readonly Rectangle _backdrop = new() { Fill = Brushes.Black, IsHitTestVisible = false, Opacity = 0 };
    private readonly TextBlock _error = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Visibility = Visibility.Collapsed };
    private bool _playing;
    private bool _opened;
    private bool _wantPlay;
    private double _length;
    private readonly bool _editing;

    public VideoHost(VideoElement video, RenderContext ctx, SlideStyle style, MediaPlayback playback, bool editing = false) : base(video, ctx, playback)
    {
        _video = video;
        _editing = editing;
        _length = video.Length ?? 0;
        _backdrop.Width = Width;
        _backdrop.Height = Height;
        Children.Add(_backdrop);
        Place(_frame, video);
        // The poster (with the element's shadow and border), drawn by the shared renderer in the frame's own box.
        var poster = new DrawingGroup();
        using (var dc = poster.Open()) ElementRenderer.Draw(dc, video, style, new RenderContext(ctx.Deck, ctx.Media, editing: false), local: true);
        poster.Freeze();
        _frame.Children.Add(new DrawingHost(poster, Math.Max(1, video.Width), Math.Max(1, video.Height)));
        var holder = new Border { Child = _player, ClipToBounds = false };
        holder.Clip = ElementRenderer.MaskClip(video.Mask, video.CornerRadius, new Rect(0, 0, Math.Max(1, video.Width), Math.Max(1, video.Height)));
        _frame.Children.Add(holder);
        if (video.Stroke != null && ElementRenderer.MakePen(video.Stroke, style) is { } pen)
        {
            var border = new System.Windows.Shapes.Path { Data = ElementRenderer.MaskClip(video.Mask, video.CornerRadius, new Rect(0, 0, Math.Max(1, video.Width), Math.Max(1, video.Height))), Stroke = pen.Brush, StrokeThickness = pen.Thickness, IsHitTestVisible = false };
            _frame.Children.Add(border);
        }
        var errorBack = new Border { Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), Child = _error, Visibility = Visibility.Collapsed };
        _frame.Children.Add(errorBack);
        Children.Add(_frame);
        RenderOptions.SetBitmapScalingMode(_player, BitmapScalingMode.HighQuality);

        _player.MediaOpened += (_, _) =>
        {
            _opened = true;
            if (_player.NaturalDuration.HasTimeSpan) _length = _player.NaturalDuration.TimeSpan.TotalSeconds;
            if (_wantPlay) StartPlayback();
        };
        _player.MediaEnded += (_, _) => Ended();
        _player.MediaFailed += (_, e) =>
        {
            Log.Error(e.ErrorException, "Play video " + Path);
            ShowError(MediaProbe.CantPlayAdvice);
        };
        if (Path == null) ShowError(Error!);
        else
        {
            try
            {
                _player.Source = new Uri(Path);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Open video " + Path);
                ShowError(MediaProbe.CantPlayAdvice);
            }
        }
        UpdateVisibility();
    }

    private void ShowError(string message)
    {
        Error = message;
        _error.Text = "This video can't play here.\n" + message;
        if (_error.Parent is Border b) b.Visibility = Visibility.Visible;
        _error.Visibility = Visibility.Visible;
        _wantPlay = false;
        _playing = false;
        Changed();
    }

    public override bool IsPlaying => _playing;

    public override double Position => _opened ? _player.Position.TotalSeconds : Start;

    public override double FileLength => _length;

    public override bool IsShown => base.IsShown && (_editing || !_video.HideWhenNotPlaying || _playing);

    public override void Play()
    {
        if (Error != null || Path == null) return;
        _wantPlay = true;
        if (!_opened)
        {
            // Opening: plays as soon as the file is ready.
            _player.Play();
            _player.Pause();
            return;
        }
        StartPlayback();
    }

    private void StartPlayback()
    {
        _wantPlay = false;
        var pos = Position;
        if (pos < Start - 0.05 || (End > 0 && pos >= End - 0.05)) _player.Position = TimeSpan.FromSeconds(Start);
        _player.Volume = CurrentVolume();
        _player.IsMuted = !Playback.Audible;
        _player.Visibility = Visibility.Visible;
        _player.Play();
        _playing = true;
        Ticker.Start();
        if (_video.FullScreen && !_editing) EnterFullScreen();
        UpdateVisibility();
        Changed();
    }

    public override void Pause()
    {
        _wantPlay = false;
        if (!_playing) return;
        _player.Pause();
        _playing = false;
        Ticker.Stop();
        UpdateVisibility();
        Changed();
    }

    public override void Stop()
    {
        _wantPlay = false;
        var was = _playing;
        if (_opened)
        {
            _player.Pause();
            _player.Position = TimeSpan.FromSeconds(Start);
        }
        _playing = false;
        Ticker.Stop();
        _player.Visibility = Visibility.Hidden;
        LeaveFullScreen();
        UpdateVisibility();
        if (was) Changed();
    }

    public override void Seek(double seconds)
    {
        if (!_opened) return;
        var end = End > 0 ? End : seconds;
        _player.Position = TimeSpan.FromSeconds(MathEx.Clamp(seconds, Start, Math.Max(Start, end)));
        if (!_playing) _player.Visibility = Visibility.Visible;
        Changed();
    }

    public override void Release()
    {
        Ticker.Stop();
        try
        {
            _player.Stop();
            _player.Close();
            _player.Source = null;
        }
        catch
        {
            // closing anyway
        }
        _playing = false;
    }

    protected override void OnTick()
    {
        if (!_playing) return;
        _player.Volume = CurrentVolume();
        // Fades also fade the picture.
        if (_video.FadeIn > 0.01 || _video.FadeOut > 0.01) _player.Opacity = FadeFactor();
        base.OnTick();
    }

    private void UpdateVisibility()
    {
        _frame.Visibility = !_editing && _video.HideWhenNotPlaying && !_playing ? Visibility.Hidden : Visibility.Visible;
    }

    private void EnterFullScreen()
    {
        // Grow the frame to the largest box of the video's shape that fits the slide, over a black backdrop.
        var w = _player.NaturalVideoWidth > 0 ? _player.NaturalVideoWidth : _video.Width;
        var h = _player.NaturalVideoHeight > 0 ? _player.NaturalVideoHeight : _video.Height;
        var k = Math.Min(Width / w, Height / h);
        var target = new Rect((Width - w * k) / 2, (Height - h * k) / 2, w * k, h * k);
        Canvas.SetLeft(_frame, target.X);
        Canvas.SetTop(_frame, target.Y);
        _frame.Width = target.Width;
        _frame.Height = target.Height;
        _frame.RenderTransform = Transform.Identity;
        _player.Stretch = Stretch.Uniform;
        foreach (var child in _frame.Children.OfType<FrameworkElement>())
            if (child is not Border) child.Visibility = Visibility.Hidden;
        if (_player.Parent is Border holder) holder.Clip = null;
        _backdrop.Opacity = 1;
    }

    private void LeaveFullScreen()
    {
        if (_backdrop.Opacity <= 0) return;
        _backdrop.Opacity = 0;
        Place(_frame, _video);
        _player.Stretch = Stretch.UniformToFill;
        foreach (var child in _frame.Children.OfType<FrameworkElement>()) child.Visibility = Visibility.Visible;
        if (_player.Parent is Border holder) holder.Clip = ElementRenderer.MaskClip(_video.Mask, _video.CornerRadius, new Rect(0, 0, Math.Max(1, _video.Width), Math.Max(1, _video.Height)));
        if (_error.Parent is Border b && Error == null) b.Visibility = Visibility.Collapsed;
    }
}

/// <summary>A sound: the speaker icon (unless hidden) and a player without a picture.</summary>
internal sealed class AudioHost : MediaHost
{
    private readonly AudioElement _audio;
    private MediaPlayer? _player;
    private bool _playing;
    private bool _opened;
    private bool _wantPlay;
    private double _length;

    public AudioHost(AudioElement audio, RenderContext ctx, SlideStyle style, MediaPlayback playback) : base(audio, ctx, playback)
    {
        _audio = audio;
        _length = audio.Length ?? 0;
        var icon = new DrawingGroup();
        using (var dc = icon.Open()) ElementRenderer.Draw(dc, audio, style, ctx);
        icon.Freeze();
        Children.Add(new DrawingHost(icon, Width, Height));
    }

    public override bool IsPlaying => _playing;

    public override double Position => _player != null && _opened ? _player.Position.TotalSeconds : Start;

    public override double FileLength => _length;

    /// <summary>A hidden speaker can't be clicked, but it still plays.</summary>
    public override bool IsShown => base.IsShown && !_audio.HideIcon;

    private MediaPlayer Player()
    {
        if (_player != null) return _player;
        // Background music still playing from an earlier slide: carry on with it.
        if (Playback.Background.TryGetValue(_audio.Id, out var carried))
        {
            Playback.Background.Remove(_audio.Id);
            carried.Timer.Stop();
            _player = carried.Player;
            _opened = true;
            _playing = true;
            Hook(_player);
            Ticker.Start();
            return _player;
        }
        _player = new MediaPlayer { Volume = 0, IsMuted = !Playback.Audible };
        Hook(_player);
        if (Path != null)
        {
            try
            {
                _player.Open(new Uri(Path));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Open sound " + Path);
                Error = MediaProbe.CantPlayAdvice;
            }
        }
        return _player;
    }

    private void Hook(MediaPlayer p)
    {
        p.MediaOpened += (_, _) =>
        {
            _opened = true;
            if (p.NaturalDuration.HasTimeSpan) _length = p.NaturalDuration.TimeSpan.TotalSeconds;
            if (_wantPlay) StartPlayback();
        };
        p.MediaEnded += (_, _) => Ended();
        p.MediaFailed += (_, e) =>
        {
            Log.Error(e.ErrorException, "Play sound " + Path);
            Error = MediaProbe.CantPlayAdvice;
            _playing = false;
            Changed();
        };
    }

    /// <summary>Already playing background music from before this slide.</summary>
    public bool AdoptBackground()
    {
        if (!Playback.Background.ContainsKey(_audio.Id)) return false;
        Player();
        Changed();
        return true;
    }

    public override void Play()
    {
        if (Error != null || Path == null) return;
        var p = Player();
        if (_playing) return;
        _wantPlay = true;
        if (_opened) StartPlayback();
    }

    private void StartPlayback()
    {
        _wantPlay = false;
        var p = _player!;
        var pos = Position;
        if (pos < Start - 0.05 || (End > 0 && pos >= End - 0.05)) p.Position = TimeSpan.FromSeconds(Start);
        p.IsMuted = !Playback.Audible;
        p.Volume = CurrentVolume();
        p.Play();
        _playing = true;
        Ticker.Start();
        Changed();
    }

    public override void Pause()
    {
        _wantPlay = false;
        if (!_playing || _player == null) return;
        _player.Pause();
        _playing = false;
        Ticker.Stop();
        Changed();
    }

    public override void Stop()
    {
        _wantPlay = false;
        if (_player != null)
        {
            _player.Stop();
            if (_opened) _player.Position = TimeSpan.FromSeconds(Start);
        }
        var was = _playing;
        _playing = false;
        Ticker.Stop();
        if (was) Changed();
    }

    public override void Seek(double seconds)
    {
        if (_player == null || !_opened) return;
        var end = End > 0 ? End : seconds;
        _player.Position = TimeSpan.FromSeconds(MathEx.Clamp(seconds, Start, Math.Max(Start, end)));
        Changed();
    }

    protected override void OnTick()
    {
        if (_playing && _player != null) _player.Volume = CurrentVolume();
        base.OnTick();
    }

    /// <summary>
    /// Leaving the slide: background music hands its player to the surface and keeps playing; anything else stops.
    /// Returns true when it was handed over.
    /// </summary>
    public bool HandOff()
    {
        if (!_audio.AcrossSlides || !_playing || _player == null || !Playback.Sound || _audio.StopAfterSlides == 1) return false;
        Ticker.Stop();
        var player = _player;
        var clip = _audio;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        var carried = new CarriedSound(player, clip, timer);
        timer.Tick += (_, _) =>
        {
            // Keeps volume fades, trim end and looping going between slides.
            var start = Math.Max(0, clip.TrimStart);
            var length = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0;
            var end = clip.TrimEnd is { } e && e > start ? (length > 0 ? Math.Min(e, length) : e) : length;
            var pos = player.Position.TotalSeconds;
            if (end > 0 && pos >= end - 0.03)
            {
                if (clip.Loop) player.Position = TimeSpan.FromSeconds(start);
                else
                {
                    timer.Stop();
                    player.Close();
                    Playback.Background.Remove(clip.Id);
                    return;
                }
            }
            var f = 1.0;
            if (clip.FadeOut > 0.01 && end > 0) f = MathEx.Clamp((end - pos) / clip.FadeOut, 0, 1);
            player.Volume = Playback.Audible ? MathEx.Clamp(clip.Volume, 0, 1) * f : 0;
        };
        timer.Start();
        Playback.Background[clip.Id] = carried;
        _player = null;
        _playing = false;
        _opened = false;
        return true;
    }

    public override void Release()
    {
        Ticker.Stop();
        if (_player == null) return;
        try
        {
            _player.Stop();
            _player.Close();
        }
        catch
        {
            // closing anyway
        }
        _player = null;
        _playing = false;
    }
}
