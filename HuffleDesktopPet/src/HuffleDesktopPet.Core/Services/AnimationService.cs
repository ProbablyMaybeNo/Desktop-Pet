using HuffleDesktopPet.Core.Models;

namespace HuffleDesktopPet.Core.Services;

/// <summary>
/// Frame-level animation state machine for the desktop pet.
/// Determines which animation state is active and which frame index to display.
/// No WPF dependency — fully unit-testable.
///
/// Priority order (highest wins):
///   1. Faint animation (uninterruptible — set via TriggerFaint / IsFainting flag)
///   2. Hunger-faint sustained state (IsFainting + IsHungerFainted — until pet is fed)
///   3. Forced sleep (IsForcedSleep — uninterruptible 8-h recovery)
///   4. Interaction transient: eat / clean / play / study / poked
///   5. Scheduled sleep (IsSleeping)
///   6. SAD (sad bar ≥ threshold OR 2+ needs ≥ Stage2; uses hysteresis)
///   7. Need prompt (periodic short-lived interrupt from hungry/tired/dirty/bored)
///   8. Walk (pet is moving)
///   9. Happy (all needs and sad below threshold)
///  10. Idle (default)
/// </summary>
public sealed class AnimationService
{
    // ── FPS per state ─────────────────────────────────────────────────────────
    private static double GetFps(string state) => state switch
    {
        "walk"        => 8.0,
        "idle"        => 3.0,
        "eat"         => 5.0,
        "clean"       => 5.0,
        "study"       => 4.0,
        "sleep"       => 1.5,
        "faint"       => 4.0,
        "celebrating" => 7.0,
        "happy"       => 6.0,
        "poked"       => 8.0,
        "playing"     => 6.0,
        _             => 3.0,   // hungry / tired / bored / dirty / sad / cautious / booing
    };

    // ── Internal state ────────────────────────────────────────────────────────
    private readonly Dictionary<string, int>    _frameCounts;
    private readonly Dictionary<string, double> _lastPromptTime = new();  // seconds epoch

    private string? _transientState;
    private bool    _faintLocked;       // faint transient cannot be overridden by other transients
    private double  _elapsed;           // seconds elapsed in current frame

    private string? _needPromptState;   // which need is currently "prompting" (e.g. "hungry")
    private double  _needPromptRemaining;   // seconds left in the current prompt

    private bool    _sadActive;         // hysteresis latch for SAD state

    private double  _clockSeconds;      // monotonic clock used for prompt cooldowns

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>Name of the animation currently playing.</summary>
    public string CurrentState { get; private set; } = "idle";

    /// <summary>Zero-based frame index within <see cref="CurrentState"/>.</summary>
    public int CurrentFrame { get; private set; } = 0;

    /// <summary>True while a transient (one-shot) animation is still playing.</summary>
    public bool IsPlayingTransient => _transientState is not null;

    /// <summary>True while the faint-locked uninterruptible animation is playing.</summary>
    public bool IsFaintLocked => _faintLocked;

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <param name="frameCounts">
    /// Maps animation state name → number of frames.
    /// States absent here default to a single frame.
    /// </param>
    public AnimationService(IReadOnlyDictionary<string, int> frameCounts)
    {
        _frameCounts = new Dictionary<string, int>(frameCounts);
    }

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a one-shot interaction animation (eat / clean / study / poked / celebrating).
    /// No-op while a faint-locked animation is playing.
    /// No-op if <paramref name="state"/> is not in the frame-count dictionary.
    /// </summary>
    public void TriggerTransient(string state)
    {
        if (_faintLocked) return;
        if (!_frameCounts.ContainsKey(state)) return;

        _transientState   = state;
        CurrentState      = state;
        CurrentFrame      = 0;
        _elapsed          = 0;
        _needPromptState  = null;   // clear any active need prompt
    }

    /// <summary>
    /// Starts the faint collapse animation.
    /// This is uninterruptible: no other transient can override it until it completes.
    /// After the animation finishes the priority stack will pick "sleep" (if IsForcedSleep)
    /// or "idle"/"faint" based on PetState flags.
    /// </summary>
    public void TriggerFaint()
    {
        if (!_frameCounts.ContainsKey("faint")) return;

        _faintLocked     = true;
        _transientState  = "faint";
        CurrentState     = "faint";
        CurrentFrame     = 0;
        _elapsed         = 0;
        _needPromptState = null;
    }

    /// <summary>
    /// Resets the need-prompt state and clears any active prompt,
    /// so the pet reacts immediately to user input rather than finishing the prompt.
    /// </summary>
    public void WakeUp()
    {
        _needPromptState     = null;
        _needPromptRemaining = 0;
    }

