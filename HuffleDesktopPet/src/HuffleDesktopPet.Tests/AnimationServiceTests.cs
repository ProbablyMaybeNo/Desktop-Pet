using HuffleDesktopPet.Core.Models;
using HuffleDesktopPet.Core.Services;
using Xunit;

namespace HuffleDesktopPet.Tests;

public sealed class AnimationServiceTests
{
    private static readonly Dictionary<string, int> DefaultCounts = new()
    {
        ["idle"] = 3,
        ["walk"] = 4,
        ["hungry"] = 2,
        ["bored"] = 2,
        ["dirty"] = 2,
        ["sad"] = 2,
        ["eat"] = 4,
        ["clean"] = 4,
        ["cautious"] = 2,
        ["booing"] = 2,
    };

    private static PetState HealthyPet() => new() { Hunger = 100f, Hygiene = 100f, Fun = 100f, Knowledge = 100f };

    [Fact]
    public void Priority_CriticalNeed_OverridesIdle()
    {
        var pet = HealthyPet();
        pet.Hunger = 10f;
        string state = AnimationService.ResolvePassiveState(pet, isMoving: false, localNow: new DateTime(2025, 1, 1, 14, 0, 0));
        Assert.Equal("sad", state);
    }

    [Fact]
    public void Priority_Motion_OverridesNeeds()
    {
        var pet = HealthyPet();
        pet.Hunger = 10f;
        string state = AnimationService.ResolvePassiveState(pet, isMoving: true, localNow: new DateTime(2025, 1, 1, 14, 0, 0));
        Assert.Equal("walk", state);
    }

    [Fact]
    public void Hysteresis_MinDuration_PreventsFlipFlop()
    {
        DateTime now = new(2025, 1, 1, 14, 0, 0);
        var svc = new AnimationService(DefaultCounts, clock: () => now, random: new Random(1));
        var pet = HealthyPet();

        pet.Hunger = 25f;
        svc.Tick(0.5, pet, isMoving: false); // transition to hungry
        Assert.Equal("hungry", svc.CurrentState);

        pet.Hunger = 90f; // would return to happy/idle, but hungry min duration is 2s
        now = now.AddSeconds(0.5);
        svc.Tick(0.5, pet, isMoving: false);
        Assert.Equal("hungry", svc.CurrentState);

        now = now.AddSeconds(2.0);
        svc.Tick(0.5, pet, isMoving: false);
        Assert.NotEqual("hungry", svc.CurrentState);
    }

    [Fact]
    public void Timing_MinDuration_RespectedForWalk()
    {
        DateTime now = new(2025, 1, 1, 14, 0, 0);
        var svc = new AnimationService(DefaultCounts, clock: () => now, random: new Random(1));
        var pet = HealthyPet();

        svc.Tick(0.1, pet, isMoving: true);
        Assert.Equal("walk", svc.CurrentState);

        now = now.AddMilliseconds(100);
        svc.Tick(0.1, pet, isMoving: false);
        Assert.Equal("walk", svc.CurrentState);

        now = now.AddMilliseconds(300);
        svc.Tick(0.1, pet, isMoving: false);
        Assert.NotEqual("walk", svc.CurrentState);
    }

    [Fact]
    public void Determinism_FixedSeed_ProducesSameSequence()
    {
        static List<string> RunSequence(int seed)
        {
            DateTime now = new(2025, 1, 1, 14, 0, 0);
            var svc = new AnimationService(DefaultCounts, clock: () => now, random: new Random(seed));
            var pet = new PetState { Hunger = 50, Hygiene = 50, Fun = 50, Knowledge = 50 };
            var states = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                now = now.AddSeconds(6); // allows idle flavor roll
                svc.Tick(0.2, pet, isMoving: false);
                states.Add(svc.CurrentState);
            }

            return states;
        }

        var a = RunSequence(42);
        var b = RunSequence(42);

