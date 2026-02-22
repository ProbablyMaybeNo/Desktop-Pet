using HuffleDesktopPet.Core.Models;
using HuffleDesktopPet.Core.Services;
using Xunit;

namespace HuffleDesktopPet.Tests;

/// <summary>
/// Verifies that PetState round-trips through JSON without data loss.
/// </summary>
public sealed class PetStateSerializationTests
{
    [Fact]
    public void Serialize_ThenDeserialize_PreservesAllFields()
    {
        // Arrange
        var original = new PetState
        {
            Hunger      = 75.5f,
            Hygiene     = 42.0f,
            Fun         = 88.3f,
            Knowledge   = 60.1f,
            LastUpdatedUtc = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            PositionX   = 0.25,
            PositionY   = 0.75,
        };

        // Act
        string json   = PetPersistence.Serialize(original);
        PetState copy = PetPersistence.Deserialize(json);

        // Assert
        Assert.Equal(original.Hunger,         copy.Hunger,         precision: 2);
        Assert.Equal(original.Hygiene,        copy.Hygiene,        precision: 2);
        Assert.Equal(original.Fun,            copy.Fun,            precision: 2);
        Assert.Equal(original.Knowledge,      copy.Knowledge,      precision: 2);
        Assert.Equal(original.LastUpdatedUtc, copy.LastUpdatedUtc);
        Assert.Equal(original.PositionX,      copy.PositionX,      precision: 4);
        Assert.Equal(original.PositionY,      copy.PositionY,      precision: 4);
    }

    [Fact]
    public void Serialize_ProducesValidJson()
    {
        var state  = new PetState();
        string json = PetPersistence.Serialize(state);

        Assert.False(string.IsNullOrWhiteSpace(json));
        Assert.Contains("hunger", json);      // camelCase from options
        Assert.Contains("hygiene", json);
        Assert.Contains("fun", json);
        Assert.Contains("knowledge", json);
    }

    [Fact]
    public void Deserialize_InvalidJson_ThrowsJsonException()
    {
        Assert.Throws<System.Text.Json.JsonException>(() =>
            PetPersistence.Deserialize("{bad json"));
    }
}
