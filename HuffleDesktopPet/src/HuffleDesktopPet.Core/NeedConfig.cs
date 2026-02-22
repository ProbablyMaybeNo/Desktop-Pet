namespace HuffleDesktopPet.Core;

/// <summary>
/// All tunable constants for the pet's need simulation.
/// Edit values here to re-balance without touching game logic.
/// All "bar" values are in the range 0–100 where 0 = satisfied and 100 = critical.
/// </summary>
public static class NeedConfig
{
    // ── Stage thresholds (bar value; 0 = best, 100 = worst) ──────────────────
    public const float Stage1Threshold = 30f;   // 30–59: occasional need prompt
    public const float Stage2Threshold = 60f;   // 60–89: frequent prompt + mood penalty
    public const float Stage3Threshold = 90f;   // 90–100: persistent / critical

    // ── Sad state hysteresis ──────────────────────────────────────────────────
    /// <summary>Sad transitions to ACTIVE when sad bar crosses this.</summary>
    public const float SadActiveThreshold = 60f;
    /// <summary>Sad transitions back to INACTIVE only when it falls below this.</summary>
    public const float SadClearThreshold  = 40f;

    // ── Happy state (all needs must be BELOW these values) ───────────────────
    public const float HappyNeedMax = 30f;
    public const float HappySadMax  = 30f;

    // ── Need tick rates (per minute while awake) ──────────────────────────────
    public const float HungerTickPerMin          = 0.10f;   // +1 per 10 min
    public const float DirtyTickPerMin           = 0.05f;   // +1 per 20 min
    public const float BoredomTickPerMin         = 1.00f;   // +1/min after idle window
    public const float BoredomTickSlowMultiplier = 0.2f;    // fraction while recently active
    public const double BoredomIdleWindowMinutes = 30.0;    // minutes of grace before full rate

    // ── Tired / sleep ─────────────────────────────────────────────────────────
    /// <summary>Baseline tired increase per minute while awake.</summary>
    public const float TiredTickPerMin          = 0.25f;   // +1 per 4 min baseline
    /// <summary>Extra tired per minute, scaled by sleep-deficit fraction.</summary>
    public const float TiredTickPerMinDeficit   = 1.00f;
    /// <summary>Tired decrease per minute while sleeping.</summary>
    public const float TiredSleepRecoveryPerMin = 2.00f;
    /// <summary>Sleep minutes target per 24-hour window.</summary>
    public const float SleepRequiredMinutes     = 480f;    // 8 hours

    // ── Sad (derived + slow-burn) ─────────────────────────────────────────────
    public const float SadTickMultiNeedsPerMin  = 1.00f;  // per min when 2+ needs ≥ Stage2
    public const float SadTickCriticalPerMin    = 0.30f;  // per min when 1 need ≥ Stage3
    public const float SadDecayPerMin           = 0.50f;  // per min when needs improving
    public const float SadJoyFromPlay           = 10f;    // instant reduction from Play

    // ── Faint conditions ──────────────────────────────────────────────────────
    /// <summary>Tired must be at or above this for the 24-h faint rule to start ticking.</summary>
    public const float  TiredFaintThreshold     = 60f;
    /// <summary>Minutes tired must be ≥ TiredFaintThreshold with insufficient sleep to trigger faint.</summary>
    public const double TiredFaintMinutes       = 24 * 60;   // 24 hours
    /// <summary>Duration of forced recovery sleep after tired faint.</summary>
    public const double TiredForcedSleepMinutes = 480;        // 8 hours

    /// <summary>Hunger must be at or above this before the 6-h faint rule starts.</summary>
    public const float  HungerFaintThreshold    = 90f;
    /// <summary>Minutes hunger must be ≥ HungerFaintThreshold without feeding to trigger faint.</summary>
    public const double HungerFaintMinutes      = 360;        // 6 hours

    // ── Need-prompt cooldowns (seconds) ───────────────────────────────────────
    public const double Stage1PromptCooldownSec = 5 * 60;   // 5 minutes
    public const double Stage2PromptCooldownSec = 2 * 60;   // 2 minutes
    public const double Stage3PromptCooldownSec = 30;        // 30 seconds
    /// <summary>How long a need-prompt interruption lasts.</summary>
    public const double PromptDurationSec       = 4.0;

    // ── Interaction effects ───────────────────────────────────────────────────
    public const float FeedHungerReduce  = 40f;
    public const float FeedDirtyIncrease =  5f;
    public const float FeedSadReduce     =  5f;

    public const float PlayBoredomReduce  = 70f;
    public const float PlaySadReduce      = 10f;
    public const float PlayHungerIncrease =  5f;

    public const float CleanDirtyReduce  = 60f;
    public const float CleanSadReduce    =  5f;

    // ── Sleep schedule (local time) ───────────────────────────────────────────
    // Night:          22:00 – 08:00 (wraps midnight)
    // Noon nap:       12:00 – 13:00
    // Afternoon nap:  16:00 – 17:00
    public static bool IsInSleepWindow(DateTime localNow)
    {
        int h = localNow.Hour;
        return h >= 22 || h < 8 || h == 12 || h == 16;
    }
}
