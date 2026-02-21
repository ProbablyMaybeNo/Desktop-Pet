namespace HuffleDesktopPet.Core.Services;

/// <summary>
/// Autonomous wander logic for the desktop pet.
/// Works entirely in pixel space. No WPF dependency — unit-testable.
///
/// Behaviour:
///   - Pet drifts toward a random target at <see cref="PixelsPerSecond"/> px/s.
///   - On arrival it idles for a random duration, then picks a new target.
///   - All positions are clamped to the supplied bounds.
/// </summary>
public sealed class WanderService
{
    // ── Tuning constants ──────────────────────────────────────────────────────

    /// <summary>Movement speed in pixels per second.</summary>
    public const double PixelsPerSecond = 55.0;

    /// <summary>Distance threshold (px) at which the pet is considered "arrived".</summary>
    private const double ArrivalThreshold = 6.0;

    /// <summary>Seconds the pet waits after arriving before choosing the next target.</summary>
    private const double MinIdleSeconds = 0.8;
    private const double MaxIdleSeconds = 3.5;

    /// <summary>Seconds of initial pause before the very first move.</summary>
    private const double InitialIdleSeconds = 0.5;

    // ── Internal state ────────────────────────────────────────────────────────

    private readonly Random _rng;
    private readonly double _minX, _minY, _maxX, _maxY;

    private double _targetX;
    private double _targetY;
    private double _idleRemaining;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>Current position of the pet window's top-left corner, in pixels.</summary>
    public double X { get; private set; }

    /// <summary>Current position of the pet window's top-left corner, in pixels.</summary>
    public double Y { get; private set; }

    /// <summary>True while the pet is resting between moves.</summary>
    public bool IsIdle => _idleRemaining > 0;

    /// <summary>True when the pet is moving left (useful for sprite flipping later).</summary>
    public bool FacingLeft { get; private set; }

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="startX">Initial X position (clamped to bounds).</param>
    /// <param name="startY">Initial Y position (clamped to bounds).</param>
    /// <param name="minX">Left bound (inclusive).</param>
    /// <param name="minY">Top bound (inclusive).</param>
    /// <param name="maxX">Right bound (inclusive, should be screen.Right - petWidth).</param>
    /// <param name="maxY">Bottom bound (inclusive, should be screen.Bottom - petHeight).</param>
    /// <param name="seed">Optional RNG seed for deterministic tests.</param>
    public WanderService(
        double startX, double startY,
        double minX,   double minY,
        double maxX,   double maxY,
        int?   seed = null)
    {
        _rng  = seed.HasValue ? new Random(seed.Value) : new Random();
        _minX = minX;
        _minY = minY;
        _maxX = maxX;
        _maxY = maxY;

        X        = Math.Clamp(startX, minX, maxX);
        Y        = Math.Clamp(startY, minY, maxY);
        _targetX = X;
        _targetY = Y;

        _idleRemaining = InitialIdleSeconds;  // brief pause before first move
    }

    // ── Tick ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Advance wander state by <paramref name="deltaSeconds"/>.
    /// Read <see cref="X"/> / <see cref="Y"/> after calling to reposition the window.
    /// </summary>
    public void Tick(double deltaSeconds)
    {
        if (deltaSeconds <= 0) return;

        // Idle phase — count down, then pick a new destination
        if (_idleRemaining > 0)
        {
            _idleRemaining -= deltaSeconds;
            if (_idleRemaining <= 0)
                PickNewTarget();
            return;
        }

        // Move phase — step toward target
        double dx   = _targetX - X;
        double dy   = _targetY - Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist <= ArrivalThreshold)
        {
            // Snap to target and start idle
            X = _targetX;
            Y = _targetY;
            _idleRemaining = MinIdleSeconds + _rng.NextDouble() * (MaxIdleSeconds - MinIdleSeconds);
            return;
        }

        double step = Math.Min(PixelsPerSecond * deltaSeconds, dist);
        FacingLeft = dx < 0;
        X += (dx / dist) * step;
        Y += (dy / dist) * step;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void PickNewTarget()
    {
        _targetX = _minX + _rng.NextDouble() * (_maxX - _minX);
        _targetY = _minY + _rng.NextDouble() * (_maxY - _minY);
    }

    /// <summary>
    /// Instantly relocate the pet (e.g. when restoring a saved position).
    /// Resets the current target to the new position and starts an idle pause.
    /// </summary>
    public void SetPosition(double x, double y)
    {
        X        = Math.Clamp(x, _minX, _maxX);
        Y        = Math.Clamp(y, _minY, _maxY);
        _targetX = X;
        _targetY = Y;
        _idleRemaining = InitialIdleSeconds;
    }
}