        Assert.Equal(a, b);
    }

    [Fact]
    public void LogsTransition_WithReason()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sprite_state_{Guid.NewGuid():N}.log");
        DateTime now = new(2025, 1, 1, 14, 0, 0);
        var svc = new AnimationService(DefaultCounts, clock: () => now, random: new Random(1), transitionLogPath: path);
        var pet = HealthyPet();

        svc.Tick(0.2, pet, isMoving: true);

        string log = File.ReadAllText(path);
        Assert.Contains("idle", log);
        Assert.Contains("walk", log);
        Assert.Contains("motion", log);
    }

    // ── TriggerTransient ────────────────────────────────────────────────────

    [Fact]
    public void TriggerTransient_SetsCurrentState()
    {
        DateTime now = new(2025, 1, 1, 14, 0, 0);
        var svc = new AnimationService(DefaultCounts, clock: () => now, random: new Random(1));
        var pet = HealthyPet();

        svc.TriggerTransient("eat");

        Assert.Equal("eat", svc.CurrentState);
        Assert.True(svc.IsPlayingTransient);
    }

    [Fact]
    public void TriggerTransient_UnknownState_DoesNotTransition()
    {
        DateTime now = new(2025, 1, 1, 14, 0, 0);
        var svc = new AnimationService(DefaultCounts, clock: () => now, random: new Random(1));
        string before = svc.CurrentState;

        svc.TriggerTransient("nonexistent_state");

        Assert.Equal(before, svc.CurrentState);
        Assert.False(svc.IsPlayingTransient);
    }

    [Fact]
    public void TriggerTransient_ClearsAfterAllFramesAdvance()
    {
        DateTime now = new(2025, 1, 1, 14, 0, 0);
        // "eat" has 4 frames at 5 fps → 0.2 s per frame → 4 frames = 0.8 s total
        var svc = new AnimationService(DefaultCounts, clock: () => now, random: new Random(1));
        var pet = HealthyPet();

        svc.TriggerTransient("eat");
        Assert.True(svc.IsPlayingTransient);

        // Advance past all 4 frames
        for (int i = 0; i < 8; i++)   // 8 × 0.15 s = 1.2 s > 0.8 s
            svc.Tick(0.15, pet, isMoving: false);

        Assert.False(svc.IsPlayingTransient);
    }

    // ── Sleep schedule ──────────────────────────────────────────────────────

    [Fact]
    public void Sleep_ScheduledAt22h_TriggersWithGoodNeeds()
    {
        // 22:00 is in the scheduled-sleep window; healthy pet should sleep
        string state = AnimationService.ResolvePassiveState(
            HealthyPet(), isMoving: false, localNow: new DateTime(2025, 1, 1, 22, 0, 0));
        Assert.Equal("sleep", state);
    }

    [Fact]
    public void Sleep_ScheduledAt07h_TriggersWithGoodNeeds()
    {
        string state = AnimationService.ResolvePassiveState(
            HealthyPet(), isMoving: false, localNow: new DateTime(2025, 1, 1, 7, 0, 0));
        Assert.Equal("sleep", state);
    }

    [Fact]
    public void Sleep_ScheduledAt12h_TriggersWithGoodNeeds()
    {
        string state = AnimationService.ResolvePassiveState(
            HealthyPet(), isMoving: false, localNow: new DateTime(2025, 1, 1, 12, 0, 0));
        Assert.Equal("sleep", state);
    }

    [Fact]
    public void Sleep_AtNormalHour_DoesNotTriggerWithGoodNeeds()
    {
        // 14:00 is not in any scheduled-sleep window
        string state = AnimationService.ResolvePassiveState(
            HealthyPet(), isMoving: false, localNow: new DateTime(2025, 1, 1, 14, 0, 0));
        Assert.NotEqual("sleep", state);
    }

    [Fact]
    public void Sleep_InactivityOverThreshold_TriggersSleep()
    {
        const double sleepSeconds = 15 * 60 + 1; // just over 15 minutes
        string state = AnimationService.ResolvePassiveState(
            HealthyPet(),
            isMoving: false,
            inactivitySeconds: sleepSeconds,
            localNow: new DateTime(2025, 1, 1, 14, 0, 0));
        Assert.Equal("sleep", state);
    }

    [Fact]
    public void Sleep_ForcedAwake_OverridesSchedule()
    {
        string state = AnimationService.ResolvePassiveState(
            HealthyPet(),
            isMoving: false,
            forcedAwake: true,
            localNow: new DateTime(2025, 1, 1, 22, 0, 0));
        Assert.NotEqual("sleep", state);
    }

    // ── Knowledge → bored ──────────────────────────────────────────────────

    [Fact]
    public void LowKnowledge_WithOtherNeedsOk_TriggersBored()
    {
        var pet = new PetState
        {
            Hunger    = 80f,
            Hygiene   = 80f,
            Fun       = 80f,
            Knowledge = 20f,   // below WarningThreshold (30)
        };
        string state = AnimationService.ResolvePassiveState(
            pet, isMoving: false, localNow: new DateTime(2025, 1, 1, 14, 0, 0));
        Assert.Equal("bored", state);
    }

    [Fact]
    public void LowKnowledge_Reason_IsLowKnowledge()
    {
        var pet = new PetState
        {
            Hunger    = 80f,
            Hygiene   = 80f,
            Fun       = 80f,
            Knowledge = 20f,
        };
        var decision = AnimationService.ResolvePassiveDecision(
            pet,
            isMoving: false,
            inactivitySeconds: 0,
            forcedAwake: false,
            now: new DateTime(2025, 1, 1, 14, 0, 0),
            random: new Random(0),
            lastIdleFlavorChange: DateTime.MinValue);
        Assert.Equal("bored",         decision.State);
        Assert.Equal("low_knowledge", decision.Reason);
    }
}
