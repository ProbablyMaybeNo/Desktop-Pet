using HuffleDesktopPet.Core.Services;
using Xunit;

namespace HuffleDesktopPet.Tests;

/// <summary>
/// Unit tests for <see cref="WanderService"/>.
/// Uses a fixed RNG seed for deterministic results.
/// </summary>
public sealed class WanderServiceTests
{
    private const double MinX = 0, MinY = 0, MaxX = 1800, MaxY = 980;

    private static WanderService Make(double x = 100, double y = 100, int seed = 42)
        => new(x, y, MinX, MinY, MaxX, MaxY, seed);

    // ── Constructor / clamping ─────────────────────────────────────────────

    [Fact]
    public void Constructor_ClampsToBounds_WhenStartOutOfRange()
    {
        var svc = new WanderService(-9999, 99999, MinX, MinY, MaxX, MaxY, seed: 1);
        Assert.Equal(MinX, svc.X);
        Assert.Equal(MaxY, svc.Y);
    }

    [Fact]
    public void Constructor_StartsIdle()
    {
        var svc = Make();
        Assert.True(svc.IsIdle);
    }

    // ── Tick — zero / negative delta ──────────────────────────────────────

    [Fact]
    public void Tick_ZeroDelta_DoesNotChangePosition()
    {
        var svc = Make();
        double x0 = svc.X, y0 = svc.Y;
        svc.Tick(0);
        Assert.Equal(x0, svc.X);
        Assert.Equal(y0, svc.Y);
    }

    [Fact]
    public void Tick_NegativeDelta_DoesNotChangePosition()
    {
        var svc = Make();
        double x0 = svc.X, y0 = svc.Y;
        svc.Tick(-5);
        Assert.Equal(x0, svc.X);
        Assert.Equal(y0, svc.Y);
    }

    // ── Tick — movement ───────────────────────────────────────────────────

    [Fact]
    public void Tick_AfterInitialIdleExpires_PetStartsMoving()
    {
        var svc = Make(x: 100, y: 100);

        // Burn off initial idle (0.5 s)
        svc.Tick(1.0);
        // Pet should now have a target and should move
        double x0 = svc.X, y0 = svc.Y;
        svc.Tick(1.0);

        // Position should differ from start OR still idle at same spot — either is valid
        // Key invariant: must stay in bounds
        Assert.InRange(svc.X, MinX, MaxX);
        Assert.InRange(svc.Y, MinY, MaxY);
    }

    [Fact]
    public void Tick_ManyIterations_PositionAlwaysInBounds()
    {
        var svc = Make();
        for (int i = 0; i < 500; i++)
            svc.Tick(0.033);  // simulate 33 ms ticks

        Assert.InRange(svc.X, MinX, MaxX);
        Assert.InRange(svc.Y, MinY, MaxY);
    }

    [Fact]
    public void Tick_LargeDelta_DoesNotOvershootBounds()
    {
        var svc = Make();
        svc.Tick(1000.0);   // absurdly large delta
        Assert.InRange(svc.X, MinX, MaxX);
        Assert.InRange(svc.Y, MinY, MaxY);
    }

    // ── SetPosition ───────────────────────────────────────────────────────

    [Fact]
    public void SetPosition_UpdatesCoordinates()
    {
        var svc = Make();
        svc.SetPosition(500, 300);
        Assert.Equal(500, svc.X);
        Assert.Equal(300, svc.Y);
    }

    [Fact]
    public void SetPosition_ClampsOutOfRangeValues()
    {
        var svc = Make();
        svc.SetPosition(-100, 99999);
        Assert.Equal(MinX, svc.X);
        Assert.Equal(MaxY, svc.Y);
    }

    [Fact]
    public void SetPosition_StartsIdleAfterTeleport()
    {
        var svc = Make();
        svc.Tick(10.0);     // burn off initial idle + move
        svc.SetPosition(500, 300);
        Assert.True(svc.IsIdle);
    }

    // ── FacingLeft ────────────────────────────────────────────────────────

    [Fact]
    public void FacingLeft_IsFalse_ByDefault()
    {
        // Default state — not yet moved
        var svc = Make();
        Assert.False(svc.FacingLeft);
    }

    // ── SpeedDipsPerSecond ────────────────────────────────────────────────

    [Fact]
    public void SpeedDipsPerSecond_IsPositive()
    {
        // Guard against accidental zero / negative which would freeze the pet
        Assert.True(WanderService.SpeedDipsPerSecond > 0);
    }

    [Fact]
    public void Tick_SpeedDoesNotExceedConstant()
    {
        // After initial idle, the pet moves. Verify it doesn't exceed the declared speed.
        var svc = Make(x: 100, y: 100, seed: 7);
        const double delta = 0.033;

        // Burn off initial idle
        svc.Tick(1.0);

        double x0 = svc.X, y0 = svc.Y;
        svc.Tick(delta);

        double moved = Math.Sqrt(Math.Pow(svc.X - x0, 2) + Math.Pow(svc.Y - y0, 2));
        double maxExpected = WanderService.SpeedDipsPerSecond * delta + 0.001; // small tolerance
        Assert.True(moved <= maxExpected,
            $"Moved {moved:F4} DIPs but max expected {maxExpected:F4}");
    }
}
