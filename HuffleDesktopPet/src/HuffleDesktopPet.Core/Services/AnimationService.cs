using HuffleDesktopPet.Core.Models;

namespace HuffleDesktopPet.Core.Services;

/// <summary>
/// Frame-level animation state machine for the desktop pet.
/// Determines which animation is active and which frame to display.
/// No WPF dependency — fully unit-testable.
///
/// Priority order:
///   1. Transient (eat / clean / study / faint — plays once, then reverts)
///   2. Walk (while moving)
///   3. Sad  (any need below critical threshold)
///   4. Hungry / Dirty / Bored (whichever need is lowest)
///   5. Sleep (scheduled night/nap windows, or extended inactivity — unless woken)
///   6. Happy (all needs > 70 %)
///   7. Idle (default)
/// </summary>
public sealed class AnimationService
{
    // ── Thresholds ────────────────────────────────────────────────────────────
    private const float CriticalThreshold = 20f;
    private const float WarningThreshold  = 30f;
    private const float HappyThreshold    = 70f;

    /// <summary>Minutes of no movement before the pet nods off outside schedule.</summary>
    private const double InactivitySleepMinutes = 15.0;

    // ── FPS per animation state ───────────────────────────────────────────────
    private static double GetFps(string state) => state switch
    {
        "walk"        => 8.0,
        "idle"        => 3.0,
        "eat"         => 5.0,
        "clean"       => 5.0,
        "study"       => 4.0,
        "sleep"       => 1.5,   // slow breathing loop
        "faint"       => 4.0,
        "celebrating" => 7.0,
        "happy"       => 6.0,
        _             => 3.0,   // hungry / bored / dirty / sad / cautious / booing
    };

    // ── Internal state ────────────────────────────────────────────────────────
    private readonly Dictionary<string, int> _frameCounts;
    private string?  _transientState;
    private double   _elapsed;
    private double   _inactivitySeconds;
    private DateTime _wokenUntil = DateTime.MinValue;   // forced-awake deadline

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>Name of the animation currently playing (e.g. "walk", "idle", "eat").</summary>
    public string CurrentState { get; private set; } = "idle";

    /// <summary>Zero-based index of the frame to display within <see cref="CurrentState"/>.</summary>
    public int CurrentFrame { get; private set; } = 0;

    /// <summary>True while a transient (one-shot) animation is still playing.</summary>
    public bool IsPlayingTransient => _transientState is not null;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="frameCounts">
    /// Dictionary mapping animation state name → number of frames.
    /// States not present here are treated as single-frame.
    /// </param>
    public AnimationService(IReadOnlyDictionary<string, int> frameCounts)
    {
        _frameCounts = new Dictionary<string, int>(frameCounts);
    }

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Trigger a one-shot animation (e.g. "eat", "clean").
    /// The animation plays to completion; the state machine then reverts automatically.
    /// No-op if <paramref name="state"/> is not in the frame-count dictionary.
    /// </summary>
    public void TriggerTransient(string state)
    {
        if (!_frameCounts.ContainsKey(state)) return;

        _transientState = state;
        CurrentState    = state;
        CurrentFrame    = 0;
        _elapsed        = 0;
    }

    /// <summary>
    /// Wake the pet up for <paramref name="durationMinutes"/> minutes,
    /// overriding any scheduled sleep window.  Call this when the user
    /// interacts with the pet while it is sleeping.
    /// </summary>
    public void WakeUp(double durationMinutes = 5.0)
    {
        _inactivitySeconds = 0;
        _wokenUntil = DateTime.Now.AddMinutes(durationMinutes);
    }

    /// <summary>
    /// Advance the animation by <paramref name="deltaSeconds"/>.
    /// Call once per render tick (~30 fps from the wander timer).
    /// </summary>
    /// <param name="deltaSeconds">Seconds elapsed since last call.</param>
    /// <param name="petState">Current pet needs, used to pick passive animation.</param>
    /// <param name="isMoving">True when the pet is actively walking toward a target.</param>
    public void Tick(double deltaSeconds, PetState petState, bool isMoving)
    {
        if (deltaSeconds <= 0) return;

        // Track inactivity: reset whenever the pet is moving or a transient fires
        if (isMoving || _transientState is not null)
            _inactivitySeconds = 0;
        else
            _inactivitySeconds += deltaSeconds;

        // If not in a transient, switch to whatever the passive state should be
        if (_transientState is null)
        {
            bool forcedAwake = DateTime.Now < _wokenUntil;
            string target = ResolvePassiveState(petState, isMoving, _inactivitySeconds, forcedAwake);
            if (CurrentState != target)
            {
                CurrentState = target;
                CurrentFrame = 0;
                _elapsed     = 0;
            }
        }

        // Advance frame timer
        double frameInterval = 1.0 / GetFps(CurrentState);
        _elapsed += deltaSeconds;

        if (_elapsed >= frameInterval)
        {
            _elapsed -= frameInterval;

            int frameCount = FrameCount(CurrentState);
            CurrentFrame = (CurrentFrame + 1) % frameCount;

            // Transient finishes when it wraps back to frame 0
            if (_transientState is not null && CurrentFrame == 0)
                _transientState = null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private int FrameCount(string state) =>
        _frameCounts.TryGetValue(state, out int n) ? Math.Max(1, n) : 1;

    /// <summary>
    /// Determines the appropriate passive animation state.
    /// Sleep schedule: 22:00 – 08:00 (night), 12:00 – 13:00 (noon nap), 16:00 – 17:00 (afternoon nap).
    /// Extended inactivity (≥ 15 min) also triggers sleep when not forced awake.
    /// </summary>
    internal static string ResolvePassiveState(
        PetState state,
        bool isMoving,
        double inactivitySeconds = 0,
        bool forcedAwake = false,
        DateTime? localNow = null)
    {
        if (isMoving) return "walk";

        float minNeed = Math.Min(Math.Min(state.Hunger, state.Hygiene), state.Fun);

        if (minNeed < CriticalThreshold) return "sad";
        if (state.Hunger  < WarningThreshold) return "hungry";
        if (state.Hygiene < WarningThreshold) return "dirty";
        if (state.Fun     < WarningThreshold) return "bored";

        // Sleep check (skipped when user has manually woken the pet)
        if (!forcedAwake)
        {
            var now  = localNow ?? DateTime.Now;
            int hour = now.Hour;
            bool isScheduledSleep =
                hour >= 22 || hour < 8 ||          // night: 22:00 – 08:00
                (hour == 12) ||                     // noon nap: 12:00 – 13:00
                (hour == 16);                       // afternoon nap: 16:00 – 17:00
            bool isInactiveTooLong = inactivitySeconds >= InactivitySleepMinutes * 60;

            if (isScheduledSleep || isInactiveTooLong) return "sleep";
        }

        // Happy — all needs comfortably high
        if (minNeed > HappyThreshold &&
            state.Hunger  > HappyThreshold &&
            state.Hygiene > HappyThreshold &&
            state.Fun     > HappyThreshold)
            return "happy";

        return "idle";
    }
}
