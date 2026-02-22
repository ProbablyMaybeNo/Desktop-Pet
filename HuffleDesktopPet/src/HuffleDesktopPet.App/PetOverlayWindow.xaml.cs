using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
/// </summary>
public partial class PetOverlayWindow : Window
{
    // ── State ─────────────────────────────────────────────────────────────────
    private PetState         _state     = new();
    private WanderService    _wander    = null!;
    private AnimationService? _animation;

    // ── Sleep / faint cooldown ────────────────────────────────────────────────
    private DateTime _lastFaintTime = DateTime.MinValue;
    private const double FaintCooldownMinutes = 10.0;

    // ── Sprite frames ─────────────────────────────────────────────────────────
    // state-name → ordered array of pre-loaded, frozen BitmapImages
    private readonly Dictionary<string, BitmapImage[]> _spriteFrames = new();

    // ── Timers ────────────────────────────────────────────────────────────────
    private DispatcherTimer _wanderTimer = null!;   // ~30 fps
    private DispatcherTimer _needsTimer  = null!;   // every 60 s

    // ── Tray ──────────────────────────────────────────────────────────────────
    private WinForms.NotifyIcon        _trayIcon          = null!;
    private WinForms.ContextMenuStrip  _trayMenu          = null!;
    private WinForms.ToolStripMenuItem _trayItemClickThrough = null!;
    private WinForms.ToolStripMenuItem _trayItemStartup      = null!;

    // ── Click-through state ───────────────────────────────────────────────────
    private bool _clickThrough = false;

    // ── P/Invoke ─────────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_LAYERED     = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    // ── Body fill colours (placeholder ellipse) ───────────────────────────────
    private static readonly SolidColorBrush BrushNormal   = new(Color.FromArgb(0xCC, 0x7B, 0x5E, 0xA6));
    private static readonly SolidColorBrush BrushWarning  = new(Color.FromArgb(0xCC, 0xCC, 0x88, 0x33));
    private static readonly SolidColorBrush BrushCritical = new(Color.FromArgb(0xCC, 0xCC, 0x44, 0x33));

    // ─────────────────────────────────────────────────────────────────────────

    public PetOverlayWindow()
    {
        InitializeComponent();
        BuildTrayIcon();
        MouseLeftButtonDown += (_, _) => { if (!_clickThrough) DragMove(); };
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        _state = await PetPersistence.LoadAsync();
        PetEngine.Tick(_state, DateTime.UtcNow);

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

    /// <summary>
    /// Scans assets/sprites/ next to the exe for huffle_{state}_{nn}.png files.
    /// Loads all found frames into memory and activates the sprite Image element.
    /// Falls back silently to the placeholder ellipse if no sprites are present.
    /// </summary>
    private void LoadSprites()
    {
        string exeDir     = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
        string spritesDir = Path.Combine(exeDir, "assets", "sprites");

        if (!Directory.Exists(spritesDir)) return;

        string[] states =
        [
            // Core loop states
            "walk", "idle",
            // Need states
            "hungry", "bored", "dirty", "sad", "happy",
            // Interaction transients
            "eat", "clean", "study",
            // Bonus states (used when available)
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
                    bmp.CacheOption      = BitmapCacheOption.OnLoad; // release file handle
                    bmp.DecodePixelWidth = 128;                       // 2x upscale at decode time
                    bmp.EndInit();
                    bmp.Freeze();   // immutable → safe to share across render passes

                    frames.Add(bmp);
                }
                catch
                {
                    // Corrupt or unreadable frame — skip it
                }
            }

            if (frames.Count > 0)
            {
                _spriteFrames[state] = [.. frames];
                frameCounts[state]   = frames.Count;
            }
        }

        if (frameCounts.Count == 0) return;

        _animation = new AnimationService(frameCounts);

        // Switch UI from placeholder to sprite
        PetSprite.Visibility      = Visibility.Visible;
        PlaceholderGrid.Visibility = Visibility.Collapsed;

        // Display first frame immediately
        UpdateSpriteFrame();

