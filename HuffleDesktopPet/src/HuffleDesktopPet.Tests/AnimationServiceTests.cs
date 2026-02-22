using HuffleDesktopPet.Core.Models;
using HuffleDesktopPet.Core.Services;
using Xunit;

namespace HuffleDesktopPet.Tests;

/// <summary>
/// Verifies AnimationService state machine, frame advancement, and transient playback.
/// </summary>
public sealed class AnimationServiceTests
{
    // Minimal frame counts used across most tests
    private static readonly Dictionary<string, int> DefaultCounts = new()
    {
        ["idle"]   = 3,
        ["walk"]   = 4,
        ["hungry"] = 2,
        ["bored"]  = 2,
        ["dirty"]  = 2,
        ["sad"]    = 2,
        ["eat"]    = 4,
        ["clean"]  = 4,
    };

    private static AnimationService Make() => new(DefaultCounts);

    private static PetState HealthyPet() => new()
    {
        Hunger = 100f, Hygiene = 100f, Fun = 100f, Knowledge = 100f
    };

    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsIdle()
    {
        var svc = Make();
        Assert.Equal("idle", svc.CurrentState);
        Assert.Equal(0, svc.CurrentFrame);
    }

    [Fact]
    public void InitialState_IsNotPlayingTransient()
    {
        var svc = Make();
        Assert.False(svc.IsPlayingTransient);
    }

    // ── Passive state resolution ──────────────────────────────────────────────

    [Fact]
    public void Tick_WhenMoving_SwitchesToWalk()
    {
        var svc  = Make();
        var pet  = HealthyPet();
        svc.Tick(0.01, pet, isMoving: true);
        Assert.Equal("walk", svc.CurrentState);
    }

    [Fact]
    public void Tick_WhenStopped_HealthyNeeds_SwitchesToIdle()
    {
        var svc = Make();
        var pet = HealthyPet();
        svc.Tick(0.01, pet, isMoving: false);
        Assert.Equal("idle", svc.CurrentState);
    }

    [Fact]
    public void Tick_WhenStopped_AnyCriticalNeed_SwitchesToSad()
    {
        var svc = Make();
        var pet = HealthyPet();
        pet.Hunger = 15f;   // below critical threshold (20)
        svc.Tick(0.01, pet, isMoving: false);
        Assert.Equal("sad", svc.CurrentState);
    }

    [Fact]
    public void Tick_WhenStopped_HungerLow_SwitchesToHungry()
    {
        var svc = Make();
        var pet = HealthyPet();
        pet.Hunger = 25f;   // below warning threshold (30) but above critical (20)
        svc.Tick(0.01, pet, isMoving: false);
        Assert.Equal("hungry", svc.CurrentState);
    }

    [Fact]
    public void Tick_WhenStopped_HygieneLow_SwitchesToDirty()
    {
        var svc = Make();
        var pet = HealthyPet();
        pet.Hygiene = 25f;
        svc.Tick(0.01, pet, isMoving: false);
        Assert.Equal("dirty", svc.CurrentState);
    }

    [Fact]
    public void Tick_WhenStopped_FunLow_SwitchesToBored()
    {
        var svc = Make();
        var pet = HealthyPet();
        pet.Fun = 25f;
        svc.Tick(0.01, pet, isMoving: false);
        Assert.Equal("bored", svc.CurrentState);
    }

    [Fact]
    public void Tick_SadTakesPriorityOverHungry()
    {
        var svc = Make();
        var pet = HealthyPet();
        pet.Hunger = 10f;   // critical — should be "sad", not "hungry"
        svc.Tick(0.01, pet, isMoving: false);
        Assert.Equal("sad", svc.CurrentState);
    }

    // ── State switching resets frame ──────────────────────────────────────────

