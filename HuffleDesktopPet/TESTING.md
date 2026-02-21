# Testing Guide — HuffleDesktopPet

---

## Unit tests (automated)

Unit tests live in `src/HuffleDesktopPet.Tests/` and use **xUnit**.
They cover `HuffleDesktopPet.Core` only — no WPF, no UI automation.

### Run all tests

```powershell
# One-liner script
.\tools\scripts\test.ps1

# Direct dotnet CLI
dotnet test src\HuffleDesktopPet.Tests\HuffleDesktopPet.Tests.csproj --logger "console;verbosity=normal"
```

### Current test coverage (scaffold level)

| Test class | What it tests |
|---|---|
| `PetStateSerializationTests` | JSON round-trip, valid JSON shape, invalid JSON throws |
| `PetEngineTests` | 1-hour decay rates, zero-elapsed no-op, past-time guard, clamp at 0 |

All tests must pass on a fresh clone before any PR is merged.

---

## Manual test checklist — Milestone 0 (scaffold)

These are the baseline checks to confirm the scaffold is healthy.

### Build

- [ ] `dotnet build HuffleDesktopPet.sln` succeeds with 0 errors, 0 warnings
- [ ] All three projects build (App, Core, Tests)

### Launch

- [ ] `.\tools\scripts\run.ps1` builds and launches without crash
- [ ] A small purple circle appears on screen (placeholder pet)
- [ ] The window has no title bar or border
- [ ] The window is always on top of other windows
- [ ] Clicking and dragging the circle moves it

### Persistence

- [ ] Close the app, reopen — the pet appears in roughly the same screen location
- [ ] `%AppData%\HuffleDesktopPet\pet_state.json` is created on first run

### Unit tests

- [ ] `.\tools\scripts\test.ps1` reports all tests passed (green)

---

## Manual test checklist — Milestone A (overlay visible)

*(Will be completed when Milestone A is implemented)*

- [ ] Real sprite/PNG renders instead of placeholder ellipse
- [ ] Transparency is clean (no black halo / artifacting)
- [ ] Window is pixel-perfect — no drop shadow or window chrome visible
- [ ] Stays on top of all windows including full-screen apps (optional)

---

## Manual test checklist — Milestone B (click-through + tray)

- [ ] Right-clicking tray icon shows context menu (Show / Hide / Exit)
- [ ] "Exit" fully closes the process
- [ ] When click-through is enabled, clicks pass through the pet to the window behind
- [ ] When click-through is disabled, the pet can be dragged

---

## Manual test checklist — Milestone C (wandering)

- [ ] Pet moves autonomously at a gentle pace
- [ ] Pet does not walk off screen edges (clamped to monitor bounds)
- [ ] Movement looks smooth (no jitter)
- [ ] Pet pauses occasionally (idle animation or state)

---

## Adding new tests

1. Add `.cs` files to `src/HuffleDesktopPet.Tests/`.
2. Reference `HuffleDesktopPet.Core` only (never WPF assemblies).
3. Use `xunit` `[Fact]` or `[Theory]` attributes.
4. Run `.\tools\scripts\test.ps1` to confirm they pass.
