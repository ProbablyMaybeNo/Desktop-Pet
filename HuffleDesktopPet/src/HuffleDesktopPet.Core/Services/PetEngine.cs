using HuffleDesktopPet.Core.Models;

namespace HuffleDesktopPet.Core.Services;

/// <summary>
/// Bit-flags returned by <see cref="PetEngine.Tick"/> so the UI layer can
/// react to events (e.g. trigger a faint animation) without polling.
/// </summary>
[Flags]
public enum PetTickEvents
{
    None                 = 0,
    /// <summary>Tired was ≥ Stage2 for 24 h with &lt;8 h sleep → forced 8-h recovery.</summary>
    TiredFaintTriggered  = 1 << 0,
    /// <summary>Hunger was ≥ Stage3 for 6 h without feeding.</summary>
    HungerFaintTriggered = 1 << 1,
}

/// <summary>
/// Applies time-based need fills, sleep accounting, and faint detection to a
/// <see cref="PetState"/>.  Stateless — no fields; call once per minute tick.
/// </summary>
public static class PetEngine
{
    private const double MaxElapsedMinutes = 72.0 * 60;  // cap at 72 h to avoid skew

    // ── Minute Tick ───────────────────────────────────────────────────────────

    /// <summary>
    /// Advances the pet state forward in time.
    /// Call once per minute (from the UI's 60-second timer).
    /// </summary>
    /// <param name="state">Pet state mutated in place.</param>
    /// <param name="nowUtc">Current UTC time.</param>
    /// <returns>Any faint events that fired during this tick.</returns>
    public static PetTickEvents Tick(PetState state, DateTime nowUtc)
    {
        if (nowUtc <= state.LastUpdatedUtc) return PetTickEvents.None;

        double elapsedMin = Math.Min(
            (nowUtc - state.LastUpdatedUtc).TotalMinutes,
            MaxElapsedMinutes);

        DateTime nowLocal = nowUtc.ToLocalTime();
        float    elapsed  = (float)elapsedMin;

        // ── Sleep-log housekeeping ────────────────────────────────────────────
        state.PruneSleepLog();

        // ── Forced-sleep end check ────────────────────────────────────────────
        if (state.IsForcedSleep && state.ForcedSleepEndsAt.HasValue &&
            nowUtc >= state.ForcedSleepEndsAt.Value)
        {
            state.IsForcedSleep     = false;
            state.IsSleeping        = false;
            state.IsFainting        = false;
            state.ForcedSleepEndsAt = null;
            state.Tired             = 0f;
            state.TiredAbove60Since = null;
        }

        // ── Sleep window transitions ──────────────────────────────────────────
        bool inWindow = NeedConfig.IsInSleepWindow(nowLocal);

        if (!inWindow)
        {
            // Left window: reset early-wake flag so the next window auto-enters
            state.WokeEarlyInWindow = false;
            if (state.IsSleeping && !state.IsForcedSleep)
                state.IsSleeping = false;
        }
        else if (!state.WokeEarlyInWindow && !state.IsSleeping)
        {
            state.IsSleeping = true;   // auto-enter scheduled sleep
        }

        // ── Sleep-minute logging ──────────────────────────────────────────────
        if (state.IsSleeping)
        {
            int toLog = (int)Math.Floor(elapsedMin);
            for (int i = 0; i < toLog; i++)
                state.SleepMinuteLog.Add(nowUtc.AddMinutes(-(toLog - i)));
        }

        // ── Need fills ────────────────────────────────────────────────────────
        if (!state.IsSleeping)
        {
            state.Hunger += NeedConfig.HungerTickPerMin * elapsed;
            state.Dirty  += NeedConfig.DirtyTickPerMin  * elapsed;

            // Boredom — full rate only after the idle grace window expires
            double sinceInteraction = state.LastInteractionUtc == DateTime.MinValue
                ? double.MaxValue
                : (nowUtc - state.LastInteractionUtc).TotalMinutes;
            float boreRate = sinceInteraction > NeedConfig.BoredomIdleWindowMinutes
                ? NeedConfig.BoredomTickPerMin
                : NeedConfig.BoredomTickPerMin * NeedConfig.BoredomTickSlowMultiplier;
            state.Bored += boreRate * elapsed;

            // Tired — baseline plus sleep-deficit modifier
            int   sleepMin       = state.GetSleepMinutesLast24h();
            float deficitFrac    = Math.Max(0f, 1f - sleepMin / NeedConfig.SleepRequiredMinutes);
            float tiredRate      = NeedConfig.TiredTickPerMin
                                 + deficitFrac * NeedConfig.TiredTickPerMinDeficit;
            state.Tired += tiredRate * elapsed;
        }
        else
        {
            // While sleeping, tired recovers
            state.Tired -= NeedConfig.TiredSleepRecoveryPerMin * elapsed;
        }

        // ── Sad (derived) ─────────────────────────────────────────────────────
        int needsAboveStage2 = CountNeedsAbove(state, NeedConfig.Stage2Threshold);
        bool anyAboveStage3  = state.Hunger >= NeedConfig.Stage3Threshold
                            || state.Tired  >= NeedConfig.Stage3Threshold
                            || state.Dirty  >= NeedConfig.Stage3Threshold
                            || state.Bored  >= NeedConfig.Stage3Threshold;

        if (needsAboveStage2 >= 2)
            state.Sad += NeedConfig.SadTickMultiNeedsPerMin * elapsed;
        else if (anyAboveStage3)
            state.Sad += NeedConfig.SadTickCriticalPerMin   * elapsed;
        else
            state.Sad -= NeedConfig.SadDecayPerMin          * elapsed;

        state.Clamp();

        // ── Faint conditions ──────────────────────────────────────────────────
        var events = PetTickEvents.None;

        // Tired faint: tired ≥ Stage2 for 24 h with insufficient sleep
        if (!state.IsForcedSleep && state.Tired >= NeedConfig.TiredFaintThreshold)
        {
            state.TiredAbove60Since ??= nowUtc;
            double minutesAbove = (nowUtc - state.TiredAbove60Since.Value).TotalMinutes;
            if (minutesAbove >= NeedConfig.TiredFaintMinutes &&
                state.GetSleepMinutesLast24h() < NeedConfig.SleepRequiredMinutes)
            {
                state.IsForcedSleep     = true;
                state.IsSleeping        = true;
                state.IsFainting        = true;
                state.ForcedSleepEndsAt = nowUtc.AddMinutes(NeedConfig.TiredForcedSleepMinutes);
                events |= PetTickEvents.TiredFaintTriggered;
            }
        }
        else if (!state.IsForcedSleep)
        {
            state.TiredAbove60Since = null;
        }

        // Hunger faint: hunger ≥ Stage3 for 6 h without feeding
        if (state.Hunger >= NeedConfig.HungerFaintThreshold)
        {
            state.HungerAbove90Since ??= nowUtc;
            double minutesAbove = (nowUtc - state.HungerAbove90Since.Value).TotalMinutes;
            if (minutesAbove >= NeedConfig.HungerFaintMinutes && !state.IsHungerFainted)
            {
                state.IsHungerFainted = true;
                state.IsFainting      = true;
                events |= PetTickEvents.HungerFaintTriggered;
            }
        }
        else
        {
            // Hunger recovered (user fed pet enough to drop below Stage3)
            state.HungerAbove90Since = null;
            if (state.IsHungerFainted)
            {
                state.IsHungerFainted = false;
                state.IsFainting      = state.IsForcedSleep;  // keep fainting only if forced-sleep
            }
        }

        state.LastUpdatedUtc = nowUtc;
        return events;
    }

