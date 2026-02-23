# DEVLOG — HuffleDesktopPet

Ongoing record of decisions, gotchas, and lessons learned.

---

## 2025-02-21 — Initial scaffold

### Decisions

**WPF chosen over WinUI 3**
- WPF's `AllowsTransparency="True"` + `WindowStyle="None"` + `Topmost="True"` gives a zero-friction transparent overlay without needing XAML Islands or the Desktop Bridge.
- WinUI 3 requires MSIX packaging or a newer app model for always-on-top transparent overlays and has more friction for a developer-only demo.
- Decision is not final — WinUI 3 will be reconsidered if WPF rendering quality is poor.

**Core library with no WPF dependency**
- `HuffleDesktopPet.Core` targets `net8.0-windows` but imports no WPF namespaces.
- This lets `PetEngine` and `PetPersistence` be unit-tested without spinning up a WPF dispatcher.
- The WPF app consumes Core as a project reference.

**Persistence via System.Text.Json**
- No external serialization library. `System.Text.Json` is built into .NET 8 and is fast.
- Save path: `%AppData%\HuffleDesktopPet\pet_state.json` — follows Windows app data conventions.
- If the file is missing on startup, a fresh `PetState` with defaults is used (graceful degradation).

**xUnit for tests**
- Standard in .NET ecosystem, integrates with VS Test Explorer and `dotnet test`.

**`global.json` `rollForward: latestMajor`**
- Pins to .NET 8 minimum but allows 9+ if installed. Prevents accidental use of a preview SDK.

**`Directory.Build.props`**
- Keeps shared settings (Nullable, ImplicitUsings, LangVersion) DRY across all three projects.
- Kept intentionally minimal — no complex MSBuild magic.

**PowerShell scripts**
- Chosen over `Makefile` because the target machine is Windows. PowerShell 5.1 is always available on Win 10/11.
- Scripts are in `tools/scripts/` and are self-contained (no external modules).

### Gotchas

