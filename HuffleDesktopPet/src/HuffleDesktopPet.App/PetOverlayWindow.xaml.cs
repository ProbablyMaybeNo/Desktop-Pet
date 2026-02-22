using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HuffleDesktopPet.Core;
using HuffleDesktopPet.Core.Models;
using HuffleDesktopPet.Core.Services;
using WinForms = System.Windows.Forms;
using Drawing  = System.Drawing;

namespace HuffleDesktopPet;

/// <summary>
/// Transparent always-on-top overlay window that is the pet's physical presence on screen.
///
/// Milestone A  — placeholder ellipse visible, draggable ✓
/// Milestone B  — tray icon, click-through toggle ✓
/// Milestone C  — autonomous wandering via WanderService ✓
/// Milestone D  — need decay + auto-save + tooltip ✓
/// Milestone E  — interactions (Feed/Play/Clean/Study) + visual need states + startup toggle ✓
/// Milestone F  — sprite animation layer, frame timer, directional flip ✓
/// Milestone G  — real-clock sleep, faint, celebrating, happy, tray-icon upgrade ✓
/// Milestone H  — 5-bar need system, sleep accounting, priority state machine,
///                need prompts, poke interaction, schema v2 ✓
/// </summary>
public partial class PetOverlayWindow : Window
{
    // ── State ─────────────────────────────────────────────────────────────────
    private PetState          _state     = new();
    private WanderService     _wander    = null!;
    private AnimationService? _animation;

    // ── Sprite frames ─────────────────────────────────────────────────────────
    private readonly Dictionary<string, BitmapImage[]> _spriteFrames = new();

    // ── Timers ────────────────────────────────────────────────────────────────
    private DispatcherTimer _wanderTimer = null!;   // ~30 fps
    private DispatcherTimer _needsTimer  = null!;   // every 60 s

    // ── Tray ──────────────────────────────────────────────────────────────────
    private WinForms.NotifyIcon        _trayIcon             = null!;
    private WinForms.ContextMenuStrip  _trayMenu             = null!;
    private WinForms.ToolStripMenuItem _trayItemClickThrough = null!;
    private WinForms.ToolStripMenuItem _trayItemStartup      = null!;

    // ── Particle system ───────────────────────────────────────────────────────
    private sealed class Particle
    {
        public readonly TextBlock      Text;
        public readonly ScaleTransform Scale = new(1, 1);
        public double Phase;   // 0.0–1.0 position within one animation cycle

        public Particle(TextBlock tb, double initialPhase)
        {
            Text                       = tb;
            Phase                      = initialPhase;
            tb.RenderTransform         = Scale;
            tb.RenderTransformOrigin   = new Point(0.5, 0.5);
            tb.IsHitTestVisible        = false;
            tb.Opacity                 = 0;
        }
    }

    private readonly List<Particle> _zzzParticles   = new();
    private readonly List<Particle> _heartParticles = new();

    // ── Click-through ─────────────────────────────────────────────────────────
    private bool _clickThrough = false;

    // ── P/Invoke ──────────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_LAYERED     = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    // ── Placeholder-ellipse colours ───────────────────────────────────────────
    private static readonly SolidColorBrush BrushNormal   = new(Color.FromArgb(0xCC, 0x7B, 0x5E, 0xA6));
    private static readonly SolidColorBrush BrushWarning  = new(Color.FromArgb(0xCC, 0xCC, 0x88, 0x33));
    private static readonly SolidColorBrush BrushCritical = new(Color.FromArgb(0xCC, 0xCC, 0x44, 0x33));

    // ─────────────────────────────────────────────────────────────────────────

    public PetOverlayWindow()
    {
        InitializeComponent();
        BuildTrayIcon();

        // Left-click: drag, or poke if no significant movement
        MouseLeftButtonDown += OnPetMouseDown;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        _state = await PetPersistence.LoadAsync();
        PetEngine.Tick(_state, DateTime.UtcNow);   // catch up on offline time

        var area = SystemParameters.WorkArea;
        double maxX = area.Right  - Width;
        double maxY = area.Bottom - Height;

        _wander = new WanderService(
            _state.PositionX * maxX, _state.PositionY * maxY,
            minX: area.Left, minY: area.Top,
            maxX: maxX,      maxY: maxY);

        Left = _wander.X;
        Top  = _wander.Y;

        LoadSprites();
        SetupParticles();
        StartTimers();
        RefreshPetAppearance();
    }