    /// <summary>
    /// Advances the animation by <paramref name="deltaSeconds"/>.
    /// Call once per render tick (~30 fps).
    /// </summary>
    public void Tick(double deltaSeconds, PetState petState, bool isMoving)
    {
        if (deltaSeconds <= 0) return;

        _clockSeconds += deltaSeconds;

        // ── Advance need-prompt countdown ─────────────────────────────────────
        if (_needPromptState is not null)
        {
            _needPromptRemaining -= deltaSeconds;
            if (_needPromptRemaining <= 0)
                _needPromptState = null;
        }

        // ── Update SAD hysteresis ─────────────────────────────────────────────
        UpdateSadHysteresis(petState);

        // ── Determine current animation state via priority ────────────────────
        string target = ResolveState(petState, isMoving);

        if (CurrentState != target)
        {
            CurrentState = target;
            CurrentFrame = 0;
            _elapsed     = 0;
        }

        // ── Advance frame timer ───────────────────────────────────────────────
        double interval = 1.0 / GetFps(CurrentState);
        _elapsed += deltaSeconds;

        if (_elapsed >= interval)
        {
            _elapsed -= interval;

            int count = FrameCount(CurrentState);
            CurrentFrame = (CurrentFrame + 1) % count;

            // Transient ends when it wraps back to frame 0
            if (_transientState is not null && CurrentFrame == 0)
            {
                _transientState = null;
                _faintLocked    = false;
            }
        }

        // ── Check need prompts when in an interruptible base state ────────────
        // (Only when no higher-priority overrides are active)
        bool canPrompt = _transientState is null
                      && !petState.IsFainting
                      && !petState.IsSleeping
                      && !_sadActive
                      && _needPromptState is null
                      && (CurrentState is "idle" or "walk");

        if (canPrompt)
            CheckNeedPrompts(petState);
    }

    // ── Priority stack ────────────────────────────────────────────────────────

    private string ResolveState(PetState petState, bool isMoving)
    {
        // 1. Faint transient (faint-locked — in-progress collapse animation)
        if (_faintLocked && _transientState == "faint") return "faint";

        // 2. Hunger-faint sustained (pet is down until fed)
        if (petState.IsHungerFainted) return "faint";

        // 3. Forced recovery sleep (tired faint, uninterruptible)
        if (petState.IsForcedSleep) return "sleep";

        // 4. Interaction transient (eat / clean / study / poked / celebrating)
        if (_transientState is not null) return _transientState;

        // 5. Scheduled sleep (interruptible by poke)
        if (petState.IsSleeping) return "sleep";

        // 6. SAD
        if (_sadActive) return "sad";

        // 7. Need prompt (periodic short interruption)
        if (_needPromptState is not null) return _needPromptState;

        // 8. Walk
        if (isMoving) return "walk";

        // 9. Happy
        if (IsHappy(petState)) return "happy";

        // 10. Idle (default)
        return "idle";
    }

    // ── Sad hysteresis ────────────────────────────────────────────────────────

    private void UpdateSadHysteresis(PetState petState)
    {
        int needsAboveStage2 = CountNeedsAbove(petState, NeedConfig.Stage2Threshold);

        if (!_sadActive)
        {
            // Activate when sad bar crosses trigger threshold OR 2+ needs are bad
            if (petState.Sad >= NeedConfig.SadActiveThreshold || needsAboveStage2 >= 2)
                _sadActive = true;
        }
        else
        {
            // Clear only when both conditions are resolved (hysteresis)
            if (petState.Sad <= NeedConfig.SadClearThreshold && needsAboveStage2 < 2)
                _sadActive = false;
        }
    }

    // ── Need prompts ──────────────────────────────────────────────────────────

    private void CheckNeedPrompts(PetState petState)
    {
        // Check all promtable needs sorted by severity (highest value = most urgent)
        var candidates = new[]
        {
            ("hungry", petState.Hunger),
            ("tired",  petState.Tired),
            ("dirty",  petState.Dirty),
            ("bored",  petState.Bored),
        };

        foreach (var (needName, value) in candidates.OrderByDescending(n => n.Item2))
        {
            int stage = NeedStage(value);
            if (stage == 0) break;   // sorted descending; nothing below this can qualify

            double cooldown = stage switch
            {
                1 => NeedConfig.Stage1PromptCooldownSec,
                2 => NeedConfig.Stage2PromptCooldownSec,
                _ => NeedConfig.Stage3PromptCooldownSec,
            };

            double lastPrompt = _lastPromptTime.TryGetValue(needName, out double t) ? t : double.MinValue;
            if ((_clockSeconds - lastPrompt) >= cooldown && _frameCounts.ContainsKey(needName))
            {
                _needPromptState        = needName;
                _needPromptRemaining    = NeedConfig.PromptDurationSec;
                _lastPromptTime[needName] = _clockSeconds;
                return;   // one prompt at a time
            }
        }
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static bool IsHappy(PetState petState) =>
        petState.Hunger < NeedConfig.HappyNeedMax &&
        petState.Tired  < NeedConfig.HappyNeedMax &&
        petState.Dirty  < NeedConfig.HappyNeedMax &&
        petState.Bored  < NeedConfig.HappyNeedMax &&
        petState.Sad    < NeedConfig.HappySadMax;

    private static int NeedStage(float value) => value switch
    {
        >= NeedConfig.Stage3Threshold => 3,
        >= NeedConfig.Stage2Threshold => 2,
        >= NeedConfig.Stage1Threshold => 1,
        _ => 0,
    };

    private static int CountNeedsAbove(PetState state, float threshold)
    {
        int n = 0;
        if (state.Hunger >= threshold) n++;
        if (state.Tired  >= threshold) n++;
        if (state.Dirty  >= threshold) n++;
        if (state.Bored  >= threshold) n++;
        return n;
    }

    private int FrameCount(string state) =>
        _frameCounts.TryGetValue(state, out int n) ? Math.Max(1, n) : 1;

    // ── Internal test surface ─────────────────────────────────────────────────

    /// <summary>Exposed for unit tests only — returns the active need prompt or null.</summary>
    internal string? CurrentNeedPrompt => _needPromptState;

    /// <summary>Exposed for unit tests only — returns whether sad-state is latched on.</summary>
    internal bool SadActive => _sadActive;
}
