using Microsoft.Win32;

namespace HuffleDesktopPet;

/// <summary>
/// Manages the "Start with Windows" registry entry for the current user.
/// Writes to HKCU\Software\Microsoft\Windows\CurrentVersion\Run — no admin required.
/// </summary>
internal static class StartupManager
{
    private const string AppName = "HuffleDesktopPet";
    private const string RunKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Returns true if a startup entry for this app already exists.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) is not null;
    }

    /// <summary>
    /// Adds (or updates) the startup registry entry pointing to <paramref name="exePath"/>.
    /// Path is quoted to handle spaces.
    /// </summary>
    public static void Enable(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.SetValue(AppName, $"\"{exePath}\"");
    }

    /// <summary>Removes the startup registry entry if it exists.</summary>
    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
