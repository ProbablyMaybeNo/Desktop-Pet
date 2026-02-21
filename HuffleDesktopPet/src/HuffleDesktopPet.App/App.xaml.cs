using System.Windows;

namespace HuffleDesktopPet;

/// <summary>
/// Application entry point.
/// Manages the pet overlay window lifetime.
/// </summary>
public partial class App : Application
{
    private PetOverlayWindow? _overlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _overlay = new PetOverlayWindow();
        _overlay.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
    }
}
