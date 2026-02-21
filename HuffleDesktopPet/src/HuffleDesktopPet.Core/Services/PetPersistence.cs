using System.Text.Json;
using HuffleDesktopPet.Core.Models;

namespace HuffleDesktopPet.Core.Services;

/// <summary>
/// Handles serialization and deserialization of <see cref="PetState"/> to/from JSON.
/// Default save location: %AppData%\HuffleDesktopPet\pet_state.json
///
/// Save strategy: write to a .tmp file first, then File.Replace() for atomicity.
/// This prevents data loss if the process is killed mid-write.
/// </summary>
public static class PetPersistence
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DefaultSavePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HuffleDesktopPet",
            "pet_state.json");

    /// <summary>Serializes <paramref name="state"/> to a JSON string.</summary>
    public static string Serialize(PetState state) =>
        JsonSerializer.Serialize(state, s_options);

    /// <summary>
    /// Deserializes a <see cref="PetState"/> from a JSON string.
    /// </summary>
    /// <exception cref="JsonException">Thrown if the JSON is malformed or null.</exception>
    public static PetState Deserialize(string json) =>
        JsonSerializer.Deserialize<PetState>(json, s_options)
            ?? throw new JsonException("Deserialization returned null.");

    /// <summary>
    /// Atomically saves <paramref name="state"/> to <paramref name="path"/>.
    /// Writes to a temp file first, then replaces the target — prevents corruption on crash.
    /// </summary>
    public static async Task SaveAsync(PetState state, string? path = null)
    {
        path ??= DefaultSavePath;
        string dir  = Path.GetDirectoryName(path)!;
        string tmp  = path + ".tmp";

        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(tmp, Serialize(state));

        // Atomic replace: tmp → path, backup → path.bak (overwritten each save)
        File.Replace(tmp, path, destinationBackupFileName: path + ".bak",
                     ignoreMetadataErrors: true);
    }

    /// <summary>
    /// Loads state from <paramref name="path"/>.
    /// Falls back to the .bak file if the primary is corrupted.
    /// Returns a fresh <see cref="PetState"/> if neither file exists.
    /// </summary>
    public static async Task<PetState> LoadAsync(string? path = null)
    {
        path ??= DefaultSavePath;

        if (File.Exists(path))
        {
            try
            {
                return Deserialize(await File.ReadAllTextAsync(path));
            }
            catch (JsonException)
            {
                // Primary corrupt — try backup
            }
        }

        string bak = path + ".bak";
        if (File.Exists(bak))
        {
            try
            {
                return Deserialize(await File.ReadAllTextAsync(bak));
            }
            catch (JsonException) { /* backup also corrupt */ }
        }

        return new PetState();
    }
}