**`ApplicationIcon` in App.csproj**
- References `Assets\huffle.ico`. This file does NOT exist yet — it is a placeholder.
- The project will still build (the `.ico` is optional for `WinExe` if the asset isn't included in the build).
- Add a real `.ico` before Milestone A to avoid a build warning.

**Click-through on WPF**
- WPF's `IsHitTestVisible="False"` only prevents WPF hit-testing, but Windows still receives `WM_NCHITTEST` hits.
- True Windows-level click-through requires P/Invoking `SetWindowLong` with `WS_EX_TRANSPARENT` on the window HWND.
- This is deferred to Milestone B — documented here to avoid the false expectation that XAML alone is sufficient.

**Single-monitor assumption for v1**
- `SystemParameters.WorkArea` returns the primary monitor's work area.
- Multi-monitor wander logic requires enumerating monitors via `Screen.AllScreens` (WinForms) or `Monitor` P/Invoke.
- Deferred to post-v1.

**WPF + .NET 8 on ARM64**
- Not tested. The scaffold targets `x64`. ARM64 may need a separate `Platforms` value.

---

---

## 2025-02-21 — Milestones B, C, D implemented

### Choices confirmed by user

| Question | Answer |
|---|---|
| WPF vs WinUI 3 | WPF |
| Monitor scope | Single monitor |
| Sprite format | Placeholder (ellipse) |
| Window style | Small moving window |
| Controls | Tray-only (no hotkey) |

### Milestone B decisions

**WinForms `NotifyIcon` over `Hardcodet.NotifyIcon.Wpf`**
- `<UseWindowsForms>true</UseWindowsForms>` unlocks `System.Windows.Forms` — zero extra NuGet packages.
- 16×16 tray icon painted in-memory via `System.Drawing.Bitmap` — no `.ico` file required.

**Click-through via P/Invoke `SetWindowLong`**
- `WS_EX_TRANSPARENT | WS_EX_LAYERED` is the only reliable OS-level click-through method.
- WPF's `IsHitTestVisible="False"` alone is insufficient — OS still routes clicks to the WPF HWND.
- HWND is available after `OnContentRendered` (constructor is too early).

### Milestone C decisions

**Pure-Core `WanderService`**
- No WPF dependency; bounds passed in from the UI layer.
- Seeded RNG enables deterministic unit tests.
- `FacingLeft` exposed for future sprite mirroring.
- `DispatcherPriority.Background` on the 33 ms timer prevents blocking drag/input.

### Milestone D decisions

**72-hour cap on elapsed decay**
- Guards against long hibernate, clock jumps, corrupted `LastUpdatedUtc`.
- Pet still fully depletes for any gap > ~10 hours; cap prevents float overflow.

**Atomic save via `File.Replace()`**
- Write `.tmp` → `File.Replace(.tmp → .json, backup: .json.bak)`.
- If process killed mid-write, previous `.json` is intact; `.bak` is the fallback.
- `LoadAsync` tries primary → backup → fresh state.

**Auto-save every 60 seconds**
- Fire-and-forget `_ = PetPersistence.SaveAsync(...)` in the needs timer.
- `OnClosed` awaits and also persists fractional window position.

---

## 2026-02-23 — Post-G audit fixes

Full codebase audit conducted. Six issues identified and resolved.

### Bug fixes

**Bug 1 — Play interaction had no animation**
- `OnInteract(Interaction.Play)` was calling `PetEngine.Play()` but had no `TriggerTransient()` call, leaving a comment as a placeholder.
- Fix: call `TriggerTransient("play")` if `huffle_play_*.png` frames are present; otherwise fall back to `"celebrating"` so there is always visible feedback. The fallback requires zero code change when a real play sprite sheet is added — just drop the PNGs in `assets/sprites/`.
- `"play"` added to `AnimationService.GetFps()` (7.0 fps) and to the `LoadSprites()` states array.

**Bug 2 — Knowledge decay had no passive visual state**
- Hunger → `hungry`, Hygiene → `dirty`, Fun → `bored` were all wired in `ResolvePassiveDecision`. Knowledge was tracked and persisted but never drove an animation.
- Fix: added `if (state.Knowledge < WarningThreshold) return new("bored", "low_knowledge", 60)` after the Fun branch. `"bored"` is the closest semantic fit (pet is under-stimulated).

**Bug 3 — AnimationService log defaulted to source-tree path**
- Default `_logPath` was `AppContext.BaseDirectory/tools/artifacts/logs/sprite_state.log`. In a `dotnet publish` output the exe lives next to `assets/sprites/`, not inside the source tree; the `tools/` directory doesn't exist there. The `catch {}` swallowed the failure silently, dropping all logs.
- Fix: default to `%AppData%\HuffleDesktopPet\logs\sprite_state.log` — same roaming-data location used by the save file.

**Bug 4 — No single-instance guard**
- Two concurrent instances both read/write `pet_state.json` via `File.Replace()`, creating a race that can corrupt the save. Two tray icons also appeared with no way to distinguish them.
- Fix: acquire a named `Mutex` (`"HuffleDesktopPet_SingleInstance"`) in `App.OnStartup`. If `createdNew == false` show an informational `MessageBox` and call `Shutdown()`. Mutex released and disposed in `OnExit`.

**Bug 5 — `WanderService.PixelsPerSecond` naming confusion**
- The constant was labelled "pixels" but `Window.Left/Top` and `SystemParameters.WorkArea` are both in WPF device-independent units (DIPs). The math was correct; the name was misleading.
- Fix: renamed to `SpeedDipsPerSecond`. No arithmetic change required.

**Bug 6 — Raw sprite sheets included in build output**
- The `*.png` glob in `App.csproj` captured the 8 raw GUID-named sheets in `assets/sprites/raw/`. They are not loaded at runtime (only `huffle_{state}_{nn}.png` files match the pattern in `LoadSprites`) but added unnecessary weight to every build output.
- Fix: added `Exclude="..\..\assets\sprites\raw\*.png"` to the `Content` item.

### New unit tests added

| Test class | New tests |
|---|---|
| `AnimationServiceTests` | `TriggerTransient_SetsCurrentState`, `TriggerTransient_UnknownState_DoesNotTransition`, `TriggerTransient_ClearsAfterAllFramesAdvance`, `Sleep_ScheduledAt22h`, `Sleep_ScheduledAt07h`, `Sleep_ScheduledAt12h`, `Sleep_AtNormalHour_DoesNotTrigger`, `Sleep_InactivityOverThreshold`, `Sleep_ForcedAwake_OverridesSchedule`, `LowKnowledge_TriggersBored`, `LowKnowledge_Reason_IsLowKnowledge` |
| `WanderServiceTests` | `SpeedDipsPerSecond_IsPositive`, `Tick_SpeedDoesNotExceedConstant` |

### TESTING.md updated

Added manual test checklists for Milestones E, F, and G (interactions, sprite animation, sleep/reactive states). Added a "Known limitations" section documenting single-monitor constraint, missing play sprite, scheduled naps, and lack of interaction cooldowns.