    // ── Interactions ──────────────────────────────────────────────────────────

    /// <summary>Feed the pet: reduces Hunger, slightly increases Dirty, reduces Sad.</summary>
    public static void Feed(PetState state)
    {
        state.Hunger -= NeedConfig.FeedHungerReduce;
        state.Dirty  += NeedConfig.FeedDirtyIncrease;
        state.Sad    -= NeedConfig.FeedSadReduce;
        // Feeding clears a hunger-triggered faint
        state.IsHungerFainted    = false;
        state.IsFainting         = state.IsForcedSleep;   // faint persists only for forced sleep
        state.HungerAbove90Since = null;
        state.LastInteractionUtc = DateTime.UtcNow;
        state.Clamp();
    }

    /// <summary>Play with the pet: reduces Bored and Sad, slightly increases Hunger.</summary>
    public static void Play(PetState state)
    {
        state.Bored  -= NeedConfig.PlayBoredomReduce;
        state.Sad    -= NeedConfig.PlaySadReduce;
        state.Hunger += NeedConfig.PlayHungerIncrease;
        state.LastInteractionUtc = DateTime.UtcNow;
        state.Clamp();
    }

    /// <summary>Clean the pet: reduces Dirty and slightly reduces Sad.</summary>
    public static void Clean(PetState state)
    {
        state.Dirty -= NeedConfig.CleanDirtyReduce;
        state.Sad   -= NeedConfig.CleanSadReduce;
        state.LastInteractionUtc = DateTime.UtcNow;
        state.Clamp();
    }

    /// <summary>Study: mentally stimulating — reduces Bored, slight Hunger cost.</summary>
    public static void Study(PetState state)
    {
        state.Bored  -= 30f;
        state.Hunger +=  5f;
        state.LastInteractionUtc = DateTime.UtcNow;
        state.Clamp();
    }

    /// <summary>
    /// Poke: direct click on the pet body.
    /// Records interaction time (so boredom resets its grace window).
    /// Waking from sleep is handled by the caller before this is called.
    /// </summary>
    public static void Poke(PetState state)
    {
        state.LastInteractionUtc = DateTime.UtcNow;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int CountNeedsAbove(PetState state, float threshold)
    {
        int n = 0;
        if (state.Hunger >= threshold) n++;
        if (state.Tired  >= threshold) n++;
        if (state.Dirty  >= threshold) n++;
        if (state.Bored  >= threshold) n++;
        return n;
    }
}
