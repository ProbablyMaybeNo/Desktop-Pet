using HuffleDesktopPet.Core.Models;
using HuffleDesktopPet.Core.Services;
using Xunit;

namespace HuffleDesktopPet.Tests;

/// <summary>
/// Verifies PetEngine.Tick decay logic.
/// </summary>
public sealed class PetEngineTests
{
    private static PetState FullPet(DateTime baseTime) => new()
    {
        Hunger         = 100f,
        Hygiene        = 100f,
        Fun            = 100f,
        Knowledge      = 100f,
        LastUpdatedUtc = baseTime,
    };

    [Fact]
    public void Tick_OneHour_ReducesNeedsByDecayRate()
    {
        var baseline = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var state    = FullPet(baseline);
        var oneHourLater = baseline.AddHours(1);

        PetEngine.Tick(state, oneHourLater);

        Assert.Equal(90f, state.Hunger,    precision: 1);   // 100 - 10/hr
        Assert.Equal(95f, state.Hygiene,   precision: 1);   // 100 -  5/hr
        Assert.Equal(92f, state.Fun,       precision: 1);   // 100 -  8/hr
        Assert.Equal(98f, state.Knowledge, precision: 1);   // 100 -  2/hr
        Assert.Equal(oneHourLater, state.LastUpdatedUtc);
    }

    [Fact]
    public void Tick_ZeroElapsed_DoesNotChangeState()
    {
        var baseline = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var state    = FullPet(baseline);

        PetEngine.Tick(state, baseline);   // same time

        Assert.Equal(100f, state.Hunger);
        Assert.Equal(100f, state.Hygiene);
        Assert.Equal(100f, state.Fun);
    }

    [Fact]
    public void Tick_PastTime_DoesNotChangeState()
    {
        var baseline  = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var state     = FullPet(baseline);
        var pastTime  = baseline.AddHours(-1);

        PetEngine.Tick(state, pastTime);   // going backwards

        Assert.Equal(100f, state.Hunger);  // unchanged
        Assert.Equal(baseline, state.LastUpdatedUtc);
    }

    [Fact]
    public void Tick_LongDecay_ClampsAt0()
    {
        var baseline = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var state    = FullPet(baseline);
        var tenHoursLater = baseline.AddHours(100); // way past depletion

        PetEngine.Tick(state, tenHoursLater);

        Assert.Equal(0f, state.Hunger);
        Assert.Equal(0f, state.Hygiene);
        Assert.Equal(0f, state.Fun);
        Assert.Equal(0f, state.Knowledge);
    }

    [Fact]
    public void Tick_UpdatesLastUpdatedUtc()
    {
        var baseline = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var state    = FullPet(baseline);
        var future   = baseline.AddMinutes(30);

        PetEngine.Tick(state, future);

        Assert.Equal(future, state.LastUpdatedUtc);
    }
}
