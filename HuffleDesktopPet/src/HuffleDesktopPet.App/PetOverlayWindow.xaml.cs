using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
/// </summary>
public partial class PetOverlayWindow : Window
{
    // ── State ─────────────────────────────────────────────────────────────────
    private PetState      _state   = new();
    private WanderService _wander  = null!;

    // ── Timers ────────────────────────────────────────────────────────────────
    private DispatcherTimer _wanderTimer = null!;   // ~30 fps
    private DispatcherTimer _needsTimer  = null!;   // every 60 s

    // ── Tray ──────────────────────────────────────────────────────────────────
    private WinForms.NotifyIcon   _trayIcon   = null!;
    private WinForms.ContextMenuStrip _trayMenu = null!;

    // ── Click-through state ───────────────────────────────────────────────────
    private bool _clickThrough = false;

    // ── P/Invoke ─────────────────────────────────────────────────────────────
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);
    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_LAYERED     = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;

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

        // Load persisted state and catch up any time-away decay
        _state = await PetPersistence.LoadAsync();
        PetEngine.Tick(_state, DateTime.UtcNow);

        // Derive wander bounds from primary work area (minus pet window size)
        var area = SystemParameters.WorkArea;
        double maxX = area.Right  - Width;
        double maxY = area.Bottom - Height;

        // Restore saved position (fractional → pixel)
        double startX = _state.PositionX * maxX;
        double startY = _state.PositionY * maxY;

        _wander = new WanderService(
            startX, startY,
            minX: area.Left, minY: area.Top,
            maxX: maxX,      maxY: maxY);

        Left = _wander.X;
        Top  = _wander.Y;

        StartTimers();
        RefreshNeedsTooltip();
    }

    protected override async void OnClosed(EventArgs e)
    {
        StopTimers();
        DisposeTray();

        // Persist fractional position
        var area = SystemParameters.WorkArea;
        double maxX = Math.Max(1, area.Right  - Width);
        double maxY = Math.Max(1, area.Bottom - Height);
        _state.PositionX = Left / maxX;
        _state.PositionY = Top  / maxY;

        await PetPersistence.SaveAsync(_state);
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    // ── Timers ────────────────────────────────────────────────────────────────

    private void StartTimers()
    {
        // Wander timer — ~33 ms ≈ 30 fps
        _wanderTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _wanderTimer.Tick += OnWanderTick;
        _wanderTimer.Start();

        // Needs/save timer — every 60 seconds
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
        var now     = DateTime.UtcNow;
        double delta = (now - _lastWanderTick).TotalSeconds;
        _lastWanderTick = now;

        _wander.Tick(delta);
        Left = _wander.X;
        Top  = _wander.Y;
    }

    private void OnNeedsTick(object? sender, EventArgs e)
    {
        PetEngine.Tick(_state, DateTime.UtcNow);
        RefreshNeedsTooltip();
        // Fire-and-forget auto-save (no await in event handler)
        _ = PetPersistence.SaveAsync(_state);
    }

    // ── Needs tooltip ─────────────────────────────────────────────────────────

    private void RefreshNeedsTooltip()
    {
        NeedsText.Text =
            $"Hunger    {_state.Hunger,5:F0}/100\n" +
            $"Hygiene   {_state.Hygiene,5:F0}/100\n" +
            $"Fun       {_state.Fun,5:F0}/100\n" +
            $"Knowledge {_state.Knowledge,5:F0}/100";
    }

    // ── Tray icon ─────────────────────────────────────────────────────────────

    private void BuildTrayIcon()
    {
        _trayMenu = new WinForms.ContextMenuStrip();
        _trayMenu.Items.Add("Show / Hide",         null, OnTrayToggleVisibility);
        _trayMenu.Items.Add("Click-through: OFF",  null, OnTrayToggleClickThrough);
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit",                null, OnTrayExit);

        _trayIcon = new WinForms.NotifyIcon
        {
            Text             = "HuffleDesktopPet — right-click for options",
            Icon             = MakePlaceholderIcon(),
            ContextMenuStrip = _trayMenu,
            Visible          = true,
        };

        _trayIcon.DoubleClick += OnTrayToggleVisibility;
    }

    /// <summary>Generates a tiny purple circle icon in memory — no .ico file required.</summary>
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

        var hwnd   = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

        if (_clickThrough)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);
            ((WinForms.ToolStripMenuItem)_trayMenu.Items[1]).Text = "Click-through: ON";
        }
        else
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
            ((WinForms.ToolStripMenuItem)_trayMenu.Items[1]).Text = "Click-through: OFF";
        }
    }

    private void OnTrayExit(object? sender, EventArgs e) => Close();
}
