namespace HuffleDesktopPet.Core.Models;

/// <summary>
/// Full persisted state of the desktop pet.
///
/// All need values are 0–100.  0 = best (satisfied); 100 = worst (critical).
/// This is the OPPOSITE of the v1 schema (where 100 = full/great).
/// StateVersion is checked on load; stale saves are discarded.
/// </summary>
public sealed class PetState
{
    // ── Schema version ────────────────────────────────────────────────────────
    /// <summary>Increment whenever the shape changes incompatibly so stale saves reset cleanly.</summary>
    public int StateVersion { get; set; } = 2;

    // ── Needs (0 = satisfied; 100 = critical) ─────────────────────────────────
    public float Hunger { get; set; } = 0f;   // 0 = full;         100 = starving
    public float Tired  { get; set; } = 0f;   // 0 = well rested;  100 = exhausted
    public float Dirty  { get; set; } = 0f;   // 0 = clean;        100 = filthy
    public float Bored  { get; set; } = 0f;   // 0 = entertained;  100 = very bored
    public float Sad    { get; set; } = 0f;   // 0 = content;      100 = miserable (derived)

    // ── Sleep state ───────────────────────────────────────────────────────────
    /// <summary>UTC timestamps of each minute the pet spent sleeping (rolling 24 h window).</summary>
    public List<DateTime> SleepMinuteLog { get; set; } = new();
    /// <summary>True while the pet is in a scheduled or forced sleep window.</summary>
    public bool IsSleeping             { get; set; } = false;
    /// <summary>True when the user woke the pet early; prevents re-entry until the next window.</summary>
    public bool WokeEarlyInWindow      { get; set; } = false;
    /// <summary>True during forced recovery sleep (tired faint) — cannot be interrupted by the user.</summary>
    public bool IsForcedSleep          { get; set; } = false;
    /// <summary>UTC time when the forced recovery sleep ends (null when not in forced sleep).</summary>
    public DateTime? ForcedSleepEndsAt { get; set; } = null;

    // ── Faint tracking ────────────────────────────────────────────────────────
    /// <summary>UTC when tired first crossed the Stage2 threshold (used for 24-h faint rule).</summary>
    public DateTime? TiredAbove60Since  { get; set; } = null;
    /// <summary>UTC when hunger first crossed the Stage3 threshold (used for 6-h faint rule).</summary>
    public DateTime? HungerAbove90Since { get; set; } = null;
    /// <summary>True while a faint animation / forced recovery is active.</summary>
    public bool IsFainting             { get; set; } = false;
    /// <summary>True during a hunger-triggered faint; requires the user to feed the pet to clear.</summary>
    public bool IsHungerFainted        { get; set; } = false;

    // ── Interaction timing ────────────────────────────────────────────────────
    /// <summary>UTC of the most recent user interaction (Feed/Play/Clean/Study/Poke).</summary>
    public DateTime LastInteractionUtc { get; set; } = DateTime.MinValue;

    // ── Position + meta ───────────────────────────────────────────────────────
    public double   PositionX      { get; set; } = 0.5;
    public double   PositionY      { get; set; } = 0.8;
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Clamps all need values to [0, 100].</summary>
    public void Clamp()
    {
        Hunger = Math.Clamp(Hunger, 0f, 100f);
        Tired  = Math.Clamp(Tired,  0f, 100f);
        Dirty  = Math.Clamp(Dirty,  0f, 100f);
        Bored  = Math.Clamp(Bored,  0f, 100f);
        Sad    = Math.Clamp(Sad,    0f, 100f);
    }

    /// <summary>Returns the count of minutes the pet slept in the last 24 hours.</summary>
    public int GetSleepMinutesLast24h()
    {
        PruneSleepLog();
        return SleepMinuteLog.Count;
    }

    /// <summary>Removes log entries older than 24 hours.</summary>
    public void PruneSleepLog()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        SleepMinuteLog.RemoveAll(t => t < cutoff);
    }
}
