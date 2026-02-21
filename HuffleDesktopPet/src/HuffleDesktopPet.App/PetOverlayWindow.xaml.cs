using System.Windows;
using System.Windows.Input;
using HuffleDesktopPet.Core.Models;
using HuffleDesktopPet.Core.Services;

namespace HuffleDesktopPet;

/// <summary>
/// Transparent always-on-top overlay window that displays the pet.
/// Milestone A target: visible on screen.
/// Milestone B target: click-through, tray icon.
/// Milestone C target: autonomous wandering movement.
/// </summary>
public partial class PetOverlayWindow : Window
{
    private PetState _state = new();

    public PetOverlayWindow()
    {
        InitializeComponent();

        // Position near bottom-right of primary screen on first launch
        var screen = SystemParameters.WorkArea;
        Left = screen.Right - Width - 40;
        Top  = screen.Bottom - Height - 40;

        // Allow dragging the pet by clicking and dragging
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _state = await PetPersistence.LoadAsync();
        PetEngine.Tick(_state, DateTime.UtcNow);
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        await PetPersistence.SaveAsync(_state);
        Application.Current.Shutdown();
    }
}