        // Upgrade tray icon to use the actual idle sprite
        UpdateTrayIconFromSprite(spritesDir);
    }

    /// <summary>
    /// Replaces the placeholder tray icon with a 32×32 version of the idle sprite.
    /// Falls back silently if the file is missing or unreadable.
    /// </summary>
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

            var hIcon = scaled.GetHicon();
            var icon  = Drawing.Icon.FromHandle(hIcon);
            _trayIcon.Icon = icon;
        }
        catch
        {
            // Keep placeholder icon — not a fatal error
        }
    }

    /// <summary>Pushes the current animation frame to the Image element.</summary>
    private void UpdateSpriteFrame()
    {
        if (_animation is null) return;
        if (!_spriteFrames.TryGetValue(_animation.CurrentState, out var frames)) return;

        int idx = Math.Min(_animation.CurrentFrame, frames.Length - 1);
        PetSprite.Source = frames[idx];
    }

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

        _wander.Tick(delta);
        Left = _wander.X;
        Top  = _wander.Y;

        // Animation — advance frame and apply directional flip
        if (_animation is not null)
        {
            _animation.Tick(delta, _state, isMoving: !_wander.IsIdle);
            UpdateSpriteFrame();
            SpriteFlip.ScaleX = _wander.FacingLeft ? -1 : 1;
        }
    }

    private void OnNeedsTick(object? sender, EventArgs e)
    {
        PetEngine.Tick(_state, DateTime.UtcNow);
        RefreshPetAppearance();
        _ = PetPersistence.SaveAsync(_state);

        // Faint when any need bottoms out (with cooldown to avoid looping every tick)
        bool anyDepleted = _state.Hunger < 1f || _state.Hygiene < 1f || _state.Fun < 1f;
        if (anyDepleted && _animation is not null &&
            (DateTime.UtcNow - _lastFaintTime).TotalMinutes >= FaintCooldownMinutes)
        {
            _lastFaintTime = DateTime.UtcNow;
            _animation.TriggerTransient("faint");
        }
    }

    // ── Visual state ──────────────────────────────────────────────────────────

    /// <summary>
    /// Updates placeholder body colour + expression, and the hover tooltip.
    /// When sprites are loaded the placeholder is hidden, but we still update
    /// it so the fallback is always in sync.
    /// </summary>
    private void RefreshPetAppearance()
    {
        float minNeed = Math.Min(Math.Min(_state.Hunger, _state.Hygiene), _state.Fun);

        if (minNeed < 20f)
        {
            PetBody.Fill        = BrushCritical;
            PetSmile.Visibility = Visibility.Collapsed;
            PetFrown.Visibility = Visibility.Visible;
        }
        else if (minNeed < 40f)
        {
            PetBody.Fill        = BrushWarning;
            PetSmile.Visibility = Visibility.Visible;
            PetFrown.Visibility = Visibility.Collapsed;
        }
        else
        {
            PetBody.Fill        = BrushNormal;
            PetSmile.Visibility = Visibility.Visible;
            PetFrown.Visibility = Visibility.Collapsed;
        }

        string spriteState = _animation?.CurrentState ?? "placeholder";
        string reason = _animation?.LastTransitionReason ?? "none";

        NeedsText.Text =
            $"Hunger    {_state.Hunger,5:F0}/100\n" +
            $"Hygiene   {_state.Hygiene,5:F0}/100\n" +
            $"Fun       {_state.Fun,5:F0}/100\n" +
            $"Knowledge {_state.Knowledge,5:F0}/100\n" +
            $"State     {spriteState}\n" +
            $"Reason    {reason}";
    }

    // ── Interactions ──────────────────────────────────────────────────────────

    private enum Interaction { Feed, Play, Clean, Study }

    private void OnInteract(Interaction action)
    {
        // Wake the pet if it's sleeping so it reacts visually
        _animation?.WakeUp();

        switch (action)
        {
            case Interaction.Feed:
                PetEngine.Feed(_state);
                _animation?.TriggerTransient("eat");
                break;
            case Interaction.Play:
                PetEngine.Play(_state);
                // play animation pending revised sprite sheet
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

        // Celebrate if all needs are comfortably high — but only when no other
        // transient is already playing (eat/clean/study play first; the passive
        // "happy" state will follow naturally once they finish).
        bool allNeedsHigh = _state.Hunger > 70f && _state.Hygiene > 70f && _state.Fun > 70f;
        if (allNeedsHigh && _animation?.IsPlayingTransient == false)
            _animation.TriggerTransient("celebrating");
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
        _trayMenu.Items.Add("Show / Hide",            null, OnTrayToggleVisibility);
        _trayMenu.Items.Add(_trayItemClickThrough);
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
        _trayMenu.Items.Add("Feed",  null, (_, _) => OnInteract(Interaction.Feed));
        _trayMenu.Items.Add("Play",  null, (_, _) => OnInteract(Interaction.Play));
        _trayMenu.Items.Add("Clean", null, (_, _) => OnInteract(Interaction.Clean));
        _trayMenu.Items.Add("Study", null, (_, _) => OnInteract(Interaction.Study));
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
        _trayMenu.Items.Add(_trayItemStartup);
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit",                   null, OnTrayExit);

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
