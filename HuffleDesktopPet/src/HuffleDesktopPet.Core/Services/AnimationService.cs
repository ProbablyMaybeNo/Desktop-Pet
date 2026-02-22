using System.Globalization;
using HuffleDesktopPet.Core.Models;

namespace HuffleDesktopPet.Core.Services;

public sealed class AnimationService
{
    private const float CriticalThreshold = 20f;
    private const float WarningThreshold = 30f;
    private const float HappyThreshold = 70f;
    private const double InactivitySleepMinutes = 15.0;

    private static readonly Dictionary<string, StateTiming> StateTimings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["idle"] = new(TimeSpan.FromSeconds(1.0), canInterrupt: true),
        ["walk"] = new(TimeSpan.FromSeconds(0.25), canInterrupt: true),
        ["hungry"] = new(TimeSpan.FromSeconds(2.0), canInterrupt: true),
        ["dirty"] = new(TimeSpan.FromSeconds(2.0), canInterrupt: true),
        ["bored"] = new(TimeSpan.FromSeconds(2.0), canInterrupt: true),
        ["sad"] = new(TimeSpan.FromSeconds(3.0), canInterrupt: true),
        ["sleep"] = new(TimeSpan.FromSeconds(5.0), canInterrupt: true),
        ["happy"] = new(TimeSpan.FromSeconds(2.0), canInterrupt: true),
    };

    private static double GetFps(string state) => state switch
    {
        "walk" => 8.0,
        "idle" => 3.0,
        "eat" => 5.0,
        "clean" => 5.0,
        "study" => 4.0,
        "sleep" => 1.5,
        "faint" => 4.0,
        "celebrating" => 7.0,
        "happy" => 6.0,
        _ => 3.0,
    };

    private readonly Dictionary<string, int> _frameCounts;
    private readonly Func<DateTime> _clock;
    private readonly Random _random;
    private readonly string _logPath;

    private string? _transientState;
    private double _elapsed;
    private double _inactivitySeconds;
    private DateTime _wokenUntil = DateTime.MinValue;
    private DateTime _enteredAt;
    private DateTime _lastIdleFlavorChange = DateTime.MinValue;

    public string CurrentState { get; private set; } = "idle";
    public int CurrentFrame { get; private set; }
    public bool IsPlayingTransient => _transientState is not null;
    public string LastTransitionReason { get; private set; } = "initial";

    public AnimationService(
        IReadOnlyDictionary<string, int> frameCounts,
        Func<DateTime>? clock = null,
        Random? random = null,
        string? transitionLogPath = null)
    {
        _frameCounts = new Dictionary<string, int>(frameCounts);
        _clock = clock ?? (() => DateTime.Now);
        _random = random ?? new Random();
        _logPath = transitionLogPath ?? Path.Combine(AppContext.BaseDirectory, "tools", "artifacts", "logs", "sprite_state.log");
        _enteredAt = _clock();
    }

    public void TriggerTransient(string state)
    {
        if (!_frameCounts.ContainsKey(state)) return;

        _transientState = state;
        TransitionTo(state, $"transient:{state}", force: true);
    }

    public void WakeUp(double durationMinutes = 5.0)
    {
        _inactivitySeconds = 0;
        _wokenUntil = _clock().AddMinutes(durationMinutes);
    }

    public void Tick(double deltaSeconds, PetState petState, bool isMoving)
    {
        if (deltaSeconds <= 0) return;

        if (isMoving || _transientState is not null)
            _inactivitySeconds = 0;
        else
            _inactivitySeconds += deltaSeconds;

        if (_transientState is null)
        {
            bool forcedAwake = _clock() < _wokenUntil;
            var decision = ResolvePassiveDecision(petState, isMoving, _inactivitySeconds, forcedAwake, _clock(), _random, _lastIdleFlavorChange);
            if (decision.State != CurrentState && CanLeaveCurrentState(decision.Priority))
            {
                TransitionTo(decision.State, decision.Reason, force: false);
                if (decision.State is "cautious" or "booing")
                    _lastIdleFlavorChange = _clock();
            }
        }

        AdvanceFrame(deltaSeconds);
    }

    private void AdvanceFrame(double deltaSeconds)
    {
        double frameInterval = 1.0 / GetFps(CurrentState);
        _elapsed += deltaSeconds;

        if (_elapsed < frameInterval) return;

        _elapsed -= frameInterval;
        int frameCount = FrameCount(CurrentState);
        CurrentFrame = (CurrentFrame + 1) % frameCount;

        if (_transientState is not null && CurrentFrame == 0)
            _transientState = null;
    }

    private bool CanLeaveCurrentState(int nextPriority)
    {
        var now = _clock();
        var timing = GetTiming(CurrentState);
        if ((now - _enteredAt) < timing.MinDuration)
            return false;

        if (!timing.CanInterrupt && nextPriority <= GetPriority(CurrentState))
            return false;

        return true;
    }

    private void TransitionTo(string target, string reason, bool force)
    {
        if (!force && target == CurrentState)
            return;

        string from = CurrentState;
        CurrentState = target;
        CurrentFrame = 0;
        _elapsed = 0;
        _enteredAt = _clock();
        LastTransitionReason = reason;
        LogTransition(from, target, reason);
    }

    private void LogTransition(string fromState, string toState, string reason)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            string line = string.Create(CultureInfo.InvariantCulture, $"{_clock():O}\t{fromState}\t{toState}\t{reason}{Environment.NewLine}");
            File.AppendAllText(_logPath, line);
        }
        catch
        {
        }
    }

    private int FrameCount(string state) =>
        _frameCounts.TryGetValue(state, out int n) ? Math.Max(1, n) : 1;

    private static StateTiming GetTiming(string state) =>
        StateTimings.TryGetValue(state, out var timing) ? timing : new(TimeSpan.FromSeconds(0.5), canInterrupt: true);

    private static int GetPriority(string state) => state switch
    {
        "eat" or "clean" or "study" or "faint" or "celebrating" => 100,
        "walk" => 80,
        "sad" => 70,
        "hungry" or "dirty" or "bored" => 60,
        "sleep" => 50,
        "happy" => 40,
        "idle" or "cautious" or "booing" => 30,
        _ => 10,
    };

    internal static string ResolvePassiveState(
        PetState state,
        bool isMoving,
        double inactivitySeconds = 0,
        bool forcedAwake = false,
        DateTime? localNow = null,
        Random? random = null,
        DateTime? lastIdleFlavorChange = null)
        => ResolvePassiveDecision(
            state,
            isMoving,
            inactivitySeconds,
            forcedAwake,
            localNow ?? DateTime.Now,
            random ?? new Random(0),
            lastIdleFlavorChange ?? DateTime.MinValue).State;

    internal static TransitionDecision ResolvePassiveDecision(
        PetState state,
        bool isMoving,
        double inactivitySeconds,
        bool forcedAwake,
        DateTime now,
        Random random,
        DateTime lastIdleFlavorChange)
    {
        if (isMoving)
            return new("walk", "motion", 80);

        float minNeed = Math.Min(Math.Min(state.Hunger, state.Hygiene), state.Fun);

        if (minNeed < CriticalThreshold)
            return new("sad", "critical_need", 70);
        if (state.Hunger < WarningThreshold)
            return new("hungry", "low_hunger", 60);
        if (state.Hygiene < WarningThreshold)
            return new("dirty", "low_hygiene", 60);
        if (state.Fun < WarningThreshold)
            return new("bored", "low_fun", 60);

        if (!forcedAwake)
        {
            int hour = now.Hour;
            bool isScheduledSleep = hour >= 22 || hour < 8 || hour == 12 || hour == 16;
            bool isInactiveTooLong = inactivitySeconds >= InactivitySleepMinutes * 60;
            if (isScheduledSleep || isInactiveTooLong)
                return new("sleep", isScheduledSleep ? "schedule_sleep" : "inactivity_sleep", 50);
        }

        if (state.Hunger > HappyThreshold && state.Hygiene > HappyThreshold && state.Fun > HappyThreshold)
            return new("happy", "all_needs_high", 40);

        if ((now - lastIdleFlavorChange) >= TimeSpan.FromSeconds(5))
        {
            int roll = random.Next(0, 20);
            if (roll == 0) return new("cautious", "idle_flavor", 30);
            if (roll == 1) return new("booing", "idle_flavor", 30);
        }

        return new("idle", "default_idle", 30);
    }

    internal readonly record struct TransitionDecision(string State, string Reason, int Priority);
    private readonly record struct StateTiming(TimeSpan MinDuration, bool CanInterrupt);
}
