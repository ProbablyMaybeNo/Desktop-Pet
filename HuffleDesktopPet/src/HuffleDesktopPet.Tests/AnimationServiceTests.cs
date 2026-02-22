using HuffleDesktopPet.Core;
using HuffleDesktopPet.Core.Models;
using HuffleDesktopPet.Core.Services;
using Xunit;

namespace HuffleDesktopPet.Tests;

/// <summary>
/// Verifies AnimationService state machine, frame advancement, transient playback,
/// priority stack, sad hysteresis, need prompts, and faint locking.
/// All bars: 0 = satisfied (best), 100 = critical (worst).
/// </summary>
public sealed class AnimationServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static readonly Dictionary<string, int> DefaultCounts = new()
    {
        ["idle"]        = 4,
        ["walk"]        = 4,
        ["hungry"]      = 2,
        ["tired"]       = 2,
        ["dirty"]       = 2,
        ["bored"]       = 2,
        ["sad"]         = 2,
        ["happy"]       = 3,
        ["sleep"]       = 2,
        ["faint"]       = 2,
        ["eat"]         = 4,
        ["clean"]       = 4,
        ["study"]       = 4,
        ["poked"]       = 2,
        ["celebrating"] = 4,
    };

    private static AnimationService Make() => new(DefaultCounts);

    /// <summary>Pet with all needs satisfied (0 = best, well within happy zone).</summary>
    private static PetState HealthyPet() => new()
    {
        Hunger = 0f, Tired = 0f, Dirty = 0f, Bored = 0f, Sad = 0f
    };

    /// <summary>Pet sleeping via scheduled window.</summary>
    private static PetState SleepingPet() => new()
    {
        Hunger = 0f, Tired = 0f, Dirty = 0f, Bored = 0f, Sad = 0f,
        IsSleeping = true
    };

    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void InitialState_IsIdle()
    {
        var svc = Make();
        Assert.Equal("idle", svc.CurrentState);
        Assert.Equal(0, svc.CurrentFrame);
        Assert.False(svc.IsPlayingTransient);
    }

    // ── Priority 10: Idle (default) ───────────────────────────────────────────

    [Fact]
    public void Tick_HealthyPet_NotMoving_IsIdle()
    {
        var svc = Make();
        svc.Tick(0.01, HealthyPet(), isMoving: false);
        Assert.Equal("idle", svc.CurrentState);
    }

    // ── Priority 9: Happy ─────────────────────────────────────────────────────

    [Fact]
    public void Tick_AllNeedsLow_NotMoving_IsHappy()
    {
        // All below HappyNeedMax (30) and HappySadMax (30)
        var svc = Make();
        var pet = new PetState { Hunger = 5f, Tired = 5f, Dirty = 5f, Bored = 5f, Sad = 5f };
        svc.Tick(0.01, pet, isMoving: false);
        Assert.Equal("happy", svc.CurrentState);
    }

    [Fact]
    public void Tick_OneNeedAboveHappyThreshold_NotHappy()
    {
        var svc = Make();
        var pet = new PetState { Hunger = 35f, Tired = 5f, Dirty = 5f, Bored = 5f, Sad = 5f };
        svc.Tick(0.01, pet, isMoving: false);
        Assert.NotEqual("happy", svc.CurrentState);
    }

    // ── Priority 8: Walk ──────────────────────────────────────────────────────

    [Fact]
    public void Tick_WhenMoving_IsWalk()
    {
        var svc = Make();
        svc.Tick(0.01, HealthyPet(), isMoving: true);
        Assert.Equal("walk", svc.CurrentState);
    }

    // ── Priority 6: SAD (hysteresis) ─────────────────────────────────────────

    [Fact]
    public void Tick_SadBarAboveThreshold_ActivatesSadState()
    {
        var svc = Make();
        var pet = HealthyPet();
        pet.Sad = NeedConfig.SadActiveThreshold + 1f;  // trigger condition
        svc.Tick(0.01, pet, isMoving: false);
        Assert.Equal("sad", svc.CurrentState);
        Assert.True(svc.SadActive);
    }

    [Fact]
    public void Tick_TwoNeedsAboveStage2_ActivatesSad()
    {
        var svc = Make();
        var pet = new PetState { Hunger = 65f, Dirty = 65f };
        svc.Tick(0.01, pet, isMoving: false);
        Assert.Equal("sad", svc.CurrentState);
    }

    [Fact]
    public void Sad_DoesNotClear_UntilBothConditionsResolve()
    {
        // Activate sad via bar
        var svc = Make();
        var pet = HealthyPet();
        pet.Sad = NeedConfig.SadActiveThreshold + 5f;
        svc.Tick(0.01, pet, isMoving: false);
        Assert.True(svc.SadActive);

        // Drop to value between clear (40) and active (60) thresholds — should remain active
        pet.Sad = 50f;
        svc.Tick(0.01, pet, isMoving: false);
        Assert.True(svc.SadActive);

        // Drop below clear threshold — now clears
        pet.Sad = NeedConfig.SadClearThreshold - 1f;
        svc.Tick(0.01, pet, isMoving: false);
        Assert.False(svc.SadActive);
    }

    [Fact]
    public void Sad_TakesPriorityOverWalk()
    {
        var svc = Make();
        var pet = HealthyPet();
        pet.Sad = 70f;
        svc.Tick(0.01, pet, isMoving: true);   // moving but sad wins
        Assert.Equal("sad", svc.CurrentState);
    }

    // ── Priority 5: Scheduled sleep ───────────────────────────────────────────

    [Fact]
    public void Tick_IsSleeping_ReturnsSleep()
    {
        var svc = Make();
        svc.Tick(0.01, SleepingPet(), isMoving: false);
        Assert.Equal("sleep", svc.CurrentState);
    }

    [Fact]
    public void Tick_SadTakesPriorityOverScheduledSleep()
    {
        var svc = Make();
        var pet = SleepingPet();
        pet.Sad = 70f;
        svc.Tick(0.01, pet, isMoving: false);
        Assert.Equal("sad", svc.CurrentState);   // sad is priority 6, sleep is priority 5
    }

    // ── Priority 4: Interaction transients ───────────────────────────────────

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
        svc.TriggerTransient("eat");
        svc.Tick(0.01, HealthyPet(), isMoving: true);   // would be walk without transient
        Assert.Equal("eat", svc.CurrentState);
    }

    [Fact]
    public void Transient_BlocksSleep()
    {
        var svc = Make();
        svc.TriggerTransient("eat");
        svc.Tick(0.01, SleepingPet(), isMoving: false);
        Assert.Equal("eat", svc.CurrentState);
    }

    [Fact]
    public void Transient_ClearsAfterPlayingAllFrames()
    {
        // eat has 4 frames at 5 fps → 0.8 s per cycle
        var svc = Make();
        var pet = HealthyPet();
        svc.TriggerTransient("eat");
        for (int i = 0; i < 60; i++)
            svc.Tick(0.02, pet, isMoving: false);   // 1.2 s total
        Assert.False(svc.IsPlayingTransient);
    }

    [Fact]
    public void AfterTransientClears_ReturnsToPassiveState()
    {
        var svc = Make();
        var pet = HealthyPet();
        svc.TriggerTransient("eat");
        for (int i = 0; i < 60; i++)
            svc.Tick(0.02, pet, isMoving: false);
        Assert.Equal("happy", svc.CurrentState);   // healthy pet → happy
    }

    // ── Priority 3: Forced sleep ──────────────────────────────────────────────

    [Fact]
    public void Tick_ForcedSleep_ReturnsSleep_IgnoresTransient()
    {
        var svc = Make();
        svc.TriggerTransient("eat");   // active transient
        var pet = new PetState { IsForcedSleep = true };
        svc.Tick(0.01, pet, isMoving: false);
        // Forced sleep (priority 3) beats transient (priority 4)
        Assert.Equal("sleep", svc.CurrentState);
    }

    // ── Priority 1: Faint (uninterruptible) ──────────────────────────────────

    [Fact]
    public void TriggerFaint_StartsUninterruptibleFaint()
    {
        var svc = Make();
        svc.TriggerFaint();
        Assert.Equal("faint", svc.CurrentState);
        Assert.True(svc.IsFaintLocked);
    }

    [Fact]
    public void FaintLock_PreventsTriggerTransient()
    {
        var svc = Make();
        svc.TriggerFaint();
        svc.TriggerTransient("eat");   // should be blocked
        Assert.Equal("faint", svc.CurrentState);
    }

    [Fact]
    public void FaintLock_ClearsAfterAnimationComplete()
    {
        // faint has 2 frames at 4 fps → 0.5 s per cycle
        var svc = Make();
        var pet = HealthyPet();
        svc.TriggerFaint();
        for (int i = 0; i < 40; i++)
            svc.Tick(0.02, pet, isMoving: false);   // 0.8 s total
        Assert.False(svc.IsFaintLocked);
    }

    [Fact]
    public void HungerFainted_ShowsFaintState()
    {
        var svc = Make();
        var pet = new PetState { IsHungerFainted = true, IsFainting = true };
        svc.Tick(0.01, pet, isMoving: false);
        Assert.Equal("faint", svc.CurrentState);
    }

    // ── Need prompts ──────────────────────────────────────────────────────────

    [Fact]
    public void NeedPrompt_HighHunger_TriggersHungryPrompt()
    {
        var svc = Make();
        var pet = new PetState { Hunger = 50f };  // Stage1 (30–59)

        // Run several ticks beyond Stage1PromptCooldownSec
        double time = NeedConfig.Stage1PromptCooldownSec + 1.0;
        svc.Tick(time, pet, isMoving: false);

        Assert.Equal("hungry", svc.CurrentNeedPrompt);
    }

    [Fact]
    public void NeedPrompt_Stage3_TriggersFrequently()
    {
        var svc = Make();
        var pet = new PetState { Bored = 95f };  // Stage3

        // Run ticks past Stage3 cooldown (30 sec)
        double time = NeedConfig.Stage3PromptCooldownSec + 1.0;
        svc.Tick(time, pet, isMoving: false);

        Assert.Equal("bored", svc.CurrentNeedPrompt);
    }

    [Fact]
    public void NeedPrompt_HighestSeverity_WinsWhenMultipleNeedsHigh()
    {
        var svc = Make();
        // Dirty = stage3 (95), Hungry = stage1 (35) — dirty should win
        var pet = new PetState { Dirty = 95f, Hunger = 35f };

        double time = NeedConfig.Stage3PromptCooldownSec + 1.0;
        svc.Tick(time, pet, isMoving: false);

        Assert.Equal("dirty", svc.CurrentNeedPrompt);
    }

    [Fact]
    public void NeedPrompt_DoesNotFireDuringSleep()
    {
        var svc = Make();
        var pet = new PetState { Hunger = 90f, IsSleeping = true };  // stage3 but sleeping

        double time = NeedConfig.Stage3PromptCooldownSec + 1.0;
        svc.Tick(time, pet, isMoving: false);

        Assert.Null(svc.CurrentNeedPrompt);
    }

    [Fact]
    public void NeedPrompt_DoesNotFireDuringSad()
    {
        var svc = Make();
        // First activate sad
        var pet = new PetState { Sad = 70f, Hunger = 90f };
        svc.Tick(0.01, pet, isMoving: false);   // latch sad active

        // Now run past prompt cooldown
        double time = NeedConfig.Stage3PromptCooldownSec + 1.0;
        svc.Tick(time, pet, isMoving: false);

        Assert.Null(svc.CurrentNeedPrompt);
    }

    // ── Frame advancement ─────────────────────────────────────────────────────

    [Fact]
    public void Tick_ZeroDelta_DoesNotAdvanceFrame()
    {
        var svc = Make();
        svc.Tick(0, HealthyPet(), isMoving: false);
        Assert.Equal(0, svc.CurrentFrame);
    }

    [Fact]
    public void Tick_AfterOneFrameInterval_AdvancesToFrame1()
    {
        // happy = 6 fps → interval ≈ 0.167 s
        var svc = Make();
        var pet = new PetState { Hunger = 5f, Tired = 5f, Dirty = 5f, Bored = 5f, Sad = 5f };
        svc.Tick(0.20, pet, isMoving: false);   // just past one frame
        Assert.Equal(1, svc.CurrentFrame);
    }

    [Fact]
    public void Tick_WrapsFrameBackToZero()
    {
        var svc = Make();
        var pet = HealthyPet();
        svc.Tick(2.0, pet, isMoving: false);    // plenty of time to cycle
        Assert.InRange(svc.CurrentFrame, 0, DefaultCounts["happy"] - 1);
    }

    [Fact]
    public void StateSwitch_ResetsFrameToZero()
    {
        var svc = Make();
        var pet = HealthyPet();

        svc.Tick(0.01, pet, isMoving: true);    // walk
        svc.Tick(0.30, pet, isMoving: true);    // advance frame
        svc.Tick(0.01, pet, isMoving: false);   // switch to happy

        Assert.Equal("happy", svc.CurrentState);
        Assert.Equal(0, svc.CurrentFrame);
    }

    // ── WakeUp ────────────────────────────────────────────────────────────────

    [Fact]
    public void WakeUp_ClearsNeedPrompt()
    {
        var svc = Make();
        var pet = new PetState { Hunger = 90f };
        svc.Tick(NeedConfig.Stage3PromptCooldownSec + 1.0, pet, isMoving: false);
        Assert.NotNull(svc.CurrentNeedPrompt);

        svc.WakeUp();
        Assert.Null(svc.CurrentNeedPrompt);
    }
}
