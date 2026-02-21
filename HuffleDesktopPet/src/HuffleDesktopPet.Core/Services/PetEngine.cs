using HuffleDesktopPet.Core.Models;

namespace HuffleDesktopPet.Core.Services;

/// <summary>
/// Applies time-based decay and player interactions to pet needs.
/// Runs independently of UI — unit-testable.
/// </summary>
public static class PetEngine
{
    // Decay rates per real-time hour
    private const float HungerDecayPerHour    = 10f;
    private const float HygieneDecayPerHour   =  5f;
    private const float FunDecayPerHour       =  8f;
    private const float KnowledgeDecayPerHour =  2f;

    /// <summary>
    /// Maximum elapsed time applied in a single Tick (guards against clock skew,
    /// very long sleep/hibernate, or a corrupt LastUpdatedUtc timestamp).
    /// Anything beyond 72 hours still results in full depletion; this just
    /// prevents integer overflow or absurd calculation times.
    /// </summary>
    private const double MaxElapsedHours = 72.0;

    /// <summary>
    /// Advances the pet state forward in time.
    /// Updates <see cref="PetState.LastUpdatedUtc"/> after applying decay.
    /// </summary>
    /// <param name="state">The current pet state (mutated in place).</param>
    /// <param name="now">The current UTC time.</param>
    public static void Tick(PetState state, DateTime now)
    {
        if (now <= state.LastUpdatedUtc)
            return;

        double elapsedHours = Math.Min(
            (now - state.LastUpdatedUtc).TotalHours,
            MaxElapsedHours);

        state.Hunger    -= (float)(HungerDecayPerHour    * elapsedHours);
        state.Hygiene   -= (float)(HygieneDecayPerHour   * elapsedHours);
        state.Fun       -= (float)(FunDecayPerHour       * elapsedHours);
        state.Knowledge -= (float)(KnowledgeDecayPerHour * elapsedHours);

        state.Clamp();
        state.LastUpdatedUtc = now;
    }

    // ── Interactions ──────────────────────────────────────────────────────────

    /// <summary>
    /// Feed the pet: restores Hunger (+40), slightly reduces Hygiene (-5, eating is messy).
    /// </summary>
    public static void Feed(PetState state)
    {
        state.Hunger  += 40f;
        state.Hygiene -=  5f;
        state.Clamp();
    }

    /// <summary>
    /// Play with the pet: restores Fun (+35), burns Hunger (-8, playing takes energy).
    /// </summary>
    public static void Play(PetState state)
    {
        state.Fun    += 35f;
        state.Hunger -=  8f;
        state.Clamp();
    }

    /// <summary>
    /// Clean the pet: restores Hygiene (+50).
    /// </summary>
    public static void Clean(PetState state)
    {
        state.Hygiene += 50f;
        state.Clamp();
    }

    /// <summary>
    /// Teach/study with the pet: restores Knowledge (+25), slightly reduces Fun (-5).
    /// </summary>
    public static void Study(PetState state)
    {
        state.Knowledge += 25f;
        state.Fun       -=  5f;
        state.Clamp();
    }
}
