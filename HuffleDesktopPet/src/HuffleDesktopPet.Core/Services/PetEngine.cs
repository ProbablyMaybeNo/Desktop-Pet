using HuffleDesktopPet.Core.Models;

namespace HuffleDesktopPet.Core.Services;

/// <summary>
/// Applies time-based decay to pet needs.
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
    /// Advances the pet state forward in time.
    /// Updates <see cref="PetState.LastUpdatedUtc"/> after applying decay.
    /// </summary>
    /// <param name="state">The current pet state (mutated in place).</param>
    /// <param name="now">The current UTC time.</param>
    public static void Tick(PetState state, DateTime now)
    {
        if (now <= state.LastUpdatedUtc)
            return;

        double elapsedHours = (now - state.LastUpdatedUtc).TotalHours;

        state.Hunger    -= (float)(HungerDecayPerHour    * elapsedHours);
        state.Hygiene   -= (float)(HygieneDecayPerHour   * elapsedHours);
        state.Fun       -= (float)(FunDecayPerHour       * elapsedHours);
        state.Knowledge -= (float)(KnowledgeDecayPerHour * elapsedHours);

        state.Clamp();
        state.LastUpdatedUtc = now;
    }
}
