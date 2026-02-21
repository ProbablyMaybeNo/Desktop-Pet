using HuffleDesktopPet.Core.Models;
using HuffleDesktopPet.Core.Services;
using Xunit;

namespace HuffleDesktopPet.Tests;

/// <summary>
/// Verifies PetEngine interaction methods: Feed, Play, Clean, Study.
/// </summary>
public sealed class PetEngineInteractionTests
{
    // ── Feed ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Feed_IncreasesHunger_AndSlightlyReducesHygiene()
    {
        var state = new PetState { Hunger = 50f, Hygiene = 80f };
        PetEngine.Feed(state);
        Assert.Equal(90f, state.Hunger,  precision: 1);
        Assert.Equal(75f, state.Hygiene, precision: 1);
    }

    [Fact]
    public void Feed_ClampsHungerAt100()
    {
        var state = new PetState { Hunger = 90f, Hygiene = 100f };
        PetEngine.Feed(state);
        Assert.Equal(100f, state.Hunger);
    }

    [Fact]
    public void Feed_ClampsHygieneAtZero_WhenAlreadyLow()
    {
        var state = new PetState { Hunger = 50f, Hygiene = 3f };
        PetEngine.Feed(state);
        Assert.Equal(0f, state.Hygiene);
    }

    // ── Play ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Play_IncreasesFun_AndReducesHunger()
    {
        var state = new PetState { Fun = 40f, Hunger = 80f };
        PetEngine.Play(state);
        Assert.Equal(75f, state.Fun,    precision: 1);
        Assert.Equal(72f, state.Hunger, precision: 1);
    }

    [Fact]
    public void Play_ClampsFunAt100()
    {
        var state = new PetState { Fun = 90f, Hunger = 80f };
        PetEngine.Play(state);
        Assert.Equal(100f, state.Fun);
    }

    [Fact]
    public void Play_ClampsHungerAtZero_WhenAlreadyLow()
    {
        var state = new PetState { Fun = 50f, Hunger = 5f };
        PetEngine.Play(state);
        Assert.Equal(0f, state.Hunger);
    }

    // ── Clean ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Clean_IncreasesHygiene()
    {
        var state = new PetState { Hygiene = 30f };
        PetEngine.Clean(state);
        Assert.Equal(80f, state.Hygiene, precision: 1);
    }

    [Fact]
    public void Clean_ClampsHygieneAt100()
    {
        var state = new PetState { Hygiene = 80f };
        PetEngine.Clean(state);
        Assert.Equal(100f, state.Hygiene);
    }

    [Fact]
    public void Clean_DoesNotAffectOtherNeeds()
    {
        var state = new PetState { Hygiene = 30f, Hunger = 60f, Fun = 70f };
        PetEngine.Clean(state);
        Assert.Equal(60f, state.Hunger, precision: 1);
        Assert.Equal(70f, state.Fun,    precision: 1);
    }

    // ── Study ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Study_IncreasesKnowledge_AndSlightlyReducesFun()
    {
        var state = new PetState { Knowledge = 30f, Fun = 50f };
        PetEngine.Study(state);
        Assert.Equal(55f, state.Knowledge, precision: 1);
        Assert.Equal(45f, state.Fun,       precision: 1);
    }

    [Fact]
    public void Study_ClampsKnowledgeAt100()
    {
        var state = new PetState { Knowledge = 90f, Fun = 80f };
        PetEngine.Study(state);
        Assert.Equal(100f, state.Knowledge);
    }

    [Fact]
    public void Study_ClampsFunAtZero_WhenAlreadyLow()
    {
        var state = new PetState { Knowledge = 30f, Fun = 3f };
        PetEngine.Study(state);
        Assert.Equal(0f, state.Fun);
    }
}