    protected override async void OnClosed(EventArgs e)
    {
        StopTimers();
        DisposeTray();

        var area = SystemParameters.WorkArea;
        double maxX = Math.Max(1, area.Right  - Width);
        double maxY = Math.Max(1, area.Bottom - Height);
        _state.PositionX = Left / maxX;
        _state.PositionY = Top  / maxY;

        await PetPersistence.SaveAsync(_state);
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    // ── Sprite loading ────────────────────────────────────────────────────────

    private void LoadSprites()
    {
        string exeDir     = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
        string spritesDir = Path.Combine(exeDir, "assets", "sprites");

        if (!Directory.Exists(spritesDir)) return;

        string[] states =
        [
            // Core locomotion
            "walk", "idle",
            // Need states (prompts)
            "hungry", "tired", "dirty", "bored", "sad",
            // Mood
            "happy",
            // Interaction transients
            "eat", "clean", "study", "poked", "playing",
            // Special / environmental
            "sleep", "faint", "celebrating", "booing", "cautious",
        ];

        var frameCounts = new Dictionary<string, int>();

        foreach (string state in states)
        {
            var frames = new List<BitmapImage>();
            for (int i = 1; ; i++)
            {
                string path = Path.Combine(spritesDir, $"huffle_{state}_{i:D2}.png");
                if (!File.Exists(path)) break;

                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource        = new Uri(path);
                    bmp.CacheOption      = BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = 128;               // 2× upscale at decode time
                    bmp.EndInit();
                    bmp.Freeze();
                    frames.Add(bmp);
                }
                catch { /* corrupt frame — skip */ }
            }

            if (frames.Count > 0)
            {
                _spriteFrames[state] = [.. frames];
                frameCounts[state]   = frames.Count;
            }
        }

        if (frameCounts.Count == 0) return;

        _animation = new AnimationService(frameCounts);

        PetSprite.Visibility       = Visibility.Visible;
        PlaceholderGrid.Visibility = Visibility.Collapsed;

        LoadItemSprites(spritesDir);
        UpdateSpriteFrame();
        UpdateTrayIconFromSprite(spritesDir);
    }

    private void UpdateTrayIconFromSprite(string spritesDir)
    {
        string path = Path.Combine(spritesDir, "huffle_idle_01.png");
        if (!File.Exists(path)) return;

        try
        {
            using var source = new Drawing.Bitmap(path);
            using var scaled = new Drawing.Bitmap(32, 32);
            using var g      = Drawing.Graphics.FromImage(scaled);
            g.InterpolationMode = Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode   = Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(source, 0, 0, 32, 32);

            _trayIcon.Icon = Drawing.Icon.FromHandle(scaled.GetHicon());
        }
        catch { /* keep placeholder icon */ }
    }

    // Maps animation states that don't yet have sprites to a visual stand-in.
    // Remove an entry once the real sprite sheet is added to assets/sprites/.
    private static readonly Dictionary<string, string> SpriteFallbacks = new()
    {
        // tired  → real sprites added (huffle_tired_01-04.png)
        // poked  → real sprites added (huffle_poked_01-04.png)
        // playing → real sprites added (huffle_playing_01-06.png)
    };

    // ── Item overlay ──────────────────────────────────────────────────────────

    // Asset filenames: item_food_{name}.png / item_obj_{name}.png
    private static readonly string[] FoodItemNames   = ["apple", "cupcake", "banana", "hamburger", "cherry"];
    private static readonly string[] ObjectItemNames  = ["soccerball", "sword", "balloon", "book", "gamepad"];

    // Paw anchor per playing frame in 64×64 sprite coordinates.
    // Multiply by SpriteScale (= 128/64 = 2) to get window-pixel offset.
    // Centre of the held item is placed here.
    private static readonly (int X, int Y)[] PlayingAnchors =
    [
        (19, 29), // playing_01 — calm hold
        (20, 29), // playing_02 — alert
        (21, 34), // playing_03 — excited / sparkles
        (20, 38), // playing_04 — overwhelmed
        (23, 50), // playing_05 — big splash peak
        (18, 55), // playing_06 — satisfied
    ];

