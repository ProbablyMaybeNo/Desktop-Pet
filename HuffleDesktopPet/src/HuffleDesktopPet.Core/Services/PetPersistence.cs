using System.Text.Json;
using HuffleDesktopPet.Core.Models;

namespace HuffleDesktopPet.Core.Services;

/// <summary>
/// Handles serialization and deserialization of <see cref="PetState"/> to/from JSON.
/// Default save location: %AppData%\HuffleDesktopPet\pet_state.json
/// </summary>
public static class PetPersistence
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DefaultSavePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HuffleDesktopPet",
            "pet_state.json");

    /// <summary>Serializes <paramref name="state"/> to JSON.</summary>
    public static string Serialize(PetState state) =>
        JsonSerializer.Serialize(state, s_options);

    /// <summary>Deserializes a <see cref="PetState"/> from JSON.</summary>
    /// <exception cref="JsonException">Thrown if the JSON is malformed.</exception>
    public static PetState Deserialize(string json) =>
        JsonSerializer.Deserialize<PetState>(json, s_options)
            ?? throw new JsonException("Deserialization returned null.");

    /// <summary>Saves <paramref name="state"/> to <paramref name="path"/>.</summary>
    public static async Task SaveAsync(PetState state, string? path = null)
    {
        path ??= DefaultSavePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, Serialize(state));
    }

    /// <summary>
    /// Loads state from <paramref name="path"/>.
    /// Returns a fresh <see cref="PetState"/> if the file does not exist.
    /// </summary>
    public static async Task<PetState> LoadAsync(string? path = null)
    {
        path ??= DefaultSavePath;
        if (!File.Exists(path))
            return new PetState();

        string json = await File.ReadAllTextAsync(path);
        return Deserialize(json);
    }
}
