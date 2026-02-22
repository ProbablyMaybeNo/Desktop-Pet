using HuffleDesktopPet.Core;
using HuffleDesktopPet.Core.Models;
using HuffleDesktopPet.Core.Services;
using Xunit;

namespace HuffleDesktopPet.Tests;

/// <summary>
/// Verifies PetEngine interactions and the minute-tick logic.
/// All bars: 0 = satisfied (best), 100 = critical (worst).
/// </summary>
public sealed class PetEngineInteractionTests
{
    // ── Feed ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Feed_ReducesHunger()
    {
        var s = new PetState { Hunger = 60f };
        PetEngine.Feed(s);
        Assert.Equal(60f - NeedConfig.FeedHungerReduce, s.Hunger, precision: 1);
    }

    [Fact]
    public void Feed_ClampsHungerAtZero()
    {
        var s = new PetState { Hunger = 10f };
        PetEngine.Feed(s);
        Assert.Equal(0f, s.Hunger);
    }

    [Fact]
    public void Feed_IncreasesDirtySlightly()
    {
        var s = new PetState { Hunger = 60f, Dirty = 20f };
        PetEngine.Feed(s);
        Assert.Equal(20f + NeedConfig.FeedDirtyIncrease, s.Dirty, precision: 1);
    }

    [Fact]
    public void Feed_ClearsHungerFaint()
    {
        var s = new PetState { Hunger = 95f, IsHungerFainted = true, IsFainting = true };
        PetEngine.Feed(s);
        Assert.False(s.IsHungerFainted);
        Assert.False(s.IsFainting);
    }

    [Fact]
    public void Feed_UpdatesLastInteractionTime()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var s = new PetState();
        PetEngine.Feed(s);
        Assert.True(s.LastInteractionUtc > before);
    }

    // ── Play ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Play_ReducesBored()
    {
        var s = new PetState { Bored = 80f };
        PetEngine.Play(s);
        Assert.Equal(80f - NeedConfig.PlayBoredomReduce, s.Bored, precision: 1);
    }

    [Fact]
    public void Play_ClampsBoredomAtZero()
    {
        var s = new PetState { Bored = 10f };
        PetEngine.Play(s);
        Assert.Equal(0f, s.Bored);
    }

    [Fact]
    public void Play_ReducesSad()
    {
        var s = new PetState { Sad = 50f };
        PetEngine.Play(s);
        Assert.Equal(50f - NeedConfig.PlaySadReduce, s.Sad, precision: 1);
    }

    [Fact]
    public void Play_IncreasesHungerSlightly()
    {
        var s = new PetState { Bored = 80f, Hunger = 20f };
        PetEngine.Play(s);
        Assert.Equal(20f + NeedConfig.PlayHungerIncrease, s.Hunger, precision: 1);
    }

    // ── Clean ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Clean_ReducesDirty()
    {
        var s = new PetState { Dirty = 70f };
        PetEngine.Clean(s);
        Assert.Equal(70f - NeedConfig.CleanDirtyReduce, s.Dirty, precision: 1);
    }

    [Fact]
    public void Clean_ClampsDirtyAtZero()
    {
        var s = new PetState { Dirty = 10f };
        PetEngine.Clean(s);
        Assert.Equal(0f, s.Dirty);
    }

    [Fact]
    public void Clean_DoesNotAffectHungerOrTired()
    {
        var s = new PetState { Dirty = 70f, Hunger = 40f, Tired = 50f };
        PetEngine.Clean(s);
        Assert.Equal(40f, s.Hunger, precision: 1);
        Assert.Equal(50f, s.Tired,  precision: 1);
    }

    // ── Study ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Study_ReducesBored()
    {
        var s = new PetState { Bored = 60f };
        PetEngine.Study(s);
        Assert.True(s.Bored < 60f);
    }

    [Fact]
    public void Study_IncreasesHungerSlightly()
    {
        var s = new PetState { Bored = 60f, Hunger = 10f };
        PetEngine.Study(s);
        Assert.True(s.Hunger > 10f);
    }

    // ── Tick — need fills ─────────────────────────────────────────────────────

    [Fact]
    public void Tick_IncreasesHungerOverTime()
    {
        var s   = new PetState();
        var now = DateTime.UtcNow;
        s.LastUpdatedUtc = now.AddMinutes(-60);   // 1 hour ago
        PetEngine.Tick(s, now);
        Assert.True(s.Hunger > 0f);
    }

    [Fact]
    public void Tick_IncreasesHungerByExpectedAmount()
    {
        var s = new PetState();
        var now = DateTime.UtcNow;
        s.LastUpdatedUtc = now.AddMinutes(-60);
        PetEngine.Tick(s, now);
        // 60 min × HungerTickPerMin
        float expected = 60f * NeedConfig.HungerTickPerMin;
        Assert.Equal(expected, s.Hunger, precision: 1);
    }

    [Fact]
    public void Tick_ClampsNeedsAt100()
    {
        var s = new PetState { Hunger = 99f };
        var now = DateTime.UtcNow;
        s.LastUpdatedUtc = now.AddMinutes(-240);   // 4 hours
        PetEngine.Tick(s, now);
        Assert.Equal(100f, s.Hunger);
    }

    [Fact]
    public void Tick_ReturnedEvents_NoneWhenHealthy()
    {
        var s = new PetState();
        var events = PetEngine.Tick(s, DateTime.UtcNow);
        Assert.Equal(PetTickEvents.None, events);
    }

    // ── Tick — sleep transitions ──────────────────────────────────────────────

    [Fact]
    public void Tick_EntersSleepWindow_SetsSleeping()
    {
        // Simulate: last tick was before 10pm, now it's 10pm
        var s = new PetState
        {
            LastUpdatedUtc = new DateTime(2025, 1, 1, 21, 55, 0, DateTimeKind.Local).ToUniversalTime()
        };
        var nowLocal = new DateTime(2025, 1, 1, 22, 5, 0, DateTimeKind.Local);
        PetEngine.Tick(s, nowLocal.ToUniversalTime());
        Assert.True(s.IsSleeping);
    }

    [Fact]
    public void Tick_WokeEarly_DoesNotReEnterSleep()
    {
        var s = new PetState
        {
            WokeEarlyInWindow = true,
            IsSleeping        = false,
            LastUpdatedUtc    = new DateTime(2025, 1, 1, 22, 0, 0, DateTimeKind.Local).ToUniversalTime()
        };
        var nowLocal = new DateTime(2025, 1, 1, 22, 10, 0, DateTimeKind.Local);
        PetEngine.Tick(s, nowLocal.ToUniversalTime());
        Assert.False(s.IsSleeping);
    }

    // ── Tick — sad derivation ─────────────────────────────────────────────────

    [Fact]
    public void Tick_TwoNeedsAboveStage2_IncreasesSad()
    {
        var s = new PetState { Hunger = 70f, Dirty = 70f, Sad = 0f };
        s.LastUpdatedUtc = DateTime.UtcNow.AddMinutes(-5);
        PetEngine.Tick(s, DateTime.UtcNow);
        Assert.True(s.Sad > 0f);
    }

    [Fact]
    public void Tick_AllNeedsGood_DecreasesSad()
    {
        var s = new PetState { Sad = 50f };
        s.LastUpdatedUtc = DateTime.UtcNow.AddMinutes(-10);
        PetEngine.Tick(s, DateTime.UtcNow);
        Assert.True(s.Sad < 50f);
    }
}