    private readonly Dictionary<string, BitmapImage> _itemSprites = new();
    private string? _heldItemKey;   // e.g. "obj_balloon" — null means no item shown

    private void LoadItemSprites(string spritesDir)
    {
        foreach (string food in FoodItemNames)
        {
            string path = Path.Combine(spritesDir, $"item_food_{food}.png");
            if (!File.Exists(path)) continue;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource   = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                _itemSprites[$"food_{food}"] = bmp;
            }
            catch { }
        }

        foreach (string obj in ObjectItemNames)
        {
            string path = Path.Combine(spritesDir, $"item_obj_{obj}.png");
            if (!File.Exists(path)) continue;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource   = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                _itemSprites[$"obj_{obj}"] = bmp;
            }
            catch { }
        }
    }

    private string PickRandomItem()
    {
        string name = ObjectItemNames[Random.Shared.Next(ObjectItemNames.Length)];
        return $"obj_{name}";
    }

    private void UpdateSpriteFrame()
    {
        if (_animation is null) return;

        string state = _animation.CurrentState;
        if (!_spriteFrames.TryGetValue(state, out var frames) &&
            (!SpriteFallbacks.TryGetValue(state, out string? fb) ||
             !_spriteFrames.TryGetValue(fb, out frames)))
            return;

        int idx = Math.Min(_animation.CurrentFrame, frames!.Length - 1);
        PetSprite.Source = frames[idx];
        UpdateItemOverlay(state, idx);
    }

    private void UpdateItemOverlay(string state, int frameIdx)
    {
        if (state != "playing" || _heldItemKey is null ||
            !_itemSprites.TryGetValue(_heldItemKey, out var itemBmp))
        {
            ItemSprite.Visibility = Visibility.Collapsed;
            return;
        }

        const double SpriteScale = 128.0 / 64.0;  // source sprite → window pixels
        const double ItemSize    = 48.0;           // rendered size of the item in window px

        if (frameIdx >= 0 && frameIdx < PlayingAnchors.Length)
        {
            var (ax, ay) = PlayingAnchors[frameIdx];
            // Anchor is the item's visual centre; compute top-left margin accordingly
            ItemSprite.Width  = ItemSize;
            ItemSprite.Height = ItemSize;
            ItemSprite.Margin = new Thickness(
                ax * SpriteScale - ItemSize / 2,
                ay * SpriteScale - ItemSize / 2,
                0, 0);
        }

        ItemSprite.Source     = itemBmp;
        ItemSprite.Visibility = Visibility.Visible;
    }

    // ── Particle system ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates the Zzz (sleep) and heart (happy) TextBlock particles and adds
    /// them to the ParticleCanvas overlay.  Call once after the window renders.
    /// </summary>
    private void SetupParticles()
    {
        // ── Zzz — three Z's of growing size, staggered by 1/3 of cycle ──────
        (string label, double size)[] zDefs = [("z", 7), ("Z", 10), ("Z", 13)];
        for (int i = 0; i < zDefs.Length; i++)
        {
            var (label, size) = zDefs[i];
            var tb = new TextBlock
            {
                Text       = label,
                FontSize   = size,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 200, 255)),
            };
            ParticleCanvas.Children.Add(tb);
            _zzzParticles.Add(new Particle(tb, initialPhase: i / 3.0));
        }

        // ── Hearts — three hearts staggered by 1/3 of cycle ──────────────────
        for (int i = 0; i < 3; i++)
        {
            var tb = new TextBlock
            {
                Text       = "♥",
                FontSize   = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 110, 140)),
            };
            ParticleCanvas.Children.Add(tb);
            _heartParticles.Add(new Particle(tb, initialPhase: i / 3.0));
        }
    }

    /// <summary>Advances and positions all particle groups each wander tick.</summary>
    private void UpdateParticles(double delta)
    {
        string? state = _animation?.CurrentState;
        TickZzzParticles(delta,   active: state == "sleep");
        TickHeartParticles(delta, active: state == "happy");
    }

    /// <summary>
    /// Zzz particles rise from near the pet's head (upper-right area) and drift
    /// upward + rightward.  Sprites face right by default, so the Z's emerge
    /// from that side; when flipped for left-facing movement they're mirrored
    /// automatically by SpriteFlip on the Image, but the canvas is not flipped —
    /// the Z's stay in their coded position (acceptable for a subtle effect).
    /// </summary>
    private void TickZzzParticles(double delta, bool active)
    {
        for (int i = 0; i < _zzzParticles.Count; i++)
        {
            var p = _zzzParticles[i];
            if (!active) { p.Text.Opacity = 0; continue; }

            p.Phase = (p.Phase + delta / 2.5) % 1.0;
            double t = p.Phase;

            // Start from above the head (right side), drift up and right
            Canvas.SetLeft(p.Text, 68 + i * 5 + 10 * t);
            Canvas.SetTop( p.Text, 30          - 25 * t);

            p.Text.Opacity = FadeInOut(t, fadeInEnd: 0.15, fadeOutStart: 0.70);

            double s = 0.60 + 0.40 * t;
            p.Scale.ScaleX = s;
            p.Scale.ScaleY = s;
        }
    }

    /// <summary>Hearts float upward from the pet's center, spreading slightly.</summary>
    private void TickHeartParticles(double delta, bool active)
    {
        for (int i = 0; i < _heartParticles.Count; i++)
        {
            var p = _heartParticles[i];
            if (!active) { p.Text.Opacity = 0; continue; }

            p.Phase = (p.Phase + delta / 2.0) % 1.0;
            double t = p.Phase;

            // Spread across the width, float straight up
            Canvas.SetLeft(p.Text, 44 + i * 16);
            Canvas.SetTop( p.Text, 28          - 24 * t);

            p.Text.Opacity = FadeInOut(t, fadeInEnd: 0.15, fadeOutStart: 0.72);

            double s = 0.75 + 0.35 * t;
            p.Scale.ScaleX = s;
            p.Scale.ScaleY = s;
        }
    }

    /// <summary>Ramps opacity 0→1 during [0, fadeInEnd], holds 1, then fades 1→0 during [fadeOutStart, 1].</summary>
    private static double FadeInOut(double t, double fadeInEnd, double fadeOutStart) =>
        t < fadeInEnd    ? t / fadeInEnd :
        t > fadeOutStart ? (1.0 - t) / (1.0 - fadeOutStart) :
                           1.0;

    // ── Timers ────────────────────────────────────────────────────────────────

    private void StartTimers()
    {
        _wanderTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33)   // ~30 fps
        };
        _wanderTimer.Tick += OnWanderTick;
        _wanderTimer.Start();

        _needsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _needsTimer.Tick += OnNeedsTick;
        _needsTimer.Start();
    }

    private void StopTimers()
    {
        _wanderTimer?.Stop();
        _needsTimer?.Stop();
    }

    private DateTime _lastWanderTick = DateTime.UtcNow;

    private void OnWanderTick(object? sender, EventArgs e)
    {
        var    now   = DateTime.UtcNow;
        double delta = (now - _lastWanderTick).TotalSeconds;
        _lastWanderTick = now;

        // Suppress wandering while sleeping or fainted (pet stays still)
        bool frozen = _state.IsSleeping || _state.IsFainting;
        if (!frozen)
        {
            _wander.Tick(delta);
            Left = _wander.X;
            Top  = _wander.Y;
        }

        if (_animation is not null)
        {
            bool isMoving = !frozen && !_wander.IsIdle;
            _animation.Tick(delta, _state, isMoving);
            UpdateSpriteFrame();
            SpriteFlip.ScaleX = _wander.FacingLeft ? -1 : 1;
        }

        UpdateParticles(delta);
    }

    private void OnNeedsTick(object? sender, EventArgs e)
    {
        var events = PetEngine.Tick(_state, DateTime.UtcNow);
        RefreshPetAppearance();
        _ = PetPersistence.SaveAsync(_state);

        // Trigger faint animation if the engine detected a faint condition this tick
        if ((events & (PetTickEvents.TiredFaintTriggered | PetTickEvents.HungerFaintTriggered)) != 0)
            _animation?.TriggerFaint();
    }

    // ── Visual state ──────────────────────────────────────────────────────────

    private void RefreshPetAppearance()
    {
        // Bars are 0 = best, 100 = worst; show warning/critical when HIGH
        float maxNeed = Math.Max(
            Math.Max(_state.Hunger, _state.Tired),
            Math.Max(_state.Dirty,  _state.Bored));

        if (maxNeed >= NeedConfig.Stage3Threshold || _state.Sad >= NeedConfig.Stage3Threshold)
        {
            PetBody.Fill        = BrushCritical;
            PetSmile.Visibility = Visibility.Collapsed;
            PetFrown.Visibility = Visibility.Visible;
        }
        else if (maxNeed >= NeedConfig.Stage2Threshold || _state.Sad >= NeedConfig.Stage2Threshold)
        {
            PetBody.Fill        = BrushWarning;
            PetSmile.Visibility = Visibility.Collapsed;
            PetFrown.Visibility = Visibility.Visible;
        }
        else
        {
            PetBody.Fill        = BrushNormal;
            PetSmile.Visibility = Visibility.Visible;
            PetFrown.Visibility = Visibility.Collapsed;
        }

        string sleepNote = _state.IsForcedSleep ? " (forced recovery)"
                         : _state.IsSleeping    ? " (sleeping)"
                         : "";
        int sleepMin  = _state.GetSleepMinutesLast24h();
        int sleepGoal = (int)NeedConfig.SleepRequiredMinutes;

        NeedsText.Text =
            $"Hunger {_state.Hunger,5:F0}/100\n" +
            $"Tired  {_state.Tired,5:F0}/100\n"  +
            $"Dirty  {_state.Dirty,5:F0}/100\n"  +
            $"Bored  {_state.Bored,5:F0}/100\n"  +
            $"Sad    {_state.Sad,5:F0}/100\n"     +
            $"Sleep  {sleepMin,3}/{sleepGoal} min{sleepNote}";
    }

    // ── Interactions ──────────────────────────────────────────────────────────

    private enum Interaction { Feed, Play, Clean, Study }

    /// <summary>Shared path for all menu / tray interactions.</summary>
    private void OnInteract(Interaction action)
    {
        WakeIfScheduledSleep();

        switch (action)
        {
            case Interaction.Feed:
                PetEngine.Feed(_state);
                _animation?.TriggerTransient("eat");
                break;
            case Interaction.Play:
                PetEngine.Play(_state);
                _heldItemKey = PickRandomItem();
                _animation?.TriggerTransient("playing");
                break;
            case Interaction.Clean:
                PetEngine.Clean(_state);
                _animation?.TriggerTransient("clean");
                break;
            case Interaction.Study:
                PetEngine.Study(_state);
                _animation?.TriggerTransient("study");
                break;
        }

        RefreshPetAppearance();
        _ = PetPersistence.SaveAsync(_state);

        // Celebrate when all needs are in the happy zone and no interaction animation is running
        bool allGood = _state.Hunger < NeedConfig.HappyNeedMax &&
                       _state.Tired  < NeedConfig.HappyNeedMax &&
                       _state.Dirty  < NeedConfig.HappyNeedMax &&
                       _state.Bored  < NeedConfig.HappyNeedMax;
        if (allGood && _animation?.IsPlayingTransient == false)
            _animation?.TriggerTransient("celebrating");
    }

    // ── Poke (direct click on pet body) ───────────────────────────────────────

    private void OnPetMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough) return;

        double startLeft = Left;
        double startTop  = Top;

        DragMove();   // blocks until mouse is released

        // If the window barely moved, treat it as a poke click
        bool wasDrag = Math.Abs(Left - startLeft) > 5 || Math.Abs(Top - startTop) > 5;
        if (!wasDrag)
            OnPoke();
    }

    private void OnPoke()
    {
        // Forced sleep cannot be interrupted by poke
        if (_state.IsForcedSleep) return;

        WakeIfScheduledSleep();
        PetEngine.Poke(_state);
        _animation?.WakeUp();
        _animation?.TriggerTransient("poked");

        RefreshPetAppearance();
        _ = PetPersistence.SaveAsync(_state);
    }

    /// <summary>
    /// If the pet is in a scheduled (non-forced) sleep window, wakes it and marks
    /// it as having woken early so it won't re-enter sleep until the next window.
    /// </summary>
    private void WakeIfScheduledSleep()
    {
        if (_state.IsSleeping && !_state.IsForcedSleep)
        {
            _state.WokeEarlyInWindow = true;
            _state.IsSleeping        = false;
        }
        _animation?.WakeUp();
    }

    // XAML ContextMenu handlers
    private void OnFeedClick(object  sender, RoutedEventArgs e) => OnInteract(Interaction.Feed);
    private void OnPlayClick(object  sender, RoutedEventArgs e) => OnInteract(Interaction.Play);
    private void OnCleanClick(object sender, RoutedEventArgs e) => OnInteract(Interaction.Clean);
    private void OnStudyClick(object sender, RoutedEventArgs e) => OnInteract(Interaction.Study);

    // ── Tray icon ─────────────────────────────────────────────────────────────

    private void BuildTrayIcon()
    {
        _trayItemClickThrough = new WinForms.ToolStripMenuItem("Click-through: OFF", null, OnTrayToggleClickThrough);
        _trayItemStartup      = new WinForms.ToolStripMenuItem(
            StartupManager.IsEnabled() ? "Start with Windows: ON" : "Start with Windows: OFF",
            null, OnTrayToggleStartup);

        _trayMenu = new WinForms.ContextMenuStrip();
        _trayMenu.Items.Add("Show / Hide",  null, OnTrayToggleVisibility);
        _trayMenu.Items.Add(_trayItemClickThrough);
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
        _trayMenu.Items.Add("Feed",  null, (_, _) => OnInteract(Interaction.Feed));
        _trayMenu.Items.Add("Play",  null, (_, _) => OnInteract(Interaction.Play));
        _trayMenu.Items.Add("Clean", null, (_, _) => OnInteract(Interaction.Clean));
        _trayMenu.Items.Add("Study", null, (_, _) => OnInteract(Interaction.Study));
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
        _trayMenu.Items.Add(_trayItemStartup);
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit",  null, OnTrayExit);

        _trayIcon = new WinForms.NotifyIcon
        {
            Text             = "HuffleDesktopPet — right-click for options",
            Icon             = MakePlaceholderIcon(),
            ContextMenuStrip = _trayMenu,
            Visible          = true,
        };
        _trayIcon.DoubleClick += OnTrayToggleVisibility;
    }

    private static Drawing.Icon MakePlaceholderIcon()
    {
        using var bmp = new Drawing.Bitmap(16, 16);
        using var g   = Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.FillEllipse(new Drawing.SolidBrush(Drawing.Color.FromArgb(180, 123, 94, 166)), 1, 1, 14, 14);
        g.DrawEllipse(new Drawing.Pen(Drawing.Color.FromArgb(255, 74, 45, 122), 1.5f), 1, 1, 13, 13);
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private void DisposeTray()
    {
        _trayIcon.Visible = false;
        _trayMenu.Dispose();
        _trayIcon.Dispose();
    }

    private void OnTrayToggleVisibility(object? sender, EventArgs e)
    {
        if (IsVisible) Hide(); else Show();
    }

    private void OnTrayToggleClickThrough(object? sender, EventArgs e)
    {
        _clickThrough = !_clickThrough;

        var hwnd    = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        if (_clickThrough)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);
            _trayItemClickThrough.Text = "Click-through: ON";
        }
        else
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
            _trayItemClickThrough.Text = "Click-through: OFF";
        }
    }

    private void OnTrayToggleStartup(object? sender, EventArgs e)
    {
        if (StartupManager.IsEnabled())
        {
            StartupManager.Disable();
            _trayItemStartup.Text = "Start with Windows: OFF";
        }
        else
        {
            StartupManager.Enable(Environment.ProcessPath ?? string.Empty);
            _trayItemStartup.Text = "Start with Windows: ON";
        }
    }

    private void OnTrayExit(object? sender, EventArgs e) => Close();
}
