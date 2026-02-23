using System.Threading;
using System.Windows;

namespace HuffleDesktopPet;

/// <summary>
/// Application entry point.
/// Manages the pet overlay window lifetime and enforces single-instance.
/// </summary>
public partial class App : Application
{
    private const string MutexName = "HuffleDesktopPet_SingleInstance";
    private Mutex? _instanceMutex;

    private PetOverlayWindow? _overlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Enforce single instance — prevents two pets writing to the same save file.
        _instanceMutex = new Mutex(initiallyOwned: true, name: MutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Huffle is already running.\nCheck your system tray.",
                "HuffleDesktopPet",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _overlay = new PetOverlayWindow();
        _overlay.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
