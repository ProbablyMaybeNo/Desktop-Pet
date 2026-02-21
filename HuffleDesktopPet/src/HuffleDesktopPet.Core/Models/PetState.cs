namespace HuffleDesktopPet.Core.Models;

/// <summary>
/// Represents the full persisted state of the desktop pet.
/// All need values are 0–100 (clamped). Higher = better (full/clean/happy).
/// </summary>
public sealed class PetState
{
    // ── Needs (0 = empty/bad, 100 = full/great) ──────────────────────────────

    public float Hunger    { get; set; } = 100f;   // 100 = not hungry
    public float Hygiene   { get; set; } = 100f;   // 100 = clean
    public float Fun       { get; set; } = 100f;   // 100 = happy
    public float Knowledge { get; set; } = 50f;    // 50 = neutral curiosity

    // ── Meta ─────────────────────────────────────────────────────────────────

    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    // ── Position (logical, not pixel — resolved by UI layer) ─────────────────

    public double PositionX { get; set; } = 0.5;   // 0–1 fractional screen position
    public double PositionY { get; set; } = 0.8;

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Clamps all need values to [0, 100].
    /// </summary>
    public void Clamp()
    {
        Hunger    = Math.Clamp(Hunger,    0f, 100f);
        Hygiene   = Math.Clamp(Hygiene,   0f, 100f);
        Fun       = Math.Clamp(Fun,       0f, 100f);
        Knowledge = Math.Clamp(Knowledge, 0f, 100f);
    }
}
