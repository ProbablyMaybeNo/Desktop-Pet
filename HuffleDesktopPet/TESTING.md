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

### Current test coverage

| Test class | What it tests |
|---|---|
| `PetStateSerializationTests` | JSON round-trip, valid JSON shape, invalid JSON throws |
| `PetEngineTests` | 1-hour decay rates, zero-elapsed no-op, past-time guard, clamp at 0, clock-skew cap |
| `PetEngineInteractionTests` | Feed / Play / Clean / Study deltas and clamp behaviour |
| `WanderServiceTests` | Bounds clamping, zero/negative delta, movement start, 500-tick bounds check, SetPosition, SpeedDipsPerSecond guard |
| `AnimationServiceTests` | Priority rules, hysteresis, walk min-duration, determinism, transition log, TriggerTransient lifecycle, sleep schedule hours, Knowledge → bored state |

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

## Manual test checklist — Milestone D (needs + persistence)

- [ ] After running the app for 2+ minutes, hover the pet — tooltip shows need values
- [ ] Close the app; reopen — need values are lower than at first launch (decay persisted)
- [ ] `%AppData%\HuffleDesktopPet\pet_state.json` is created on first run
- [ ] `%AppData%\HuffleDesktopPet\pet_state.json.bak` exists after second run
- [ ] Manually corrupt `pet_state.json` (delete some characters); app still opens using `.bak`
- [ ] Pet position is roughly preserved between restarts (within primary monitor bounds)

---

## Manual test checklist — Milestone E (interactions + startup toggle)

- [ ] Right-click the pet — context menu shows Feed / Play / Clean / Study
- [ ] **Feed**: Hunger rises, Hygiene drops slightly, "eat" animation plays
- [ ] **Play**: Fun rises, Hunger drops slightly, a play/celebrating animation plays
- [ ] **Clean**: Hygiene rises, "clean" animation plays
- [ ] **Study**: Knowledge rises, Fun drops slightly, "study" animation plays
- [ ] When all needs are above 70 after an interaction, "celebrating" animation fires
- [ ] Tray icon context menu also has Feed / Play / Clean / Study — all work the same
- [ ] "Start with Windows: ON/OFF" toggle in tray menu persists across reboots
- [ ] If the app is already running, launching it again shows a "Huffle is already running" message and exits — only one tray icon visible

---

## Manual test checklist — Milestone F (sprite animation)

- [ ] Sprites load: the placeholder ellipse is hidden and a pixel-art Huffle appears
- [ ] Transparency is clean — no black halo or rectangular background around the sprite
- [ ] Sprite flips horizontally when the pet moves left
- [ ] Walking animation plays while the pet is moving; idle animation plays while resting
- [ ] Hover tooltip still shows need values and current animation state + reason
- [ ] Tray icon changes to the actual idle sprite (not the purple circle placeholder)
- [ ] If `assets/sprites/` is absent or empty, app falls back to the placeholder ellipse without crashing

---

## Manual test checklist — Milestone G (sleep + reactive states)

### Need-based states
- [ ] When Hunger < 30, pet plays the "hungry" animation
- [ ] When Hygiene < 30, pet plays the "dirty" animation
- [ ] When Fun < 30, pet plays the "bored" animation
- [ ] When Knowledge < 30, pet plays the "bored" animation (low_knowledge reason in tooltip)
- [ ] When any need < 20, pet plays the "sad" animation

### Sleep schedule
- [ ] Between 22:00–08:00, pet automatically shows the "sleep" animation
- [ ] At 12:00 and 16:00, pet shows the "sleep" animation (scheduled nap)
- [ ] After 15 minutes without movement (no drag, no interaction), pet falls asleep
- [ ] Any interaction (Feed / Play / Clean / Study) wakes the pet for ~5 minutes

### Faint
- [ ] If any need bottoms out (< 1), the "faint" animation fires
- [ ] Faint is throttled — does not repeat more than once every 10 minutes

### Celebrating
- [ ] When all needs are > 70 after an interaction, the "celebrating" animation fires
- [ ] Celebrating does not interrupt an already-playing transient (eat / clean / study)

### Transition log
- [ ] `%AppData%\HuffleDesktopPet\logs\sprite_state.log` is created and grows as states change

---

## Known limitations (v1)

- **Single monitor only.** The pet is clamped to the primary monitor's work area.
  Multi-monitor support is deferred to post-v1.
- **No play-specific sprite yet.** Play interaction falls back to the "celebrating"
  animation until `huffle_play_*.png` frames are added to `assets/sprites/`.
- **Scheduled naps at 12:00 and 16:00.** These are by design (midday + afternoon nap)
  but may be surprising — the pet sleeps even with full needs at those hours.
- **No interaction cooldowns.** Needs can be maxed instantly by repeated clicks.

---

## Adding new tests

1. Add `.cs` files to `src/HuffleDesktopPet.Tests/`.
2. Reference `HuffleDesktopPet.Core` only (never WPF assemblies).
3. Use `xunit` `[Fact]` or `[Theory]` attributes.
4. Run `.\tools\scripts\test.ps1` to confirm they pass.