    [Fact]
    public void StateSwitch_ResetsFrameToZero()
    {
        var svc = Make();
        var pet = HealthyPet();

        // Advance to walk
        svc.Tick(0.01, pet, isMoving: true);
        // Pump enough time to advance a frame
        svc.Tick(0.5, pet, isMoving: true);

        // Switch back to idle
        svc.Tick(0.01, pet, isMoving: false);
        Assert.Equal("idle", svc.CurrentState);
        Assert.Equal(0, svc.CurrentFrame);
    }

    // ── Frame advancement ─────────────────────────────────────────────────────

    [Fact]
    public void Tick_ZeroDelta_DoesNotAdvanceFrame()
    {
        var svc = Make();
        var pet = HealthyPet();
        svc.Tick(0, pet, isMoving: false);
        Assert.Equal(0, svc.CurrentFrame);
    }

    [Fact]
    public void Tick_AfterOneFrameInterval_AdvancesToFrame1()
    {
        var svc = Make();   // idle = 3 fps → interval ≈ 0.333 s
        var pet = HealthyPet();

        svc.Tick(0.35, pet, isMoving: false);   // just past one interval
        Assert.Equal(1, svc.CurrentFrame);
    }

    [Fact]
    public void Tick_WrapsFrameBackToZero()
    {
        var svc = Make();   // idle has 3 frames at 3 fps → wraps after ~1 s
        var pet = HealthyPet();

        // Pump enough time to cycle all 3 frames
        svc.Tick(1.1, pet, isMoving: false);
        // Frame should have wrapped (not be >= frameCount)
        Assert.InRange(svc.CurrentFrame, 0, 2);
    }

    // ── Transient animations ──────────────────────────────────────────────────

    [Fact]
    public void TriggerTransient_SwitchesToThatState()
    {
        var svc = Make();
        svc.TriggerTransient("eat");
        Assert.Equal("eat", svc.CurrentState);
        Assert.Equal(0, svc.CurrentFrame);
        Assert.True(svc.IsPlayingTransient);
    }

    [Fact]
    public void TriggerTransient_UnknownState_IsNoOp()
    {
        var svc = Make();
        svc.TriggerTransient("nonexistent");
        Assert.Equal("idle", svc.CurrentState);
        Assert.False(svc.IsPlayingTransient);
    }

    [Fact]
    public void Transient_BlocksPassiveStateSwitching()
    {
        var svc = Make();
        var pet = HealthyPet();

        svc.TriggerTransient("eat");
        svc.Tick(0.01, pet, isMoving: true);   // would normally switch to walk

        Assert.Equal("eat", svc.CurrentState);
    }

    [Fact]
    public void Transient_ClearsAfterPlayingAllFrames()
    {
        // eat has 4 frames at 5 fps → interval = 0.2 s → full cycle = 0.8 s
        var svc = Make();
        var pet = HealthyPet();

        svc.TriggerTransient("eat");

        // Advance through all 4 frames (need 4 × 0.2 s = 0.8 s, add margin)
        for (int i = 0; i < 50; i++)
            svc.Tick(0.02, pet, isMoving: false);   // 50 × 20 ms = 1.0 s

        Assert.False(svc.IsPlayingTransient);
    }

    [Fact]
    public void AfterTransientClears_ReturnsToPassiveState()
    {
        var svc = Make();
        var pet = HealthyPet();

        svc.TriggerTransient("eat");

        // Run past the transient duration
        for (int i = 0; i < 50; i++)
            svc.Tick(0.02, pet, isMoving: false);

        Assert.Equal("idle", svc.CurrentState);
    }

    // ── ResolvePassiveState (internal, tested directly) ───────────────────────

    [Fact]
    public void ResolvePassiveState_Moving_ReturnsWalk()
    {
        var pet = HealthyPet();
        Assert.Equal("walk", AnimationService.ResolvePassiveState(pet, isMoving: true));
    }

    [Fact]
    public void ResolvePassiveState_AllHealthy_ReturnsIdle()
    {
        var pet = HealthyPet();
        Assert.Equal("idle", AnimationService.ResolvePassiveState(pet, isMoving: false));
    }
}
